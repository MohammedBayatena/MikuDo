using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace MikuDo.Services;

/// <summary>
/// Tooltips in a Markdown preview, drawn as the app's own are: where a link
/// goes, by the pointer, with an icon for its kind, and the title the page
/// gives any element. The browser's status bubble in the corner and its own
/// title tooltips never show. The page's stylesheet colours the tip.
/// </summary>
public static class PreviewTips
{
    /// <summary>
    /// Gives <paramref name="core"/> the tooltips, from its next page on. A link
    /// to <paramref name="noteHost"/> is a link to a file beside the note.
    /// </summary>
    public static Task Attach(CoreWebView2 core, string? noteHost = null)
    {
        core.Settings.IsStatusBarEnabled = false;
        return core.AddScriptToExecuteOnDocumentCreatedAsync(Source(noteHost ?? string.Empty));
    }

    private static string Source(string noteHost) => """
        (function () {
          var NOTE_HOST =
        """ + JsonSerializer.Serialize(noteHost) + """
        ;
          var ICONS = {
            web: 'M21,13 v6 a2,2 0 0 1 -2,2 H5 a2,2 0 0 1 -2,-2 V5 a2,2 0 0 1 2,-2 h6 M15,3 h6 v6 M10,14 L21,3',
            note: 'M14,3 H7 a2,2 0 0 0 -2,2 v14 a2,2 0 0 0 2,2 h10 a2,2 0 0 0 2,-2 V8 z M14,3 v5 h5 M9,13 h6 M9,17 h4',
            file: 'M6,3 h8 l5,5 v13 a1,1 0 0 1 -1,1 H6 a1,1 0 0 1 -1,-1 V4 a1,1 0 0 1 1,-1 z M14,3 v5 h5',
            mail: 'M4,6 h16 v12 H4 z M4,7 l8,6 l8,-6',
            section: 'M5,9 h14 M4,15 h14 M10,3 L8,21 M16,3 l-2,18',
            link: 'M10,14 a3.5,3.5 0 0 0 5,0 l3,-3 a3.5,3.5 0 0 0 -5,-5 l-1,1 M14,10 a3.5,3.5 0 0 0 -5,0 l-3,3 a3.5,3.5 0 0 0 5,5 l1,-1'
          };
          var WAIT = 450, WARM = 60, LONGEST = 90;
          var tip = null, target = null, timer = 0, x = 0, y = 0, hiddenAt = 0;

          // Long addresses keep their start and their end, the parts that tell one from another.
          function shorten(text) {
            if (text.length <= LONGEST) return text;
            var head = Math.ceil((LONGEST - 1) * 0.6);
            return text.slice(0, head) + '…' + text.slice(text.length - (LONGEST - 1 - head));
          }

          function decoded(text) {
            try { return decodeURIComponent(text); } catch (e) { return text; }
          }

          function fileKind(path) { return /\.(md|markdown)$/i.test(path) ? 'note' : 'file'; }

          function describe(el) {
            var title = el.getAttribute('data-mikudo-title') || '';
            if (el.tagName !== 'A' || !el.hasAttribute('href')) return title ? { kind: '', title: '', text: title } : null;
            var raw = el.getAttribute('href') || '';
            if (raw.charAt(0) === '#') {
              var heading = document.getElementById(decoded(raw.slice(1)));
              return { kind: 'section', title: title, text: heading ? heading.textContent.trim() : decoded(raw.slice(1)) };
            }
            var url;
            try { url = new URL(el.href); } catch (e) { return { kind: 'link', title: title, text: raw }; }
            if (url.protocol === 'mailto:') return { kind: 'mail', title: title, text: decoded(url.pathname) };
            if (NOTE_HOST && url.hostname === NOTE_HOST) {
              var near = decoded(url.pathname.replace(/^\//, ''));
              return { kind: fileKind(near), title: title, text: near };
            }
            if (url.protocol === 'file:') {
              var path = decoded(url.pathname.replace(/^\/(?=[A-Za-z]:)/, '')).replace(/\//g, '\\');
              return { kind: fileKind(path), title: title, text: path };
            }
            if (url.protocol === 'http:' || url.protocol === 'https:') {
              var rest = url.pathname === '/' ? '' : url.pathname;
              var where = url.host.replace(/^www\./, '') + decoded(rest + url.search + url.hash);
              return { kind: 'web', title: title, text: (url.protocol === 'http:' ? 'http://' : '') + where };
            }
            return { kind: 'link', title: title, text: el.href };
          }

          function build(d) {
            var box = document.createElement('div');
            box.className = 'mikudo-tip';
            box.setAttribute('role', 'tooltip');
            if (d.kind) {
              var ns = 'http://www.w3.org/2000/svg';
              var svg = document.createElementNS(ns, 'svg');
              svg.setAttribute('viewBox', '0 0 24 24');
              var path = document.createElementNS(ns, 'path');
              path.setAttribute('d', ICONS[d.kind]);
              svg.appendChild(path);
              box.appendChild(svg);
            }
            var words = document.createElement('span');
            words.className = 'mikudo-tip-words';
            if (d.title) {
              var name = document.createElement('span');
              name.className = 'mikudo-tip-title';
              name.textContent = d.title;
              words.appendChild(name);
            }
            var line = document.createElement('span');
            line.className = d.title ? 'mikudo-tip-where' : 'mikudo-tip-only';
            line.textContent = shorten(d.text);
            words.appendChild(line);
            box.appendChild(words);
            return box;
          }

          // Just under the pointer, as the app's tooltips sit, and kept inside the page.
          function place(box) {
            var w = box.offsetWidth, h = box.offsetHeight, edge = 8;
            var left = Math.max(edge, Math.min(x, window.innerWidth - edge - w));
            var top = y + 20;
            if (top + h > window.innerHeight - edge) top = y - h - 8;
            box.style.left = left + 'px';
            box.style.top = Math.max(edge, top) + 'px';
          }

          function show() {
            timer = 0;
            if (!target || !target.isConnected || !document.body) return;
            var d = describe(target);
            if (!d || !d.text) return;
            tip = build(d);
            document.body.appendChild(tip);
            // Placing it reads its size, so the page has drawn it unseen and the fade has somewhere to start.
            place(tip);
            tip.classList.add('shown');
          }

          function hide() {
            if (timer) { clearTimeout(timer); timer = 0; }
            if (tip) { tip.remove(); tip = null; hiddenAt = Date.now(); }
            target = null;
          }

          document.addEventListener('mouseover', function (e) {
            var el = e.target.closest ? e.target.closest('a[href],[title],[data-mikudo-title]') : null;
            if (el && el.hasAttribute('title')) {
              el.setAttribute('data-mikudo-title', el.getAttribute('title'));
              el.removeAttribute('title');
            }
            if (el === target) return;
            var warm = tip !== null || Date.now() - hiddenAt < 300;
            hide();
            if (!el) return;
            target = el;
            x = e.clientX; y = e.clientY;
            timer = setTimeout(show, warm ? WARM : WAIT);
          }, true);
          document.addEventListener('mousemove', function (e) {
            if (timer) { x = e.clientX; y = e.clientY; }
          }, true);
          document.addEventListener('mouseout', function (e) {
            if (target && !(e.relatedTarget && target.contains(e.relatedTarget))) hide();
          }, true);
          ['mousedown', 'wheel', 'keydown'].forEach(function (name) { document.addEventListener(name, hide, true); });
          window.addEventListener('scroll', hide, true);
          window.addEventListener('blur', hide);
        })();
        """;
}
