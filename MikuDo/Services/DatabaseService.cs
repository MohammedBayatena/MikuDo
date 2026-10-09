using System.IO;
using LiteDB;
using MikuDo.Models;

namespace MikuDo.Services;

public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<TodoItem> _todos;
    private readonly ILiteCollection<Workspace> _workspaces;
    private readonly ILiteCollection<BsonDocument> _settings;
    private readonly ILiteStorage<string> _fileStorage;
    private readonly string _dbPath;

    public string GetDatabasePath() => _dbPath;

    private static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mikudo", "mikudo.db");

    public DatabaseService() : this(DefaultPath()) { }

    /// <summary>Opens the database at <paramref name="path"/>, such as a copy being inspected.</summary>
    public DatabaseService(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _dbPath = path;

        _db = new LiteDatabase($"Filename={_dbPath};Connection=direct");
        _todos = _db.GetCollection<TodoItem>("todos");
        _workspaces = _db.GetCollection<Workspace>("workspaces");
        _settings = _db.GetCollection<BsonDocument>("settings");
        _fileStorage = _db.FileStorage;

        _todos.EnsureIndex(x => x.Status);
        _todos.EnsureIndex(x => x.IsVault);
        _todos.EnsureIndex(x => x.WorkspaceId);
        _todos.EnsureIndex(x => x.CreatedAt);
        _workspaces.EnsureIndex(x => x.SortOrder);

        MigrateTextSubtasks();
        MigratePlacement();
    }

    /// <summary>
    /// Pins every task that exists before placement was recorded. Nothing
    /// stored says which of those the user dragged and which the app appended,
    /// so all of them keep the position they already show rather than any of
    /// them moving on their own. Runs once.
    /// </summary>
    private void MigratePlacement()
    {
        if (GetSetting("PlacementMigration") == "done") return;

        foreach (var todo in _todos.FindAll().ToList())
        {
            if (todo.IsPlaced) continue;
            todo.IsPlaced = true;
            _todos.Update(todo);
        }

        SaveSetting("PlacementMigration", "done");
    }

    /// <summary>
    /// Turns each free-text subtask into a real task in the same workspace and
    /// links it, so subtasks and tasks are one thing. Runs once.
    /// </summary>
    private void MigrateTextSubtasks()
    {
        if (GetSetting("SubtaskLinkMigration") == "done") return;

        foreach (var parent in _todos.FindAll().Select(x => x.Normalize()).ToList())
        {
            // Vault titles are ciphertext; a plaintext child would leak them.
            if (parent.IsVault || parent.LegacyTextSubtasks.Count == 0) continue;

            foreach (var legacy in parent.LegacyTextSubtasks)
            {
                if (string.IsNullOrWhiteSpace(legacy.Text)) continue;

                var child = new TodoItem
                {
                    Title = legacy.Text.Trim(),
                    Status = legacy.IsDone ? TodoStatus.Completed : TodoStatus.Active,
                    CompletedAt = legacy.IsDone ? parent.UpdatedAt : null,
                    WorkspaceId = parent.WorkspaceId,
                    SortOrder = GetNextSortOrder(parent.WorkspaceId,
                        legacy.IsDone ? TodoStatus.Completed : TodoStatus.Active)
                };
                _todos.Insert(child);
                parent.SubtaskIds.Add(child.IdText);
            }

            parent.LegacyTextSubtasks.Clear();
            _todos.Update(parent);
        }

        SaveSetting("SubtaskLinkMigration", "done");
    }

    // ── Workspaces ──────────────────────────────────────────────

    public List<Workspace> GetAllWorkspaces()
        => _workspaces.FindAll().OrderBy(x => x.SortOrder).ToList();

    public Workspace? GetWorkspaceById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try { return _workspaces.FindById(new ObjectId(id)); }
        catch { return null; }
    }

    public void UpsertWorkspace(Workspace ws) => _workspaces.Upsert(ws);

    /// <summary>"My Todos" is the default workspace and has no record of its own.</summary>
    public string GetDefaultWorkspaceName()
    {
        var name = GetSetting("DefaultWorkspaceName");
        return string.IsNullOrWhiteSpace(name) ? "My Todos" : name;
    }

    public void DeleteWorkspace(ObjectId id)
    {
        foreach (var todo in _todos.Find(x => x.WorkspaceId == id.ToString()))
        {
            todo.WorkspaceId = null;
            _todos.Update(todo);
        }
        _workspaces.Delete(id);
    }

    /// <summary>Everything not finished and not in the trash — the sidebar badge.</summary>
    public int GetWorkspaceOpenCount(string? workspaceId)
        => _todos.Count(x => !x.IsVault && x.WorkspaceId == workspaceId &&
                             (x.Status == TodoStatus.Active || x.Status == TodoStatus.Doing));

    /// <summary>
    /// The same count for every workspace at once, keyed by workspace id with
    /// "" for the default one. One pass instead of a scan per sidebar row.
    /// </summary>
    public Dictionary<string, int> GetWorkspaceOpenCounts()
    {
        var counts = new Dictionary<string, int>();

        foreach (var todo in _todos.Find(x => !x.IsVault &&
                                              (x.Status == TodoStatus.Active || x.Status == TodoStatus.Doing)))
        {
            var key = todo.WorkspaceId ?? "";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    public int GetTrashedCount() => _todos.Count(x => x.Status == TodoStatus.Trashed);

    // ── Board ───────────────────────────────────────────────────

    /// <summary>Every non-vault, non-trashed todo of a workspace, in board order.</summary>
    public List<TodoItem> GetBoardTodos(string? workspaceId)
        => TaskOrder.Default(
                _todos.Find(x => !x.IsVault && x.WorkspaceId == workspaceId && x.Status != TodoStatus.Trashed)
                      .Select(x => x.Normalize()))
            .ToList();

    /// <summary>Every non-vault, non-trashed todo across all workspaces.</summary>
    public List<TodoItem> GetAllBoardTodos()
        => TaskOrder.Default(
                _todos.Find(x => !x.IsVault && x.Status != TodoStatus.Trashed)
                      .Select(x => x.Normalize()))
            .ToList();

    public List<TodoItem> GetTrashedTodos()
        => TaskOrder.Unplaced(
                _todos.Find(x => x.Status == TodoStatus.Trashed)
                      .Select(x => x.Normalize()))
            .ToList();

    public TodoItem? GetTodoById(ObjectId id) => _todos.FindById(id)?.Normalize();

    /// <summary>Every task a subtask link can point at, keyed by id.</summary>
    public Dictionary<string, TodoItem> GetTodoLookup()
        => _todos.Find(x => !x.IsVault && x.Status != TodoStatus.Trashed)
                 .Select(x => x.Normalize())
                 .ToDictionary(x => x.IdText, x => x);

    /// <summary>The tasks among <paramref name="ids"/> that a subtask link can point at, keyed by id.</summary>
    public Dictionary<string, TodoItem> GetLinkableByIds(IEnumerable<string> ids)
    {
        var wanted = ObjectIds(ids);
        if (wanted.Count == 0) return new Dictionary<string, TodoItem>();

        return _todos.Query()
                     .Where(x => !x.IsVault && x.Status != TodoStatus.Trashed)
                     .Where(BsonExpression.Create("$._id IN @0", wanted))
                     .ToEnumerable()
                     .Select(x => x.Normalize())
                     .ToDictionary(x => x.IdText, x => x);
    }

    /// <summary>
    /// Every task beneath <paramref name="roots"/>, however deep: their
    /// subtasks, those tasks' subtasks and so on. Trashed ones are included
    /// unless <paramref name="includeTrashed"/> is false, which also leaves out
    /// whatever hangs beneath a trashed task; vault tasks are never part of a
    /// tree. Links can loop, so each task is visited once, and the roots
    /// themselves are never in the result.
    /// </summary>
    public List<TodoItem> GetSubtaskTree(IReadOnlyCollection<TodoItem> roots, bool includeTrashed = true)
    {
        var seen = roots.Select(r => r.IdText).ToHashSet(StringComparer.Ordinal);
        var found = new List<TodoItem>();
        var next = roots.SelectMany(r => r.SubtaskIds).Where(id => !seen.Contains(id)).ToList();

        while (next.Count > 0)
        {
            var wanted = ObjectIds(next);
            if (wanted.Count == 0) break;

            var level = _todos.Query()
                              .Where(x => !x.IsVault)
                              .Where(BsonExpression.Create("$._id IN @0", wanted))
                              .ToEnumerable()
                              .Select(x => x.Normalize())
                              .ToList();

            next = new List<string>();
            foreach (var task in level)
            {
                if (!seen.Add(task.IdText)) continue;
                if (!includeTrashed && task.Status == TodoStatus.Trashed) continue;
                found.Add(task);
                next.AddRange(task.SubtaskIds.Where(id => !seen.Contains(id)));
            }
        }

        return found;
    }

    /// <summary>The live tasks that hold <paramref name="id"/> as a subtask, oldest first. A task can sit under several.</summary>
    public List<TodoItem> GetParents(string id)
        => _todos.Query()
                 .Where(x => !x.IsVault && x.Status != TodoStatus.Trashed)
                 .Where(BsonExpression.Create("$.SubtaskIds[*] ANY = @0", id))
                 .ToEnumerable()
                 .Select(x => x.Normalize())
                 .OrderBy(x => x.CreatedAt)
                 .ThenBy(x => x.IdText, StringComparer.Ordinal)
                 .ToList();

    /// <summary>
    /// Every task above <paramref name="id"/>: its parents, their parents and
    /// so on. Linking any of them beneath it would make a loop.
    /// </summary>
    public HashSet<string> GetAncestorIds(string id)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            foreach (var parent in GetParents(queue.Dequeue()))
                if (parent.IdText != id && found.Add(parent.IdText)) queue.Enqueue(parent.IdText);
        }
        return found;
    }

    /// <summary>
    /// The order every page of linkable tasks is cut from: newest first, the
    /// id settling a tie. Created times are compared in UTC, so the hour that
    /// repeats when the clocks go back cannot swap two tasks.
    /// </summary>
    private const string NewestFirst = "FORMAT(TO_UTC($.CreatedAt), 'yyyyMMddHHmmssfff') + STRING($._id)";

    /// <summary>
    /// One page of the tasks in a workspace a subtask link could point at whose
    /// title contains <paramref name="query"/> in any case, newest first.
    /// <paramref name="workspaceKey"/> is "" for the default workspace or a
    /// workspace id. The same order on every call, so pages join up exactly.
    /// </summary>
    public List<TodoItem> SearchLinkable(string query, string workspaceKey, IReadOnlyCollection<string> exclude,
                                         int skip, int take)
        => Matching(query, workspaceKey, exclude)
            .OrderByDescending(NewestFirst)
            .Skip(skip)
            .Limit(take)
            .ToEnumerable()
            .Select(x => x.Normalize())
            .ToList();

    /// <summary>How many tasks <see cref="SearchLinkable"/> would page through.</summary>
    public int CountLinkable(string query, string workspaceKey, IReadOnlyCollection<string> exclude)
        => Matching(query, workspaceKey, exclude).Count();

    private ILiteQueryable<TodoItem> Matching(string query, string workspaceKey, IReadOnlyCollection<string> exclude)
    {
        var found = Linkable(exclude);
        if (query.Length > 0)
            found = found.Where(BsonExpression.Create("INDEXOF(LOWER($.Title), @0) >= 0", query.ToLowerInvariant()));
        var workspaceId = workspaceKey.Length == 0 ? null : workspaceKey;
        return found.Where(x => x.WorkspaceId == workspaceId);
    }

    /// <summary>The workspaces holding a task a subtask link could point at: "" is the default one.</summary>
    public HashSet<string> GetLinkableWorkspaceKeys(IReadOnlyCollection<string> exclude)
        => Linkable(exclude).Select(x => x.WorkspaceId).ToEnumerable()
                            .Select(id => id ?? "")
                            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Neither vault nor trash, and none of <paramref name="exclude"/>.</summary>
    private ILiteQueryable<TodoItem> Linkable(IReadOnlyCollection<string> exclude)
    {
        var query = _todos.Query().Where(x => !x.IsVault && x.Status != TodoStatus.Trashed);
        var skip = ObjectIds(exclude);
        return skip.Count == 0 ? query : query.Where(BsonExpression.Create("($._id IN @0) = false", skip));
    }

    /// <summary>The ids that are well-formed, as the database stores them.</summary>
    private static BsonArray ObjectIds(IEnumerable<string> ids)
        => new(ids.Where(id => id.Length == 24 && id.All(Uri.IsHexDigit))
                  .Distinct(StringComparer.Ordinal)
                  .Select(id => new BsonValue(new ObjectId(id))));

    public void UpsertTodo(TodoItem todo)
    {
        todo.UpdatedAt = DateTime.UtcNow;
        _todos.Upsert(todo);
    }

    public void DeleteTodoPermanently(ObjectId id)
    {
        var todo = _todos.FindById(id);
        if (todo == null) return;

        foreach (var imgId in todo.AttachedImages)
        {
            try { _fileStorage.Delete(imgId); } catch { }
        }
        foreach (var file in todo.Files)
        {
            try { _fileStorage.Delete(file.StorageId); } catch { }
        }

        UnlinkSubtaskEverywhere(todo.IdText);
        _todos.Delete(id);
    }

    /// <summary>Drops a subtask link from every task that holds it.</summary>
    private void UnlinkSubtaskEverywhere(string childId)
    {
        foreach (var parent in _todos.FindAll().Select(x => x.Normalize()).ToList())
        {
            if (parent.SubtaskIds.Remove(childId)) _todos.Update(parent);
        }
    }

    /// <summary>Next free slot at the bottom of a column.</summary>
    public double GetNextSortOrder(string? workspaceId, TodoStatus status)
    {
        var items = _todos.Find(x => !x.IsVault && x.WorkspaceId == workspaceId && x.Status == status).ToList();
        return items.Count == 0 ? 1000 : items.Max(x => x.SortOrder) + 1000;
    }

    // ── Vault TODOs ─────────────────────────────────────────────

    public List<TodoItem> GetVaultTodos()
        => TaskOrder.Unplaced(
                _todos.Find(x => x.IsVault && x.Status != TodoStatus.Trashed)
                      .Select(x => x.Normalize()))
            .ToList();

    public int GetVaultCount() => _todos.Count(x => x.IsVault && x.Status != TodoStatus.Trashed);

    public void UpsertVaultTodo(TodoItem todo)
    {
        todo.IsVault = true;
        todo.UpdatedAt = DateTime.UtcNow;
        _todos.Upsert(todo);
    }

    // ── Vault configuration ─────────────────────────────────────

    public bool IsVaultConfigured() => _settings.FindById("vault_password") != null;

    public void SaveVaultPassword(string hash, string salt, string encSalt)
        => _settings.Upsert(new BsonDocument
        {
            ["_id"] = "vault_password",
            ["hash"] = hash,
            ["salt"] = salt,
            ["encSalt"] = encSalt
        });

    public (string hash, string salt, string encSalt)? GetVaultPassword()
    {
        var doc = _settings.FindById("vault_password");
        if (doc == null) return null;
        return (doc["hash"].AsString, doc["salt"].AsString, doc["encSalt"].AsString);
    }

    // ── Settings ────────────────────────────────────────────────

    public string? GetSetting(string key) => _settings.FindById(key)?["value"].AsString;

    public void SaveSetting(string key, string value)
        => _settings.Upsert(new BsonDocument { ["_id"] = key, ["value"] = value });

    // ── Blob storage (images, voice memos, file attachments) ────

    public string StoreImage(Stream imageStream, string filename)
    {
        var id = $"$/images/{Guid.NewGuid()}/{filename}";
        _fileStorage.Upload(id, filename, imageStream);
        return id;
    }

    public Stream? GetImage(string imageId)
    {
        var info = _fileStorage.FindById(imageId);
        if (info == null) return null;
        var ms = new MemoryStream();
        info.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    public void DeleteImage(string imageId)
    {
        try { _fileStorage.Delete(imageId); } catch { }
    }

    public void Dispose() => _db?.Dispose();
}
