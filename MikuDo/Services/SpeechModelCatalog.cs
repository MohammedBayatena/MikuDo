namespace MikuDo.Services;

/// <summary>
/// The Whisper models offered for dictation, all quantized to Q5_1 so they
/// stay small and run on the CPU. Timings are for 18 seconds of speech on a
/// typical 8-thread desktop: base.en takes about 4 s, small.en about 17 s.
/// </summary>
public static class SpeechModelCatalog
{
    public static readonly AiModelOption BaseEnglish = new(
        "whisper-base.en",
        "Whisper base.en (Q5_1)",
        "ggml-base.en-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en-q5_1.bin?download=true",
        59_721_011,
        "Recommended. English, with punctuation, about 4 seconds for 20 seconds of speech.");

    public static readonly AiModelOption SmallEnglish = new(
        "whisper-small.en",
        "Whisper small.en (Q5_1)",
        "ggml-small.en-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en-q5_1.bin?download=true",
        190_098_681,
        "The most accurate English, but on a CPU it takes about as long as the speech itself.");

    public static readonly AiModelOption BaseMultilingual = new(
        "whisper-base",
        "Whisper base, multilingual (Q5_1)",
        "ggml-base-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin?download=true",
        59_700_000,
        "English and around 90 other languages; it works out which one is spoken.");

    public static readonly AiModelOption[] All = { BaseEnglish, SmallEnglish, BaseMultilingual };

    public static AiModelOption Default => BaseEnglish;

    public static AiModelOption Resolve(string? id)
        => All.FirstOrDefault(m => m.Id == id) ?? Default;

    /// <summary>The ".en" models know only English and are told so; the others detect the language.</summary>
    public static bool IsEnglishOnly(AiModelOption option) => option.Id.EndsWith(".en", StringComparison.Ordinal);
}
