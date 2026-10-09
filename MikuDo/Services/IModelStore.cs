namespace MikuDo.Services;

/// <summary>
/// A set of downloadable models of one kind, one of them chosen: the AI
/// Import models and the dictation models both work this way in Settings.
/// </summary>
public interface IModelStore
{
    /// <summary>What the models are for, as Settings and its messages name them.</summary>
    string Purpose { get; }

    AiModelOption Model { get; }
    string PathFor(AiModelOption option);
    bool Exists(AiModelOption option);
    void SelectModel(AiModelOption option);

    /// <summary>Releases the chosen model so its file can be replaced or deleted.</summary>
    void Unload();

    Task DownloadModelAsync(AiModelOption option, IProgress<double> progress, CancellationToken ct);
}
