using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MikuDo.Models;
using LLama;
using LLama.Batched;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace MikuDo.Services;

/// <summary>One task proposed by the importer, before the user accepts it.</summary>
public class ExtractedTask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TodoStatus Status { get; set; } = TodoStatus.Active;
    public TodoPriority Priority { get; set; } = TodoPriority.None;
    public List<string> Labels { get; set; } = new();
    public List<string> Subtasks { get; set; } = new();

    /// <summary>Whether each of <see cref="Subtasks"/> was ticked in the note: "[x]" done, "[ ]" not.</summary>
    public List<bool> SubtasksDone { get; set; } = new();
}

/// <summary>
/// Turns free text into tasks using a small GGUF model on the CPU, via llama.cpp.
///
/// The work is split by what each half is actually good at:
///  • Code splits the text into one candidate per bullet or sentence, pulls
///    indented bullets out as subtasks, and writes the title and description.
///  • The model classifies each note under a GBNF grammar, so it can only emit
///    one of the values this app supports.
///  • Keyword rules decide status and priority wherever the note states the
///    answer outright ("already finished", "currently working on", "urgent",
///    "someday", or a bare imperative opener), and they own the labels outright.
///    Measured on hand-labelled notes the rules beat a model this size on those
///    cues; the model earns its place on notes that carry no cue at all.
///
/// With no model installed the rules run alone and the feature still works.
///
/// The few-shot prefix is identical for every note, so it is fed through a
/// <see cref="BatchedExecutor"/> once and each note runs on a fork of that
/// conversation — about 0.9 s per note instead of 6.4 s.
/// </summary>
public class AiImportService : IModelStore, IDisposable
{
    public string Purpose => "AI Import model";

    private const int MaxChunks = 25;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelDirectory;

    private LLamaWeights? _weights;
    private string? _loadedModelId;
    private bool _backendFailed;

    public AiModelOption Model => AiModelCatalog.Resolve(App.Database?.GetSetting("AiModel"));
    public string ModelPath => PathFor(Model);
    public bool IsModelDownloaded => Exists(Model);
    public bool IsModelLoaded => _weights != null;

    /// <summary>Why the last load or generation failed, for the UI to explain the fallback.</summary>
    public string? LastError { get; private set; }

    public AiImportService()
    {
        _modelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mikudo", "models");
        Directory.CreateDirectory(_modelDirectory);

        // llama.cpp writes progress to stderr otherwise.
        try { NativeLibraryConfig.All.WithLogCallback((_, _) => { }); } catch { }
    }

    public string PathFor(AiModelOption option) => Path.Combine(_modelDirectory, option.FileName);

    public bool Exists(AiModelOption option)
    {
        var path = PathFor(option);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
    }

    public void SelectModel(AiModelOption option)
    {
        if (option.Id == Model.Id) return;
        App.Database.SaveSetting("AiModel", option.Id);
        Unload();
    }

    /// <summary>Releases the weights so the file on disk can be replaced or deleted.</summary>
    public void Unload()
    {
        _weights?.Dispose();
        _weights = null;
        _loadedModelId = null;
        _backendFailed = false;
        LastError = null;
    }

    // ── Download ────────────────────────────────────────────────

    public Task DownloadModelAsync(AiModelOption option, IProgress<double> progress, CancellationToken ct)
        => ModelDownload.ToFileAsync(option, PathFor(option), progress, ct);

    // ── Extraction ──────────────────────────────────────────────

    public async Task<List<ExtractedTask>> ExtractAsync(
        string text, IProgress<string> status, CancellationToken ct)
    {
        var chunks = Segment(text);
        var results = new List<ExtractedTask>(chunks.Count);
        if (chunks.Count == 0) return results;

        // Rule pass first — it is the answer when there is no model, and the
        // tie-breaker when there is.
        var baseline = chunks.Select(c => Heuristic(c.Text)).ToList();

        var weights = await LoadWeightsAsync(status, ct);
        if (weights == null)
        {
            for (var i = 0; i < chunks.Count; i++) Finish(baseline[i], chunks[i]);
            return baseline;
        }

        var parameters = ContextParams();
        using var executor = new BatchedExecutor(weights, parameters);

        status.Report("Preparing the model…");
        var prefix = executor.Context.Tokenize(BuildPrefix(weights), addBos: true, special: true);
        var root = executor.Create();
        root.Prompt(prefix);
        while (executor.BatchedTokenCount > 0) await executor.Infer(ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Reading note {i + 1} of {chunks.Count}…");

            var task = baseline[i];
            var verdict = await ClassifyAsync(executor, weights, root, chunks[i].Text, ct);
            if (verdict != null) Merge(task, chunks[i].Text, verdict);

            Finish(task, chunks[i]);
            results.Add(task);
        }

        return results;
    }

    // ── Summaries ───────────────────────────────────────────────

    private const string SummaryPrompt =
        "Summarize the note as 3 to 6 short markdown bullet points. Keep names, numbers, " +
        "dates and decisions. Reply with the bullet points only.";

    /// <summary>Generated tokens allowed per summary: room for six bullets, no essays.</summary>
    private const int SummaryTokens = 260;

    /// <summary>
    /// Characters of the note given to the model. The context holds 4096 tokens;
    /// this keeps the prompt near 2,000 of them at roughly four characters a
    /// token, which bounds how long a summary takes on a CPU as much as it
    /// leaves room for the reply.
    /// </summary>
    private const int SummaryInputChars = 8000;

    /// <summary>
    /// Three to six bullet lines and nothing else. Small models asked for "a few
    /// bullets" happily write forty, or echo the note line by line until they
    /// run out of room; the grammar ends the reply after the sixth.
    /// </summary>
    private const string SummaryGbnf = """
        root   ::= bullet bullet bullet bullet? bullet? bullet?
        bullet ::= "- " [^\n]{8,150} "\n"
        """;

    /// <summary>
    /// A few bullet points standing in for a long note, or null with no model
    /// installed. Callers run this off the UI thread: decoding happens on
    /// whichever thread drives it.
    /// </summary>
    public async Task<string?> SummarizeAsync(string text, CancellationToken ct)
    {
        var weights = await LoadWeightsAsync(new Progress<string>(), ct);
        if (weights == null) return null;

        var parameters = ContextParams();
        using var executor = new BatchedExecutor(weights, parameters);

        var turn = new LLamaTemplate(weights) { AddAssistant = true };
        turn.Add("system", SummaryPrompt);
        turn.Add("user", Truncate(text, SummaryInputChars));
        var tokens = executor.Context.Tokenize(Encoding.UTF8.GetString(turn.Apply()), addBos: true, special: true);

        using var conversation = executor.Create();

        // A prompt longer than one batch is fed a batch at a time; the decoder
        // rejects more tokens per call than the batch was sized for.
        var batch = (int)parameters.BatchSize;
        for (var i = 0; i < tokens.Length; i += batch)
        {
            conversation.Prompt(tokens.Skip(i).Take(batch).ToArray());
            await executor.Infer(ct);
        }

        using var pipeline = new DefaultSamplingPipeline
        {
            Temperature = 0.2f,
            RepeatPenalty = 1.3f,
            Grammar = new Grammar(SummaryGbnf, "root")
        };
        var decoder = new StreamingTokenDecoder(executor.Context);

        for (var n = 0; n < SummaryTokens; n++)
        {
            ct.ThrowIfCancellationRequested();
            var token = conversation.Sample(pipeline);
            if (token.IsEndOfGeneration(weights.Vocab)) break;

            decoder.Add(token);
            conversation.Prompt(token);
            await executor.Infer(ct);
        }

        return Bullets(decoder.Read());
    }

    // ── Dictation write-up ──────────────────────────────────────

    private const string DescribePrompt =
        "You turn dictated speech into a task description. Keep every fact, name, number and date that was said. " +
        "Drop filler words, false starts and repeats, and fix the punctuation. Write short paragraphs, or '- ' " +
        "bullet points where the speaker lists several things. Add nothing that was not said. " +
        "Reply with the description only.";

    /// <summary>
    /// Dictated words written up as a description, or null with no model
    /// installed or when the reply cannot be trusted. Callers run this off the
    /// UI thread, as with <see cref="SummarizeAsync"/>.
    /// </summary>
    public Task<string?> DescribeAsync(string transcript, CancellationToken ct)
        => RewriteWithAsync(DescribePrompt, transcript, ct);

    private const string RewritePrompt =
        "Rewrite the text so it reads clearly. Keep every fact, name, number, date, link and checkbox, and keep " +
        "its Markdown: headings, lists and code stay as they are. Fix spelling, grammar and awkward wording. " +
        "Keep any @@number@@ marker exactly where it belongs. Add nothing that was not there. " +
        "Reply with the rewritten text only.";

    /// <summary>The longest text Rewrite takes in one go; longer, the user rewrites a selection.</summary>
    public const int RewriteMaxChars = SummaryInputChars;

    /// <summary>
    /// Text rewritten to read better, or null with no model installed or when
    /// the reply cannot be trusted. Callers run this off the UI thread.
    /// </summary>
    public Task<string?> RewriteAsync(string text, CancellationToken ct)
        => RewriteWithAsync(RewritePrompt, text, ct);

    /// <summary>The model's version of <paramref name="text"/> under <paramref name="prompt"/>, if it holds up.</summary>
    private async Task<string?> RewriteWithAsync(string prompt, string text, CancellationToken ct)
    {
        var weights = await LoadWeightsAsync(new Progress<string>(), ct);
        if (weights == null) return null;

        var parameters = ContextParams();
        using var executor = new BatchedExecutor(weights, parameters);

        var turn = new LLamaTemplate(weights) { AddAssistant = true };
        turn.Add("system", prompt);
        turn.Add("user", Truncate(text, SummaryInputChars));
        var tokens = executor.Context.Tokenize(Encoding.UTF8.GetString(turn.Apply()), addBos: true, special: true);

        using var conversation = executor.Create();
        var batch = (int)parameters.BatchSize;
        for (var i = 0; i < tokens.Length; i += batch)
        {
            conversation.Prompt(tokens.Skip(i).Take(batch).ToArray());
            await executor.Infer(ct);
        }

        // A rewrite needs room for about as many words as were said, and a
        // light repeat penalty: a heavy one pushes the model off the speaker's words.
        using var pipeline = new DefaultSamplingPipeline { Temperature = 0.2f, RepeatPenalty = 1.08f };
        var decoder = new StreamingTokenDecoder(executor.Context);
        var budget = Math.Clamp(text.Length / 2, 96, 1500);

        for (var n = 0; n < budget; n++)
        {
            ct.ThrowIfCancellationRequested();
            var token = conversation.Sample(pipeline);
            if (token.IsEndOfGeneration(weights.Vocab)) break;

            decoder.Add(token);
            conversation.Prompt(token);
            await executor.Infer(ct);
        }

        var reply = decoder.Read().Trim().Trim('`').Trim();
        return Faithful(reply, text) ? reply : null;
    }

    /// <summary>
    /// A small model can drop most of what it was told or ramble on past it.
    /// A write-up far shorter or far longer than the speech is not used.
    /// </summary>
    private static bool Faithful(string reply, string transcript)
        => reply.Length >= transcript.Length * 0.35 && reply.Length <= transcript.Length * 2.5 + 200;

    /// <summary>
    /// The reply's distinct bullet lines, at most six. The grammar shapes the
    /// reply, but a small model can still say the same thing twice.
    /// </summary>
    private static string? Bullets(string reply)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = reply.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") && l.Length > 4)
            .Where(l => seen.Add(l[2..].Trim().TrimEnd('.')))
            .Take(6)
            .ToList();

        return kept.Count == 0 ? null : string.Join("\n", kept);
    }

    private async Task<LLamaWeights?> LoadWeightsAsync(IProgress<string> status, CancellationToken ct)
    {
        var option = Model;
        if (!Exists(option)) return null;
        if (_backendFailed && _loadedModelId == option.Id) return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (_weights != null && _loadedModelId == option.Id) return _weights;

            Unload();
            status.Report($"Loading {option.DisplayName}…");
            _weights = await LLamaWeights.LoadFromFileAsync(new ModelParams(PathFor(option))
            {
                ContextSize = 4096,
                GpuLayerCount = 0
            }, ct);
            _loadedModelId = option.Id;
            return _weights;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _backendFailed = true;
            _loadedModelId = option.Id;
            LastError = ex.Message;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ModelParams ContextParams() => new(ModelPath)
    {
        ContextSize = 4096,
        GpuLayerCount = 0,
        BatchSize = 512
    };

    // ── Model call ──────────────────────────────────────────────

    /// <summary>The model can only produce values this app understands.</summary>
    private const string Gbnf = """
        root   ::= "{\"status\":\"" status "\",\"priority\":\"" prio "\",\"labels\":[" labels "]}"
        status ::= "To do" | "Doing" | "Done"
        prio   ::= "None" | "Low" | "Medium" | "High"
        labels ::= "" | label ("," label){0,2}
        label  ::= "\"Internal\"" | "\"Urgent\"" | "\"Lead\""
        """;

    private const string SystemPrompt =
        "Label the note. status is Done if it already happened, Doing if it is under way, " +
        "otherwise To do. Reply with JSON only.";

    // Worked examples, as real conversation turns. Given the same content as one
    // flat block of text, small models answer with whichever example came last.
    private static readonly (string Note, string Json)[] Shots =
    {
        ("Call the supplier about the delayed shipment, needed today",
         """{"status":"To do","priority":"High","labels":["Urgent","Lead"]}"""),
        ("Finished writing the team handbook last week",
         """{"status":"Done","priority":"None","labels":["Internal"]}"""),
        ("Still building the new dashboard screen",
         """{"status":"Doing","priority":"Medium","labels":[]}"""),
        ("Maybe try a new logo font one day",
         """{"status":"To do","priority":"Low","labels":[]}"""),
        ("We shipped the pricing page yesterday",
         """{"status":"Done","priority":"None","labels":[]}"""),
        ("I am halfway through the migration script",
         """{"status":"Doing","priority":"Medium","labels":[]}"""),
        ("Tidy up the README wording at some point",
         """{"status":"To do","priority":"Low","labels":[]}"""),
        ("Review the vendor contract before Monday",
         """{"status":"To do","priority":"Medium","labels":["Lead"]}"""),
        ("Currently drafting the Q3 roadmap with the team",
         """{"status":"Doing","priority":"Medium","labels":["Internal"]}"""),
        ("Sent the signed NDA to the customer this morning",
         """{"status":"Done","priority":"None","labels":["Lead"]}"""),
        ("The login page is broken for everyone, fix it now",
         """{"status":"To do","priority":"High","labels":["Urgent"]}"""),
        ("Plan the team lunch for next month",
         """{"status":"To do","priority":"Low","labels":["Internal"]}""")
    };

    private static string BuildPrefix(LLamaWeights weights) => BuildPrefix(weights, SystemPrompt, Shots);

    /// <summary>A system turn and worked examples, the part every note shares.</summary>
    private static string BuildPrefix(LLamaWeights weights, string system, IEnumerable<(string Note, string Reply)> shots)
    {
        var template = new LLamaTemplate(weights) { AddAssistant = false };
        template.Add("system", system);
        foreach (var (note, reply) in shots)
        {
            template.Add("user", note);
            template.Add("assistant", reply);
        }
        return Encoding.UTF8.GetString(template.Apply());
    }

    private record Verdict(TodoStatus Status, TodoPriority Priority, List<string> Labels);

    private async Task<Verdict?> ClassifyAsync(
        BatchedExecutor executor, LLamaWeights weights, Conversation root, string note, CancellationToken ct)
    {
        var reply = await AnswerAsync(executor, weights, root, note, Gbnf, 64, ct);
        return reply == null ? null : Parse(reply);
    }

    /// <summary>
    /// The model's reply to <paramref name="note"/>, on a fork of the worked
    /// examples in <paramref name="root"/> and held to <paramref name="gbnf"/>;
    /// null if generating it failed.
    /// </summary>
    private async Task<string?> AnswerAsync(
        BatchedExecutor executor, LLamaWeights weights, Conversation root, string note, string gbnf,
        int maxTokens, CancellationToken ct)
    {
        try
        {
            var turn = new LLamaTemplate(weights) { AddAssistant = true };
            turn.Add("user", Truncate(note, 300));
            var suffix = executor.Context.Tokenize(
                Encoding.UTF8.GetString(turn.Apply()), addBos: false, special: true);

            using var fork = root.Fork();
            fork.Prompt(suffix);

            using var pipeline = new DefaultSamplingPipeline
            {
                Temperature = 0f,
                Grammar = new Grammar(gbnf, "root")
            };

            var decoder = new StreamingTokenDecoder(executor.Context);
            for (var i = 0; i < maxTokens; i++)
            {
                await executor.Infer(ct);
                var token = fork.Sample(pipeline);
                if (token.IsEndOfGeneration(weights.Vocab)) break;
                decoder.Add(token);
                fork.Prompt(token);
            }

            return decoder.Read();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    // ── Label suggestions ───────────────────────────────────────

    /// <summary>The labels suggestions choose between: what kind of task it is.</summary>
    public static IReadOnlyList<string> KindLabels => Palette.QuickLabels;

    private const string KindPrompt =
        "Say what kind of task this is. Bug: something is broken or wrong. Feature: something new to add. " +
        "Enhancement: something that exists, made better. Think About: an idea or a question to weigh up. " +
        "None: none of these. Reply with the kind only.";

    /// <summary>One of the kinds, or None; nothing else can come out.</summary>
    private const string KindGbnf = """
        root ::= "Bug" | "Feature" | "Enhancement" | "Think About" | "None"
        """;

    private static readonly (string Note, string Reply)[] KindShots =
    {
        ("Login page crashes when the password field is empty", "Bug"),
        ("Add dark mode to the settings page", "Feature"),
        ("Make the search results load faster", "Enhancement"),
        ("Maybe we should move the docs to a wiki?", "Think About"),
        ("Invoice totals are wrong after a refund", "Bug"),
        ("Let users export their data as CSV", "Feature"),
        ("Tidy up the wording on the onboarding screens", "Enhancement"),
        ("Consider switching the job queue to Redis", "Think About"),
        ("The export button does nothing on Safari", "Bug"),
        ("Sign in with Google", "Feature"),
        ("Reduce the padding on the dashboard cards", "Enhancement"),
        ("Buy milk", "None")
    };

    /// <summary>
    /// A kind label for each task, or null where neither the rules nor the
    /// model can tell. Each task is given as its title and description.
    /// </summary>
    /// <remarks>
    /// The rules settle any task that names its kind outright ("crashes",
    /// "add support for", "maybe"), and only when exactly one kind is named.
    /// The model reads the rest. With no model installed the rules run alone.
    /// Callers run this off the UI thread.
    /// </remarks>
    public async Task<List<string?>> SuggestKindsAsync(
        IReadOnlyList<string> tasks, IProgress<string> status, CancellationToken ct)
    {
        var kinds = tasks.Select(KindCue).ToList();
        var open = Enumerable.Range(0, tasks.Count).Where(i => kinds[i] == null).ToList();
        if (open.Count == 0) return kinds;

        var weights = await LoadWeightsAsync(status, ct);
        if (weights == null) return kinds;

        var parameters = ContextParams();
        using var executor = new BatchedExecutor(weights, parameters);

        status.Report("Preparing the model…");
        var prefix = executor.Context.Tokenize(BuildPrefix(weights, KindPrompt, KindShots), addBos: true, special: true);
        var root = executor.Create();
        root.Prompt(prefix);
        while (executor.BatchedTokenCount > 0) await executor.Infer(ct);

        // Each answer opens with its own token, so one step of the model scores
        // them all. Where two shared a token, the answer is generated instead.
        var openers = KindAnswers
            .Select(a => executor.Context.Tokenize(a, addBos: false, special: false).FirstOrDefault())
            .ToArray();
        var scored = openers.Distinct().Count() == openers.Length;

        // What the model leans to with nothing to go on. A model this small
        // favours one answer for almost any task; dividing that lean out lets
        // the task itself decide.
        float[]? lean = null;
        if (scored)
        {
            var neutral = new List<float[]>();
            foreach (var blank in NeutralNotes)
                if (await AnswerOddsAsync(executor, weights, root, blank, openers, ct) is { } odds) neutral.Add(odds);
            if (neutral.Count > 0)
                lean = Enumerable.Range(0, KindAnswers.Length)
                                 .Select(i => MathF.Log(neutral.Average(o => MathF.Exp(o[i]))))
                                 .ToArray();
        }

        for (var n = 0; n < open.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Reading task {n + 1} of {open.Count}…");

            string? reply;
            if (scored)
            {
                var odds = await AnswerOddsAsync(executor, weights, root, tasks[open[n]], openers, ct);
                reply = odds == null ? null : KindAnswers[Best(odds, lean)];
            }
            else
            {
                reply = (await AnswerAsync(executor, weights, root, tasks[open[n]], KindGbnf, 8, ct))?.Trim();
            }

            if (reply != null && KindLabels.Contains(reply)) kinds[open[n]] = reply;
        }

        return kinds;
    }

    /// <summary>Everything the model may answer, None included, in the order the odds are kept.</summary>
    private static readonly string[] KindAnswers = { "Bug", "Feature", "Enhancement", "Think About", "None" };

    /// <summary>Notes that say nothing, to measure what the model answers by default.</summary>
    private static readonly string[] NeutralNotes = { "N/A", "-", "Task" };

    /// <summary>
    /// How much of the model's default lean is taken out of its odds. All of
    /// it overcorrects: the model then swings to another answer for almost
    /// everything. Half was best on hand-labelled tasks, about seven in ten
    /// right with the rules, against under five in ten for always "Bug".
    /// </summary>
    private const float LeanWeight = 0.5f;

    /// <summary>The answer with the best odds once part of the model's default lean is taken out.</summary>
    private static int Best(float[] odds, float[]? lean)
    {
        float Score(int i) => odds[i] - LeanWeight * (lean?[i] ?? 0);

        var best = 0;
        for (var i = 1; i < odds.Length; i++)
            if (Score(i) > Score(best)) best = i;
        return best;
    }

    /// <summary>
    /// The model's log odds for each answer's opening token after <paramref name="note"/>,
    /// normalised over those answers alone; null if the model could not run.
    /// </summary>
    private async Task<float[]?> AnswerOddsAsync(
        BatchedExecutor executor, LLamaWeights weights, Conversation root, string note,
        LLamaToken[] openers, CancellationToken ct)
    {
        try
        {
            var turn = new LLamaTemplate(weights) { AddAssistant = true };
            turn.Add("user", Truncate(note, 300));
            var suffix = executor.Context.Tokenize(
                Encoding.UTF8.GetString(turn.Apply()), addBos: false, special: true);

            using var fork = root.Fork();
            fork.Prompt(suffix);
            await executor.Infer(ct);

            var logits = fork.Sample();
            var scores = new float[openers.Length];
            for (var i = 0; i < openers.Length; i++) scores[i] = logits[(int)openers[i]];
            var top = scores.Max();
            var total = MathF.Log(scores.Sum(x => MathF.Exp(x - top)));
            return scores.Select(x => x - top - total).ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private static Verdict? Parse(string json)
    {
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var document = JsonDocument.Parse(json.Substring(start, end - start + 1));
            var root = document.RootElement;

            var labels = new List<string>();
            if (root.TryGetProperty("labels", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name) && !labels.Contains(name)) labels.Add(name!);
                }
            }

            return new Verdict(
                Palette.StatusFromName(root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : ""),
                ParsePriority(root.TryGetProperty("priority", out var p) ? p.GetString() : null),
                labels);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TodoPriority ParsePriority(string? value) => value switch
    {
        "High" => TodoPriority.High,
        "Medium" => TodoPriority.Medium,
        "Low" => TodoPriority.Low,
        _ => TodoPriority.None
    };

    /// <summary>
    /// Takes the model's status and priority only where the note does not state
    /// the answer itself.
    ///
    /// Labels stay with the rules. Measured against hand-labelled notes the rules
    /// got every label right on their own, while the model reached for "Internal"
    /// whenever it was unsure — so its labels cost more in wrong chips than they
    /// were worth. Every chip a user sees now traces back to a word in their note.
    /// </summary>
    private static void Merge(ExtractedTask task, string note, Verdict verdict)
    {
        var lower = note.ToLowerInvariant();

        if (StatusCue(lower) == null) task.Status = verdict.Status;
        if (PriorityCue(lower) == null) task.Priority = verdict.Priority;
    }

    /// <summary>
    /// Settles what the note states outright. A checkbox is the writer saying
    /// whether the task is done, so it overrides both the keyword rules and the
    /// model: "[x]" is Done, "[ ]" is To do.
    /// </summary>
    private static void Finish(ExtractedTask task, Chunk chunk)
    {
        if (chunk.Ticked is { } ticked) task.Status = ticked ? TodoStatus.Completed : TodoStatus.Active;
        task.Subtasks = chunk.Subtasks;
        task.SubtasksDone = chunk.SubtasksTicked;
    }

    // ── Segmentation ────────────────────────────────────────────

    private class Chunk
    {
        public string Text = string.Empty;

        /// <summary>True for "[x]", false for "[ ]", null when the line has no checkbox.</summary>
        public bool? Ticked;

        public List<string> Subtasks = new();
        public List<bool> SubtasksTicked = new();
    }

    /// <summary>
    /// A list item's marker: a bullet or number, a checkbox, or a bullet then a
    /// checkbox as in "- [x] Done thing". "[]" counts as an empty box.
    /// </summary>
    private static readonly Regex BulletRe = new(
        @"^\s*(?:(?:[-*+•‣–]|\d+[.)])\s+(?:\[[ xX]?\]\s*)?|\[[ xX]?\]\s*)", RegexOptions.Compiled);

    private static readonly Regex CheckboxRe = new(@"^\s*(?:(?:[-*+•‣–]|\d+[.)])\s+)?\[([ xX]?)\]", RegexOptions.Compiled);

    /// <summary>True when the line's checkbox is ticked, false when empty, null with no checkbox.</summary>
    private static bool? Ticked(string line)
    {
        var box = CheckboxRe.Match(line);
        return box.Success ? box.Groups[1].Value is "x" or "X" : null;
    }
    private static readonly Regex HeadingRe = new(@"^\s*(?:#{1,6}\s+.*|.{1,60}:)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// One candidate per task. Bulleted lines are tasks and bullets indented
    /// under them become that task's subtasks; prose with no bullets is split
    /// by sentence.
    /// </summary>
    private static List<Chunk> Segment(string text)
    {
        var chunks = new List<Chunk>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var bulleted = lines.Where(l => BulletRe.IsMatch(l)).ToList();

        if (bulleted.Count > 0)
        {
            var baseIndent = bulleted.Min(Indent);
            Chunk? current = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (BulletRe.IsMatch(line))
                {
                    var body = BulletRe.Replace(line, "").Trim();
                    if (body.Length == 0) continue;

                    if (Indent(line) > baseIndent && current != null)
                    {
                        if (current.Subtasks.Count < 20)
                        {
                            current.Subtasks.Add(Truncate(body, 120));
                            current.SubtasksTicked.Add(Ticked(line) == true);
                        }
                    }
                    else if (chunks.Count < MaxChunks)
                    {
                        current = new Chunk { Text = body, Ticked = Ticked(line) };
                        chunks.Add(current);
                    }
                }
                else if (!HeadingRe.IsMatch(line) && current != null && Indent(line) > baseIndent)
                {
                    // A wrapped continuation line belongs to the bullet above it.
                    current.Text = Truncate(current.Text + " " + line.Trim(), 400);
                }
            }
        }
        else
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || HeadingRe.IsMatch(line)) continue;

                foreach (var sentence in SplitSentences(trimmed))
                {
                    if (chunks.Count >= MaxChunks) break;
                    if (sentence.Length >= 3) chunks.Add(new Chunk { Text = Truncate(sentence, 400) });
                }
            }
        }

        return chunks;
    }

    private static int Indent(string line)
    {
        var n = 0;
        foreach (var c in line)
        {
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        }
        return n;
    }

    private static IEnumerable<string> SplitSentences(string text)
        => Regex.Split(text, @"(?<=[.!?;])\s+").Select(s => s.Trim().TrimEnd('.', ';')).Where(s => s.Length > 0);

    private static string FirstSentence(string text)
        => SplitSentences(text).FirstOrDefault() ?? text;

    // ── Keyword rules ───────────────────────────────────────────

    // A note that opens with a bare imperative is a thing still to be done,
    // whatever else it mentions. Checked after the past- and present-tense cues
    // so "Fix the crash — shipped yesterday" still reads as finished.
    private static readonly string[] ImperativeOpeners =
    {
        "add", "book", "build", "call", "chase", "check", "confirm", "create", "draft", "email",
        "finish", "fix", "follow up", "get", "look into", "make", "move", "plan", "prepare",
        "publish", "refactor", "remove", "rename", "reply", "review", "rewrite", "send", "set up",
        "ship", "sort out", "update", "upgrade", "write"
    };

    private static TodoStatus? StatusCue(string lower)
    {
        if (Has(lower, "already finished", "already done", "finished", "completed", "wrapped up",
                "shipped", "sent it out", "signed off", "closed out", "delivered"))
            return TodoStatus.Completed;

        if (Has(lower, "currently", "in the middle of", "working on", "in progress", "underway",
                "halfway", "started on", "ongoing"))
            return TodoStatus.Doing;

        var trimmed = lower.TrimStart();
        if (ImperativeOpeners.Any(verb =>
                trimmed.StartsWith(verb, StringComparison.Ordinal) &&
                (trimmed.Length == verb.Length || trimmed[verb.Length] == ' ')))
            return TodoStatus.Active;

        return null;
    }

    private static TodoPriority? PriorityCue(string lower)
    {
        if (Has(lower, "urgent", "asap", "critical", "blocker", "immediately", "overdue",
                "right now", "today", "fix it now"))
            return TodoPriority.High;

        if (Has(lower, "someday", "eventually", "at some point", "nice to have", "backlog",
                "one day", "when possible", "some day"))
            return TodoPriority.Low;

        if (Has(lower, "this week", "deadline", "by monday", "by tuesday", "by wednesday",
                "by thursday", "by friday", "soon", "important"))
            return TodoPriority.Medium;

        return null;
    }

    private static List<string> LabelCues(string lower)
    {
        var labels = new List<string>();
        if (Has(lower, "urgent", "asap", "critical", "blocker", "overdue", "immediately"))
            labels.Add("Urgent");
        if (Has(lower, "client", "customer", "lead", "sales", "prospect", "vendor", "supplier", "partner"))
            labels.Add("Lead");
        if (Has(lower, "internal", "team", "standup", "retro", "onboarding", "handbook"))
            labels.Add("Internal");
        return labels;
    }

    private static readonly (string Kind, Regex Cue)[] KindCues =
    {
        ("Bug", new Regex(@"\b(bugs?|crash(es|ed|ing)?|broken|errors?|exceptions?|regressions?|fail(s|ed|ing|ure)?|leak(s|ing)?|glitch(es|y)?|issues?|wrong|incorrect|confus\w*|is missing|instead of|not working|stopped working|(does|do|did|is|are|was|can)(n'?t| not)|dont|cant|cannot)\b",
                          RegexOptions.Compiled)),
        ("Feature", new Regex(@"^(add|support|implement|introduce|allow|enable)\b|\b(new feature|ability to|option to|let users|add support)\b",
                              RegexOptions.Compiled)),
        ("Enhancement", new Regex(@"\b(improve\w*|enhance\w*|optimi[sz]\w*|refactor\w*|clean ?up|polish|speed up|faster|simplif\w*|rework|redesign|tweak\w*)\b",
                                  RegexOptions.Compiled)),
        ("Think About", new Regex(@"\b(maybe|perhaps|consider|think about|should we|what if|brainstorm|someday)\b|\?\s*$",
                                  RegexOptions.Compiled))
    };

    /// <summary>The kind a task names outright, or null when it names none or more than one.</summary>
    public static string? KindCue(string task)
    {
        var lower = task.Trim().ToLowerInvariant();
        var named = KindCues.Where(k => k.Cue.IsMatch(lower)).Select(k => k.Kind).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>The rule-only reading of a note. Also the baseline the model refines.</summary>
    public static ExtractedTask Heuristic(string note)
    {
        var lower = note.ToLowerInvariant();
        var title = FirstSentence(note).Trim();

        return new ExtractedTask
        {
            Title = Truncate(Capitalize(title), 90),
            Description = title.Length < note.Trim().Length ? Truncate(note.Trim(), 220) : string.Empty,
            Status = StatusCue(lower) ?? TodoStatus.Active,
            Priority = PriorityCue(lower) ?? TodoPriority.None,
            Labels = LabelCues(lower)
        };
    }

    private static bool Has(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    // ── Helpers ─────────────────────────────────────────────────

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    public void Dispose()
    {
        _weights?.Dispose();
        _weights = null;
        _gate.Dispose();
    }
}
