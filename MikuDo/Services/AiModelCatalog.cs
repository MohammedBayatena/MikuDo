namespace MikuDo.Services;

/// <summary>A GGUF model the AI importer can run on the CPU.</summary>
public record AiModelOption(
    string Id,
    string DisplayName,
    string FileName,
    string Url,
    long SizeBytes,
    string Note)
{
    public string SizeLabel => $"{SizeBytes / 1_000_000} MB";
}

/// <summary>
/// The models offered for AI Import, measured on a set of hand-labelled notes.
/// Qwen2.5-0.5B is the smallest that classifies status and labels usefully;
/// below it the answers collapse onto whichever example came last in the prompt,
/// which is why the compact option is described the way it is.
/// </summary>
public static class AiModelCatalog
{
    public static readonly AiModelOption Qwen05B = new(
        "qwen2.5-0.5b",
        "Qwen2.5 0.5B Instruct (Q4_K_M)",
        "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf",
        "https://huggingface.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf?download=true",
        379_400_000,
        "Recommended. Reads status and labels from the wording of a note.");

    public static readonly AiModelOption SmolLm135M = new(
        "smollm2-135m",
        "SmolLM2 135M Instruct (Q8_0)",
        "SmolLM2-135M-Instruct-Q8_0.gguf",
        "https://huggingface.co/unsloth/SmolLM2-135M-Instruct-GGUF/resolve/main/SmolLM2-135M-Instruct-Q8_0.gguf?download=true",
        138_100_000,
        "Under 200 MB and fast, but it adds little over the built-in keyword rules.");

    public static readonly AiModelOption[] All = { Qwen05B, SmolLm135M };

    public static AiModelOption Default => Qwen05B;

    public static AiModelOption Resolve(string? id)
        => All.FirstOrDefault(m => m.Id == id) ?? Default;
}
