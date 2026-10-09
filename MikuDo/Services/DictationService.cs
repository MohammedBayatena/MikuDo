using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace MikuDo.Services;

/// <summary>
/// Speech to text on the CPU with a Whisper model the user downloads in
/// Settings. Nothing is sent anywhere: the model runs in this process.
/// </summary>
public class DictationService : IModelStore, IDisposable
{
    public string Purpose => "Dictation model";

    /// <summary>Whisper listens at 16 kHz, mono.</summary>
    private const int SampleRate = 16_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelDirectory;

    private WhisperFactory? _factory;
    private string? _loadedModelId;

    public DictationService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mikudo", "models"))
    {
    }

    /// <summary>Uses the models in <paramref name="modelDirectory"/>, such as a folder of test models.</summary>
    public DictationService(string modelDirectory)
    {
        _modelDirectory = modelDirectory;
        Directory.CreateDirectory(_modelDirectory);
    }

    public AiModelOption Model => SpeechModelCatalog.Resolve(App.Database?.GetSetting("DictationModel"));
    public bool IsModelDownloaded => Exists(Model);

    public string PathFor(AiModelOption option) => Path.Combine(_modelDirectory, option.FileName);

    public bool Exists(AiModelOption option)
    {
        var path = PathFor(option);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
    }

    public void SelectModel(AiModelOption option)
    {
        if (option.Id == Model.Id) return;
        App.Database.SaveSetting("DictationModel", option.Id);
        Unload();
    }

    /// <summary>Releases the model so its file can be replaced or deleted.</summary>
    public void Unload()
    {
        _gate.Wait();
        try
        {
            _factory?.Dispose();
            _factory = null;
            _loadedModelId = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DownloadModelAsync(AiModelOption option, IProgress<double> progress, CancellationToken ct)
        => ModelDownload.ToFileAsync(option, PathFor(option), progress, ct);

    /// <summary>
    /// Loads the model ahead of time, so transcribing can start the moment
    /// the user stops talking. Called while they are still speaking.
    /// </summary>
    public async Task PrepareAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(EnsureLoaded, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The words in a WAV clip, tidied, or "" when nothing was said.</summary>
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        var samples = await Task.Run(() => ToWhisperSamples(wav), ct);
        if (samples.Length < SampleRate / 2) return string.Empty;

        await _gate.WaitAsync(ct);
        try
        {
            var factory = await Task.Run(EnsureLoaded, ct);
            var option = Model;

            var builder = factory.CreateBuilder()
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                .WithLanguage(SpeechModelCatalog.IsEnglishOnly(option) ? "en" : "auto");

            // Disposed with await: a processor stopped by cancellation refuses a plain Dispose.
            await using var processor = builder.Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                text.Append(segment.Text);

            return Tidy(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    private WhisperFactory EnsureLoaded()
    {
        var option = Model;
        if (_factory != null && _loadedModelId == option.Id) return _factory;

        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(PathFor(option));
        _loadedModelId = option.Id;
        return _factory;
    }

    /// <summary>Any WAV the recorder makes, as 16 kHz mono samples.</summary>
    private static float[] ToWhisperSamples(byte[] wav)
    {
        if (wav.Length == 0) return Array.Empty<float>();

        using var reader = new WaveFileReader(new MemoryStream(wav));
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels > 1) source = source.ToMono();
        if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);

        var samples = new List<float>((int)(reader.TotalTime.TotalSeconds * SampleRate) + SampleRate);
        var buffer = new float[SampleRate];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        return samples.ToArray();
    }

    /// <summary>Whisper marks silence and noise in brackets.</summary>
    private static readonly Regex NoiseRe = new(@"\[[^\]]*\]|\((?:music|silence|noise|inaudible|blank_audio)[^)]*\)",
                                                RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpacesRe = new(@"\s+", RegexOptions.Compiled);

    private static string Tidy(string raw)
    {
        var text = SpacesRe.Replace(NoiseRe.Replace(raw, " "), " ").Trim();

        // base.en sometimes wraps the first sentence in quote marks nobody said.
        if (text.StartsWith('"') && text.Count(c => c == '"') == 2)
            text = text.Remove(text.IndexOf('"', 1), 1).Remove(0, 1).Trim();
        return text;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
