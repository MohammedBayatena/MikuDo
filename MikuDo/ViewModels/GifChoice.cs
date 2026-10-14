using CommunityToolkit.Mvvm.ComponentModel;

namespace MikuDo.ViewModels;

/// <summary>A GIF picked for the sidebar lately: the app's own copy of it, and whether it is the one shown.</summary>
public partial class GifChoice : ObservableObject
{
    [ObservableProperty] private bool _isCurrent;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);

    public GifChoice(string path) => Path = path;
}
