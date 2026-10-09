using Markdig;
using MikuDo.Models;

namespace MikuDo.Services;

public class MarkdownService
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();
    }

    /// <summary>
    /// The note as a page in the active theme's colours, read from its semantic
    /// tokens. <paramref name="reading"/> lays it out for reading a whole
    /// document, as Notes does: larger type, across the whole width the page
    /// is given. Relative links and images resolve against
    /// <paramref name="baseHref"/>. A <paramref name="kicker"/> gives the page
    /// a reader window's roomier margins, with the kicker's line above it.
    /// </summary>
    public string ToHtml(string markdown, bool reading = false, string? baseHref = null, string? kicker = null)
    {
        var surface = Palette.Css("SurfaceBrush");
        var heading = Palette.Css("TextPrimaryBrush");
        var body = Palette.Css("TextSecondaryBrush");
        var faint = Palette.Css("TextFaintBrush");
        var accent = Palette.Css("AccentDarkBrush");
        var bullet = Palette.Css("AccentBulletBrush");
        var codeBg = Palette.Css("CodeBgBrush");
        var quoteBg = Palette.Css("QuoteBgBrush");
        var border = Palette.Css("BorderFaintBrush");
        var tableHead = Palette.Css("SurfaceSubtleBrush");
        var mediaBg = Palette.Css("InlineAddBgBrush");
        var danger = Palette.Css("DangerBrush");
        var dangerSoft = Palette.Css("DangerSoftBrush");
        var dangerBorder = Palette.Css("DangerBorderBrush");
        var muted = Palette.Css("TextMutedBrush");
        var accentSoft = Palette.Css("AccentSoftBrush");
        var accentFill = Palette.Css("AccentFillBrush");
        var accentBorder = Palette.Css("AccentBorderStrongBrush");
        var onAccent = Palette.Css("OnAccentBrush");
        var boxBorder = Palette.Css("TextGhostBrush");
        var surfaceMuted = Palette.Css("SurfaceMutedBrush");
        var green = Palette.Css("LabelGreenBrush");
        var greenSoft = Palette.Css("LabelGreenSoftBrush");
        var findMatch = Palette.Css("FindMatchBrush");
        var findActive = Palette.Css("FindActiveBrush");
        var fonts = Fonts();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return $$"""
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>
                body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;
                background:{{surface}};color:{{faint}};
                font:400 12.5px 'Plus Jakarta Sans',Segoe UI,sans-serif}
                </style></head><body class="mikudo-empty">Preview will appear here…</body></html>
                """;
        }

        var content = Markdown.ToHtml(markdown, _pipeline);
        var head = baseHref == null ? "" : $"<base href=\"{baseHref}\">";
        var reader = reading ? ReadingCss() : "";
        if (reading)
            content = kicker == null
                ? $"<main class=\"note\">{content}</main>"
                : $"<main class=\"note column\"><div class=\"mikudo-kicker\">{System.Net.WebUtility.HtmlEncode(kicker)}</div>{content}</main>";

        string ReadingCss() => $$"""
            body{font-size:14.5px;line-height:1.65;padding:26px 36px 48px 24px}
            body:has(main.column){padding:36px 44px 48px}
            main.column h1{font-size:30px}
            main.column>p{font-size:15px;line-height:1.7}
            .mikudo-kicker{font-size:11.5px;font-weight:600;color:{{faint}};margin:0 0 14px;overflow-wrap:anywhere}
            h1{font-size:28px;font-weight:800;letter-spacing:-.01em;margin:0 0 8px;line-height:1.25}
            h2{font-size:18px;font-weight:800;margin:26px 0 12px}
            h3{font-size:15px;font-weight:700;margin:20px 0 8px}
            p{margin:0 0 14px}
            li{margin:0 0 4px}
            pre{margin:12px 0 18px}
            table{font-size:13px;border-radius:10px;overflow:hidden}
            .mikudo-diagram{background:{{surfaceMuted}};border-color:{{border}};padding:18px 18px 14px;margin:0 0 26px}
            ul.contains-task-list{padding-left:0;margin:0 0 22px}
            ul.contains-task-list ul.contains-task-list{margin:2px 0 0;padding-left:26px}
            li.task-list-item{list-style:none;position:relative;padding:6px 92px 6px 8px;margin:0 0 2px;
                              border-radius:8px;color:{{heading}};font-size:14px}
            li.task-list-item:hover:not(:has(li:hover)){background:{{accentSoft}}}
            li.task-list-item>input[type=checkbox]{-webkit-appearance:none;appearance:none;width:15px;height:15px;
                              margin:0 10px 0 0;vertical-align:-2px;border:1.5px solid {{boxBorder}};border-radius:5px;
                              background:transparent}
            li.task-list-item>input[type=checkbox]:checked{border-color:{{accentFill}};background:{{accentFill}} url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='white' stroke-width='3.6' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M5 13l4 4L19 7'/%3E%3C/svg%3E") center/10px no-repeat}
            li.task-list-item.done{color:{{muted}}}
            li.task-list-item.done>.mikudo-own{text-decoration:line-through}
            .mikudo-task,.mikudo-made{position:absolute;right:8px;top:4px;display:none;align-items:center;gap:5px;
                              font-size:11.5px;font-weight:700;border-radius:99px;padding:3px 10px;cursor:pointer;
                              font-family:inherit;line-height:1.4}
            .mikudo-task{color:{{accent}};background:{{surface}};border:1px solid {{accentBorder}}}
            .mikudo-task:hover{background:{{accentFill}};color:{{onAccent}};border-color:{{accentFill}}}
            li.task-list-item:hover:not(:has(li:hover))>.mikudo-task{display:inline-flex}
            .mikudo-made{display:inline-flex;cursor:default;color:{{green}};background:{{greenSoft}};border:0}
            """;

        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8">{{head}}<style>
            {{fonts}}
            *{margin:0;padding:0;box-sizing:border-box}
            body{font-family:'Plus Jakarta Sans','Segoe UI',-apple-system,sans-serif;
                 font-size:13px;line-height:1.7;padding:16px 18px;color:{{body}};background:{{surface}};
                 -webkit-font-smoothing:antialiased}
            ::-webkit-scrollbar{width:4px;height:4px}
            ::-webkit-scrollbar-thumb{background:{{border}};border-radius:99px}
            ::-webkit-scrollbar-track{background:transparent}
            ::highlight(mikudo-find){background-color:{{findMatch}};color:{{heading}}}
            ::highlight(mikudo-find-active){background-color:{{findActive}};color:{{heading}}}
            h1{font-size:19px;font-weight:700;color:{{heading}};margin:0 0 10px}
            h2{font-size:14px;font-weight:700;color:{{heading}};margin:16px 0 8px}
            h3,h4,h5,h6{font-size:13px;font-weight:700;color:{{heading}};margin:14px 0 6px}
            p{margin:0 0 9px}
            strong{font-weight:700;color:{{heading}}}
            a{color:{{accent}};text-decoration:none}
            a:hover{text-decoration:underline}
            ul,ol{margin:0 0 9px;padding-left:20px}
            li{margin:0 0 6px}
            ul li::marker{color:{{bullet}}}
            ol li::marker{color:{{bullet}};font-weight:600}
            code{background:{{codeBg}};color:{{accent}};font-weight:600;padding:1px 6px;border-radius:5px;
                 font-family:'Cascadia Mono',Consolas,ui-monospace,monospace;font-size:.9em}
            pre{background:{{codeBg}};border-radius:8px;margin:10px 0;overflow-x:auto}
            pre code{display:block;padding:12px 14px;font-weight:400;background:transparent;border-radius:0}
            blockquote{margin:10px 0;padding:10px 14px;background:{{quoteBg}};border-radius:8px;
                       font-size:12.5px;color:{{accent}}}
            blockquote p{margin:0}
            img{max-width:100%;border-radius:10px;margin:10px 0;display:block}
            hr{border:none;border-top:1px solid {{border}};margin:14px 0}
            table{border-collapse:collapse;width:100%;margin:10px 0;font-size:12px}
            th,td{border:1px solid {{border}};padding:7px 10px;text-align:left}
            th{background:{{tableHead}};font-weight:700;color:{{heading}}}
            input[type=checkbox]{margin-right:8px;accent-color:{{accent}}}
            .mikudo-audio{margin:10px 0;display:flex;align-items:center;gap:11px;padding:9px 12px;
                          border:1px solid {{border}};border-radius:10px;background:{{mediaBg}}}
            .mermaid{white-space:pre-wrap;font-family:'Cascadia Mono',Consolas,monospace;font-size:11.5px;
                        color:{{faint}};margin:10px 0}
            .mikudo-diagram{margin:12px 0;padding:16px;border:1px solid {{border}};border-radius:12px;
                            background:{{mediaBg}};overflow-x:auto;text-align:center}
            .mikudo-diagram svg{max-width:100%;height:auto}
            .mikudo-diagram .node rect,.mikudo-diagram rect.actor{rx:8px;ry:8px}
            .mikudo-diagram-error{margin:12px 0;padding:12px 14px;border:1px solid {{dangerBorder}};border-radius:10px;
                                  background:{{dangerSoft}}}
            .mikudo-diagram-error-title{font-weight:700;color:{{danger}};margin-bottom:4px}
            .mikudo-diagram-error-detail{font-size:12px;white-space:pre-wrap;margin-bottom:8px;
                                         font-family:'Cascadia Mono',Consolas,monospace}
            .mikudo-diagram-error pre{margin:0;padding:10px 12px;background:{{surface}};border:1px solid {{dangerBorder}};
                                      font-family:'Cascadia Mono',Consolas,monospace;font-size:11.5px;
                                      white-space:pre-wrap;color:{{body}}}
            {{reader}}
            </style></head><body>{{content}}</body></html>
            """;
    }

    /// <summary>The app's own typeface for the page, served beside the other preview assets.</summary>
    private static string Fonts()
    {
        var faces = new[] { ("Regular", 400), ("Medium", 500), ("SemiBold", 600), ("Bold", 700), ("ExtraBold", 800) };
        return string.Concat(faces.Select(f =>
            $"@font-face{{font-family:'Plus Jakarta Sans';font-weight:{f.Item2};" +
            $"src:url(https://{WebAssets.Host}/fonts/PlusJakartaSans-{f.Item1}.ttf)}}"));
    }
}
