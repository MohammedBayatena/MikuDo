using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

public class VaultGroup
{
    public string Name { get; init; } = string.Empty;
    public string CountLabel { get; init; } = string.Empty;
    public Brush Background { get; init; } = Brushes.Transparent;
    public Brush Foreground { get; init; } = Brushes.Gray;
}

public class VaultRow
{
    public TodoItem Item { get; init; } = null!;
    public string Name => Item.Title;
    public TodoPriority Priority => Item.Priority;
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public string PriorityName => TaskCardViewModel.PriorityText(Item.Priority);
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;
    public int CommentCount => Item.Comments.Count;
    public string Type { get; init; } = string.Empty;
    public string Modified => Item.UpdatedAt.ToLocalTime().ToString("d MMM yyyy");
    public string Access { get; init; } = "Private";
    public Brush AccessBackground { get; init; } = Brushes.Transparent;
    public Brush AccessForeground { get; init; } = Brushes.Gray;

    /// <summary>Title and description decrypted in place; done once, when the row is first shown.</summary>
    public bool IsRevealed { get; set; }
}

public partial class VaultViewModel : ObservableObject
{
    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private string _errorMessage = string.Empty;
    /// <summary>The rows on screen: the first pages of <see cref="_all"/>.</summary>
    [ObservableProperty] private ObservableCollection<VaultRow> _items = new();

    /// <summary>Every vault item, in order, still encrypted until shown.</summary>
    private List<VaultRow> _all = new();

    public bool HasMore => _all.Count > Items.Count;
    public string MoreLabel => Paging.MoreLabel(_all.Count - Items.Count);

    [RelayCommand]
    private void ShowMore()
    {
        foreach (var row in _all.Skip(Items.Count).Take(Paging.PageSize)) Items.Add(Reveal(row));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
    }
    [ObservableProperty] private ObservableCollection<VaultGroup> _groups = new();

    private readonly MainViewModel _main;

    public string Subtitle => IsUnlocked
        ? $"Encrypted tasks and notes. {_all.Count} item{(_all.Count == 1 ? "" : "s")}."
        : "Encrypted tasks and notes.";

    public string UnlockTitle => IsConfigured ? "Vault is locked" : "Set a master password";
    public string UnlockBlurb => IsConfigured
        ? "Enter your master password to unlock encrypted tasks and notes."
        : "Choose a master password. It encrypts everything in the vault and cannot be recovered.";
    public string UnlockButtonLabel => IsConfigured ? "Unlock vault" : "Create vault";
    public bool NeedsConfirmField => !IsConfigured;

    public VaultViewModel(MainViewModel main)
    {
        _main = main;
        _isConfigured = App.Database.IsVaultConfigured();
        _isUnlocked = App.Encryption.IsUnlocked;
        if (_isUnlocked) LoadItems();
    }

    partial void OnIsUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(UnlockTitle));
    }

    partial void OnIsConfiguredChanged(bool value)
    {
        OnPropertyChanged(nameof(UnlockTitle));
        OnPropertyChanged(nameof(UnlockBlurb));
        OnPropertyChanged(nameof(UnlockButtonLabel));
        OnPropertyChanged(nameof(NeedsConfirmField));
    }

    public void Refresh()
    {
        IsConfigured = App.Database.IsVaultConfigured();
        IsUnlocked = App.Encryption.IsUnlocked;
        if (IsUnlocked) LoadItems();
    }

    public void RefreshTheme()
    {
        if (IsUnlocked) LoadItems();
    }

    public void OnLockedExternally()
    {
        IsUnlocked = false;
        Items.Clear();
        Groups.Clear();
    }

    /// <summary>Unlock, or create the vault the first time. Passed from the view.</summary>
    public void Submit(string password, string confirm)
    {
        ErrorMessage = string.Empty;

        if (!IsConfigured)
        {
            if (password.Length < 4) { ErrorMessage = "Use at least 4 characters."; return; }
            if (password != confirm) { ErrorMessage = "The two passwords do not match."; return; }

            var (hash, salt, encSalt) = EncryptionService.HashPassword(password);
            App.Database.SaveVaultPassword(hash, salt, encSalt);
            App.Encryption.UnlockVault(password, encSalt);
            IsConfigured = true;
            IsUnlocked = true;
            LoadItems();
            return;
        }

        var stored = App.Database.GetVaultPassword();
        if (stored == null) { ErrorMessage = "The vault is not set up."; return; }

        var (storedHash, storedSalt, storedEncSalt) = stored.Value;
        if (!EncryptionService.VerifyPassword(password, storedHash, storedSalt))
        {
            ErrorMessage = "Incorrect password. Try again.";
            return;
        }

        App.Encryption.UnlockVault(password, storedEncSalt);
        IsUnlocked = true;
        LoadItems();
    }

    [RelayCommand]
    private void Lock()
    {
        App.Encryption.LockVault();
        IsUnlocked = false;
        Items.Clear();
        Groups.Clear();
        OnPropertyChanged(nameof(Subtitle));
    }

    [RelayCommand]
    private void NewItem() => _main.OpenNewTask(TodoStatus.Active, isVault: true, null, "Vault");

    [RelayCommand]
    private void OpenItem(VaultRow? row)
    {
        if (row != null) _main.OpenTask(row.Item, isVault: true, "Vault");
    }

    [RelayCommand]
    private void DeleteItem(VaultRow? row)
    {
        if (row == null) return;
        if (!DialogService.Confirm($"Permanently delete \"{row.Name}\"? This cannot be undone.", "Delete Vault Item"))
            return;

        App.Database.DeleteTodoPermanently(row.Item.Id);
        LoadItems();
    }

    /// <summary>
    /// Decrypts a row's title and description in place so the list can show
    /// them. The stored records keep their ciphertext; these are display copies.
    /// </summary>
    private static VaultRow Reveal(VaultRow row)
    {
        if (row.IsRevealed) return row;
        row.IsRevealed = true;

        var item = row.Item;
        try
        {
            if (item.EncryptionTitleIV != null)
                item.Title = App.Encryption.Decrypt(item.Title, item.EncryptionTitleIV);
            if (item.EncryptionIV != null)
                item.Description = App.Encryption.Decrypt(item.Description, item.EncryptionIV);
        }
        catch
        {
            item.Title = "[Decryption failed]";
            item.Description = string.Empty;
        }
        return row;
    }

    /// <summary>
    /// Loads every vault item, ordered and grouped from what is stored in the
    /// clear, and shows the first page. Only rows that are shown get decrypted.
    /// </summary>
    private void LoadItems()
    {
        if (!App.Encryption.IsUnlocked) return;

        var rows = new List<VaultRow>();
        foreach (var item in App.Database.GetVaultTodos())
        {
            var type = item.Tags.FirstOrDefault() ?? "Note";
            var shared = item.SubtaskIds.Count > 0;
            var access = shared ? "Checklist" : "Private";
            var colors = shared ? Palette.Label("Lead") : Palette.Priority(TodoPriority.None);

            rows.Add(new VaultRow
            {
                Item = item,
                Type = type,
                Access = access,
                AccessBackground = colors.Background,
                AccessForeground = colors.Foreground
            });
        }

        _all = rows;
        Items = new ObservableCollection<VaultRow>(rows.Take(Paging.PageSize).Select(Reveal));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
        Groups = new ObservableCollection<VaultGroup>(
            rows.GroupBy(r => r.Type)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g =>
                {
                    var colors = Palette.Label(g.Key);
                    return new VaultGroup
                    {
                        Name = g.Key,
                        CountLabel = g.Count() == 1 ? "1 item" : $"{g.Count()} items",
                        Background = colors.Background,
                        Foreground = colors.Foreground
                    };
                }));

        OnPropertyChanged(nameof(Subtitle));
    }
}
