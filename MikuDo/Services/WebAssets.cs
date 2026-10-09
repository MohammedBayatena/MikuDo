using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using MikuDo.Models;

namespace MikuDo.Services;

/// <summary>
/// Scripts the previews load from disk, through a host name the browser maps
/// to a folder, rather than inline in each page.
/// </summary>
/// <remarks>
/// Mermaid is 2.5 MB. Inline, every preview would carry and parse it on every
/// render, diagram or not; served from a mapped folder it is fetched only by
/// a page that has a diagram.
/// </remarks>
public static class WebAssets
{
    /// <summary>The host the assets are served from. ".example" is reserved, so it cannot shadow a real site.</summary>
    public const string Host = "mikudo-assets.example";

    private static readonly object Gate = new();
    private static string? _folder;

    private static readonly string[] Files = { "mermaid.min.js", "mermaid.LICENSE.txt" };

    /// <summary>Where the assets are unpacked; each is rewritten when the app carries a different one.</summary>
    public static string Folder
    {
        get
        {
            lock (Gate) return _folder ??= Unpack();
        }
    }

    private static string Unpack()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mikudo", "web");
        Directory.CreateDirectory(folder);

        foreach (var name in Files)
        {
            try
            {
                using var source = typeof(WebAssets).Assembly.GetManifestResourceStream("MikuDo.Web." + name);
                if (source == null) continue;

                var target = Path.Combine(folder, name);
                if (File.Exists(target) && new FileInfo(target).Length == source.Length) continue;

                using var file = File.Create(target);
                source.CopyTo(file);
            }
            catch (IOException ex)
            {
                // Another window's browser may be reading the old copy; it is kept.
                LogService.Error($"Could not unpack {name}", ex);
            }
        }

        var fonts = Path.Combine(folder, "fonts");
        Directory.CreateDirectory(fonts);
        foreach (var weight in new[] { "Regular", "Medium", "SemiBold", "Bold", "ExtraBold" })
        {
            var name = $"PlusJakartaSans-{weight}.ttf";
            try
            {
                var info = System.Windows.Application.GetResourceStream(
                    new Uri($"pack://application:,,,/MikuDo;component/Assets/Fonts/{name}"));
                if (info == null) continue;
                using var source = info.Stream;
                var target = Path.Combine(fonts, name);
                if (File.Exists(target) && new FileInfo(target).Length == source.Length) continue;
                using var file = File.Create(target);
                source.CopyTo(file);
            }
            catch (IOException ex)
            {
                LogService.Error($"Could not unpack {name}", ex);
            }
        }
        return folder;
    }

    /// <summary>Lets a preview load the assets from https://<see cref="Host"/>/.</summary>
    public static void Map(CoreWebView2 core)
        => core.SetVirtualHostNameToFolderMapping(Host, Folder, CoreWebView2HostResourceAccessKind.Allow);

    /// <summary>
    /// Draws the page's ```mermaid blocks in the theme's colours. A block that
    /// does not parse shows why, with its source, and the rest of the page is
    /// unaffected. A page with no diagram never loads Mermaid.
    /// </summary>
    public static string MermaidScript()
    {
        var config = new Dictionary<string, object>
        {
            ["startOnLoad"] = false,
            ["securityLevel"] = "strict",
            ["theme"] = "base",
            ["fontFamily"] = "'Plus Jakarta Sans','Segoe UI',sans-serif",
            ["sequence"] = new Dictionary<string, object>
            {
                ["actorFontSize"] = 13,
                ["messageFontSize"] = 12.5,
                ["noteFontSize"] = 12,
                ["actorFontWeight"] = 600
            },
            ["themeVariables"] = new Dictionary<string, object>
            {
                ["darkMode"] = App.IsDark,
                ["fontFamily"] = "'Plus Jakarta Sans','Segoe UI',sans-serif",
                ["fontSize"] = "13px",
                ["background"] = Palette.Css("InlineAddBgBrush"),
                ["primaryColor"] = Palette.Css("AccentSoftBrush"),
                ["primaryBorderColor"] = Palette.Css("AccentBorderStrongBrush"),
                ["primaryTextColor"] = Palette.Css("TextPrimaryBrush"),
                ["secondaryColor"] = Palette.Css("SurfaceSubtleBrush"),
                ["secondaryBorderColor"] = Palette.Css("BorderBrush"),
                ["secondaryTextColor"] = Palette.Css("TextPrimaryBrush"),
                ["tertiaryColor"] = Palette.Css("SurfaceMutedBrush"),
                ["tertiaryBorderColor"] = Palette.Css("BorderBrush"),
                ["tertiaryTextColor"] = Palette.Css("TextPrimaryBrush"),
                ["lineColor"] = Palette.Css("TextMutedBrush"),
                ["textColor"] = Palette.Css("TextSecondaryBrush"),
                ["mainBkg"] = Palette.Css("AccentSoftBrush"),
                ["nodeBorder"] = Palette.Css("AccentBorderStrongBrush"),
                ["clusterBkg"] = Palette.Css("SurfaceSubtleBrush"),
                ["clusterBorder"] = Palette.Css("BorderBrush"),
                ["edgeLabelBackground"] = Palette.Css("InlineAddBgBrush"),
                ["actorBkg"] = Palette.Css("AccentSoftBrush"),
                ["actorBorder"] = Palette.Css("AccentBorderStrongBrush"),
                ["actorTextColor"] = Palette.Css("TextPrimaryBrush"),
                ["actorLineColor"] = Palette.Css("BorderBrush"),
                ["signalColor"] = Palette.Css("TextMutedBrush"),
                ["signalTextColor"] = Palette.Css("TextSecondaryBrush"),
                ["noteBkgColor"] = Palette.Css("SurfaceSubtleBrush"),
                ["noteBorderColor"] = Palette.Css("BorderBrush"),
                ["noteTextColor"] = Palette.Css("TextSecondaryBrush")
            }
        };

        return """
            (function () {
              var blocks = Array.prototype.slice.call(document.querySelectorAll('pre.mermaid, div.mermaid, pre > code.language-mermaid'));
              if (!blocks.length) return;
              var config =
            """ + JsonSerializer.Serialize(config) + """
            ;
              function place(block) {
                var host = block.tagName === 'CODE' ? block.parentNode : block;
                var figure = document.createElement('div');
                figure.className = 'mikudo-diagram';
                host.parentNode.replaceChild(figure, host);
                return figure;
              }
              function fail(figure, title, detail, source) {
                figure.className = 'mikudo-diagram-error';
                figure.textContent = '';
                var head = document.createElement('div'); head.className = 'mikudo-diagram-error-title'; head.textContent = title;
                figure.appendChild(head);
                if (detail) { var why = document.createElement('div'); why.className = 'mikudo-diagram-error-detail'; why.textContent = detail; figure.appendChild(why); }
                var code = document.createElement('pre'); code.textContent = source; figure.appendChild(code);
              }
              function firstLines(text) { return String(text || '').split('\n').slice(0, 4).join('\n'); }
              function draw() {
                mermaid.initialize(config);
                blocks.forEach(function (block, i) {
                  var source = block.textContent;
                  var figure = place(block);
                  var id = 'mikudo-mermaid-' + i;
                  mermaid.render(id, source).then(function (result) {
                    figure.innerHTML = result.svg;
                  }).catch(function (error) {
                    var stray = document.getElementById('d' + id);
                    if (stray) stray.remove();
                    fail(figure, 'This diagram could not be drawn', firstLines(error && error.message ? error.message : error), source);
                  });
                });
              }
              if (window.mermaid) { draw(); return; }
              var script = document.createElement('script');
              script.src = 'https://
            """ + Host + """
            /mermaid.min.js';
              script.onload = draw;
              script.onerror = function () {
                blocks.forEach(function (block) {
                  var source = block.textContent;
                  fail(place(block), 'Diagrams could not be loaded', '', source);
                });
              };
              document.head.appendChild(script);
            })();
            """;
    }
}
