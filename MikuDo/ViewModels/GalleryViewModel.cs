using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>The full-window image viewer opened from an attachment thumbnail or the preview.</summary>
public partial class GalleryViewModel : ObservableObject
{
    [ObservableProperty] private int _index;

    private readonly MainViewModel _main;
    public IReadOnlyList<GalleryImage> Images { get; }

    private GalleryImage? CurrentImage => Index >= 0 && Index < Images.Count ? Images[Index] : null;

    public ImageSource? Current => CurrentImage?.Source;
    public bool HasImage => Current != null;
    public bool HasSeveral => Images.Count > 1;
    public string CountLabel => $"Image {Index + 1} of {Images.Count}";
    public string Name => CurrentImage is { } image ? ImageFiles.NameOf(image.StorageId) : string.Empty;

    public GalleryViewModel(MainViewModel main, IReadOnlyList<GalleryImage> images, int index)
    {
        _main = main;
        Images = images;
        _index = Math.Clamp(index, 0, Math.Max(0, images.Count - 1));
    }

    partial void OnIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(Name));
    }

    [RelayCommand] private void Next() => Step(1);
    [RelayCommand] private void Previous() => Step(-1);

    private void Step(int delta)
    {
        if (Images.Count == 0) return;
        Index = ((Index + delta) % Images.Count + Images.Count) % Images.Count;
    }

    [RelayCommand]
    private void Save()
    {
        if (CurrentImage is { } image) ImageFiles.SaveAs(image.StorageId);
    }

    [RelayCommand] private void Close() => _main.CloseGalleryCommand.Execute(null);
}
