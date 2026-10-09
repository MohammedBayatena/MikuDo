namespace MikuDo.Services;

/// <summary>
/// Find in a preview. The page's text is read as it shows, block by block, and
/// every match is highlighted in place without touching the page itself, so
/// its buttons, links and diagrams go on working. A preview loads it with every
/// page, before the page's own scripts.
/// </summary>
/// <remarks>
/// The page answers three calls: <c>mikudoFind(pattern, keep, reveal)</c>
/// searches anew, <c>mikudoFindStep(step)</c> goes to the next or previous
/// match, and <c>mikudoFindClear()</c> takes the highlights off. Each returns
/// how many matches there are and which one is current. A new search starts at
/// the first match on screen; <c>keep</c> holds on to a match by its number, as
/// a page drawn again after an edit still has the one the user was at.
/// </remarks>
public static class FindScript
{
    public const string Source = """
        (function () {
          var found = [], active = -1, more = false, all = null, current = null;
          var SKIP = 'script,style,noscript,svg,button,textarea,input,select,.mermaid,.mikudo-diagram,.mikudo-audio,.mikudo-empty';
          var BLOCK = 'p,li,h1,h2,h3,h4,h5,h6,td,th,pre,blockquote,dt,dd,figcaption,summary,details,div,section,article,main,table,tr,ul,ol';
          var BETWEEN = 'ul,ol,table,thead,tbody,tfoot,tr,body,main,section,blockquote,details,dl';

          function compile(o) {
            var source = o.regex ? o.text : o.text.replace(/[.*+?^${}()|[\]\\\/]/g, '\\$&');
            var flags = 'gm' + (o.matchCase ? '' : 'i');
            var tries = [
              [o.wholeWord ? '(?<![\\p{L}\\p{M}\\p{N}_])(?:' + source + ')(?![\\p{L}\\p{M}\\p{N}_])' : source, flags + 'u'],
              [o.wholeWord ? '(?<![A-Za-z0-9_])(?:' + source + ')(?![A-Za-z0-9_])' : source, flags]
            ];
            for (var i = 0; i < tries.length; i++) {
              try { return new RegExp(tries[i][0], tries[i][1]); } catch (e) { }
            }
            return null;
          }

          // The text on show, with a line break between blocks, and where each piece of it lives.
          function texts() {
            var parts = [], text = '', last = null;
            var walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            for (var n = walk.nextNode(); n; n = walk.nextNode()) {
              var el = n.parentElement;
              if (!el || !n.data || el.closest(SKIP)) continue;
              if (!/\S/.test(n.data) && el.matches(BETWEEN)) continue;
              if (!el.getClientRects().length) continue;
              var block = el.closest(BLOCK);
              if (parts.length && block !== last) text += '\n';
              last = block;
              parts.push({ node: n, start: text.length });
              text += n.data;
            }
            return { parts: parts, text: text };
          }

          function piece(parts, i) {
            var lo = 0, hi = parts.length - 1;
            while (lo < hi) {
              var mid = (lo + hi + 1) >> 1;
              if (parts[mid].start <= i) lo = mid; else hi = mid - 1;
            }
            return lo;
          }

          function range(parts, s, e) {
            var a = piece(parts, s), pa = parts[a];
            if (s >= pa.start + pa.node.data.length) {
              if (a + 1 >= parts.length) return null;
              pa = parts[++a];
              s = pa.start;
            }
            var b = piece(parts, e - 1), pb = parts[b];
            if (b < a) return null;
            var r = document.createRange();
            r.setStart(pa.node, s - pa.start);
            r.setEnd(pb.node, Math.min(e - pb.start, pb.node.data.length));
            return r.collapsed ? null : r;
          }

          function clear() {
            found = []; active = -1; more = false; all = current = null;
            if (window.CSS && CSS.highlights) {
              CSS.highlights.delete('mikudo-find');
              CSS.highlights.delete('mikudo-find-active');
            }
          }

          function paint() {
            if (!window.CSS || !CSS.highlights || typeof Highlight === 'undefined') return;
            all = new Highlight();
            current = new Highlight();
            current.priority = 1;
            for (var i = 0; i < found.length; i++) if (i !== active) all.add(found[i]);
            if (active >= 0) current.add(found[active]);
            CSS.highlights.set('mikudo-find', all);
            CSS.highlights.set('mikudo-find-active', current);
          }

          function firstInView() {
            for (var i = 0; i < found.length; i++) {
              var b = found[i].getBoundingClientRect();
              if (b.bottom > 0) return i;
            }
            return 0;
          }

          // Scrolls the match into view: across a wide code block or table first, then down the page.
          function show(r) {
            for (var e = r.startContainer.parentElement; e && e !== document.body && e !== document.documentElement; e = e.parentElement) {
              if (e.scrollWidth <= e.clientWidth + 1) continue;
              var rr = r.getBoundingClientRect(), er = e.getBoundingClientRect();
              if (rr.left < er.left + 16 || rr.right > er.right - 16) e.scrollLeft += rr.left - er.left - er.width / 3;
            }
            var b = r.getBoundingClientRect(), h = window.innerHeight;
            if (b.top < 24 || b.bottom > h - 24) window.scrollBy(0, b.top - h / 3);
          }

          function state() { return { count: found.length, index: active, more: more }; }

          window.mikudoFind = function (o, keep, reveal) {
            clear();
            if (!o || !o.text) return state();
            var re = compile(o);
            if (!re) return { count: 0, index: -1, more: false, error: 'Not a valid regex' };
            var t = texts(), m;
            while ((m = re.exec(t.text))) {
              if (m[0].length === 0) { re.lastIndex++; continue; }
              if (found.length >= o.limit) { more = true; break; }
              var r = range(t.parts, m.index, m.index + m[0].length);
              if (r) found.push(r);
            }
            if (!found.length) return state();
            active = keep >= 0 ? Math.min(keep, found.length - 1) : firstInView();
            paint();
            if (reveal) show(found[active]);
            return state();
          };

          window.mikudoFindStep = function (step) {
            if (!found.length) return state();
            var next = ((active + step) % found.length + found.length) % found.length;
            if (all && current) {
              all.add(found[active]);
              all.delete(found[next]);
              current.clear();
              current.add(found[next]);
            }
            active = next;
            show(found[active]);
            return state();
          };

          window.mikudoFindClear = function () { clear(); return state(); };
        })();
        """;
}
