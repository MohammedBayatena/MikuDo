using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MikuDo.Services;

/// <summary>Saving a task's attached images back out to disk.</summary>
public static class ImageFiles
{
    /// <summary>The name an image was attached under: the last part of its storage id.</summary>
    public static string NameOf(string storageId)
    {
        var slash = storageId.LastIndexOf('/');
        return slash >= 0 ? storageId[(slash + 1)..] : storageId;
    }

    /// <summary>
    /// Asks where to save an attached image and writes it there byte for byte,
    /// as it was attached, under the name it was attached with.
    /// </summary>
    public static void SaveAs(string storageId)
    {
        var name = NameOf(storageId);
        var extension = Path.GetExtension(name);
        var kind = extension.Length > 1 ? extension[1..].ToUpperInvariant() : "PNG";

        var dialog = new SaveFileDialog
        {
            Title = "Save image",
            FileName = name,
            Filter = $"{kind} image|*{(extension.Length > 1 ? extension : ".png")}|All files|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };

        // The window in front owns the dialog, which is the lightbox when saving from there.
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (dialog.ShowDialog(owner) != true) return;

        try
        {
            using var source = App.Database.GetImage(storageId);
            if (source == null)
            {
                DialogService.Notify("This image could not be found.", "Save image");
                return;
            }

            using var target = File.Create(dialog.FileName);
            source.CopyTo(target);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not save {name}:\n{ex.Message}", "Save image");
        }
    }
}
