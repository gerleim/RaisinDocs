using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    /// <summary>
    /// Handles all rendering operations for DocsCanvas, including text drawing, styling,
    /// backgrounds, selection highlights, and visual element rendering.
    /// Encapsulates layout-independent rendering logic that depends on parsed content,
    /// visual lines, and theme palettes.
    /// </summary>
    internal class RenderingContext
    {
        private readonly IRenderingServices _rendering;
        private readonly ILayoutDataServices _layout;
        private readonly IParsedContentServices _content;
        private readonly IDocumentServices _doc;
        private readonly IScrollServices _scroll;
        private readonly ITableServices _table;
        private readonly IVisualModeServices _visual;
        private readonly IImageServices _images;
        private readonly ISearchServices _search;
        private readonly ILoggingServices _logging;
        private readonly ICanvasOperations _canvas;
        private readonly INavigationServices _navigation;
        private readonly DocsCanvas _docsCanvas;

        /// <summary>
        /// Built FormattedText per visual line, reused across frames.
        /// </summary>
        /// <remarks>
        /// A FormattedText depends on the line's text, styles, fonts and palette, never on
        /// the scroll offset - only the point it is drawn at moves. Rebuilding one per
        /// visible line per frame measured at ~66us a line, 3.4ms of a 3.5ms OnRender, and
        /// 43% of wall-clock time while coasting, which starved the message pump enough for
        /// Windows to start merging wheel notches.
        ///
        /// Keyed on DocsCanvas.RenderVersion, which InvalidateLayout bumps - covering content
        /// edits, zoom, width, edit mode and theme - and which anything else that changes how
        /// a line is drawn is expected to bump too. It is deliberately over-eager: a rebuild
        /// costs one frame on an occasional action, a missed invalidation leaves stale text on
        /// screen. Selection, search highlights, the cursor and spelling squiggles are drawn
        /// outside the FormattedText and so need no invalidation of their own.
        ///
        /// Entries outside a window around the viewport are dropped so a long document does
        /// not accumulate them.
        /// </remarks>
        /// <summary>
        /// One cached visual per visual line, rendered at its own origin and positioned by a
        /// transform. Dropped and rebuilt on the same signal as the text cache.
        /// </summary>
        /// <remarks>
        /// The text cache took prose from 66us a line to 30.5, but stopped there: a table row
        /// issues one DrawText per cell, about ten a row, and caching what feeds DrawText
        /// cannot help when DrawText is the cost. Caching the rasterised line instead makes a
        /// row a composite regardless of how many cells it holds.
        /// </remarks>
        /// <summary>How often the minimap thumbnail is refreshed while scrolling.</summary>
        private const int MinimapHz = 30;
        private long _lastMinimapTick;

        /// <summary>How far either side of the viewport lines are kept rendered ahead.</summary>
        /// <remarks>
        /// Has to exceed a screenful, or a fling empties the buffer and the next frame builds
        /// every newly visible line at once. At 40 - less than the ~50 lines on screen - that
        /// is exactly what happened: 743 of 1008 rebuilds landed nowhere near an invalidation,
        /// in bursts of about 14, which is the margin being outrun.
        /// </remarks>
        private const int PreRenderMargin = 120;

        /// <summary>
        /// Lines rendered ahead per frame, so no single frame carries the cost of the margin.
        /// </summary>
        /// <remarks>
        /// Only needs to outpace what scrolling consumes - roughly two lines a frame at speed -
        /// while leaving room to refill after a jump.
        /// </remarks>
        private const int PreRenderBudget = 6;

        /// <summary>
        /// How far outside the viewport cached visuals are kept before being dropped.
        /// </summary>
        /// <remarks>
        /// Wider than the pre-render margin, so scrolling back a little finds them still
        /// there, but bounded: each holds a rasterised bitmap the width of the canvas, which
        /// is far dearer per line than the FormattedText cache alongside it.
        /// </remarks>
        private const int LineVisualWindow = 250;

        private DrawingVisual?[]? _lineVisuals;
        private int _lineVisualsVersion = -1;
        private int _visualsLo, _visualsHi = -1;

        private void EnsureLineVisualCache(int count, int version)
        {
            if (_lineVisualsVersion == version && _lineVisuals != null && _lineVisuals.Length >= count)
                return;

            _docsCanvas.ContentLayer.Children.Clear();
            _lineVisuals = new DrawingVisual?[count];
            _lineVisualsVersion = version;
            _visualsLo = 0;
            _visualsHi = -1;
        }

        /// <summary>
        /// Renders the lines in view, keeps a margin either side rendered ahead of the
        /// viewport, and drops those that have scrolled well outside it.
        /// </summary>
        /// <remarks>
        /// A line used to be rasterised at the moment it scrolled into view, which put that
        /// work inside the frame that revealed it. It is only a mean of 0.3 lines a frame, but
        /// 100 of 911 late frames had built one, and a line that is already drawn when it
        /// arrives costs nothing.
        ///
        /// Visible lines are always built - they have to be. The margin is filled outward from
        /// the viewport a few lines a frame, which comfortably outpaces the rate scrolling
        /// consumes them without making any single frame dearer.
        /// </remarks>
        private void SyncLineVisuals(int firstVisible, int lastVisible)
        {
            if (_lineVisuals == null || firstVisible < 0) return;

            for (int i = firstVisible; i <= lastVisible; i++)
                BuildLineVisual(i);

            int budget = PreRenderBudget;
            for (int d = 1; d <= PreRenderMargin && budget > 0; d++)
            {
                if (BuildLineVisual(lastVisible + d)) budget--;
                if (budget > 0 && BuildLineVisual(firstVisible - d)) budget--;
            }

            TrimLineVisuals(firstVisible, lastVisible);
        }

        /// <summary>
        /// Builds and positions the cached line visuals for the current scroll offset.
        /// </summary>
        /// <remarks>
        /// Called from ArrangeOverride, deliberately, not from OnRender. Adding children to a
        /// visual mid-render mutates the tree during a render pass, which WPF does not allow -
        /// the same restriction that throws "Cannot call this API during the OnRender
        /// callback" for a transform. It happened to work through the live compositor, but
        /// under RenderTargetBitmap the adds simply did not take, leaving a canvas that laid
        /// out 2101 lines and drew none of them. Arrange runs before every render pass, and
        /// InvalidateVisual schedules one, so this is both legal and no less frequent.
        /// </remarks>
        /// <param name="viewportHeight">
        /// The height to treat as visible. Passed in because ArrangeOverride runs before
        /// ActualHeight is updated, so on the first arrange it still reads zero - which made
        /// every line count as out of view and built nothing at all.
        /// </param>
        internal void UpdateContentLayer(double viewportHeight)
        {
            if (_content.ParsedBlocks == null || !_docsCanvas.CachedLineVisuals) return;
            if (_layout.VisualLines.Count == 0 || viewportHeight <= 0) return;

            double effectiveScroll = Math.Round(_scroll.Scroll.EffectiveOffset);
            double viewTop = effectiveScroll;
            double viewBottom = effectiveScroll + viewportHeight;

            int firstVisible = -1, lastVisible = -1;
            for (int i = FirstLineAt(viewTop); i < _layout.VisualLines.Count; i++)
            {
                var vl = _layout.VisualLines[i];
                double lineY = _layout.LineYPositions[i];
                if (lineY + _layout.GetEffectiveLineHeight(vl) < viewTop) continue;
                if (lineY > viewBottom) break;
                if (firstVisible < 0) firstVisible = i;
                lastVisible = i;
            }

            // Both caches, in this order: building a line visual draws the line, and drawing a
            // line reads the FormattedText cache. That used to be safe because everything ran
            // inside OnRender; now that this runs at arrange time, ahead of the render, the
            // text cache has to be ready here rather than there.
            _search.EnsureSearchMatchesCurrent();
            EnsureLineFtCache(_layout.VisualLines.Count, _docsCanvas.RenderVersion);
            EnsureLineVisualCache(_layout.VisualLines.Count, _docsCanvas.RenderVersion);
            DropLineVisualsForSelectionChange();
            DropLineVisualsForHighlightChange();
            SyncLineVisuals(firstVisible, lastVisible);
            _docsCanvas.ContentScroll.Y = -effectiveScroll;
        }

        /// <summary>
        /// Index of the first visual line that could be visible at <paramref name="viewTop"/>.
        /// </summary>
        /// <remarks>
        /// Line Y positions ascend, so this is a binary search rather than a walk from zero.
        /// Several passes over the visible lines - backgrounds, colour spans, tables, the
        /// range scan itself - each used to start at line 0 and skip forward, which costs
        /// nothing on a short document and a great deal on a long one: scrolled into the
        /// middle of a 2895-block report that is about 1500 wasted iterations per pass, five
        /// passes, every frame.
        ///
        /// Steps back over any line tall enough to still intrude from above.
        /// </remarks>
        internal int FirstLineAt(double viewTop)
        {
            var ys = _layout.LineYPositions;
            if (ys.Count == 0) return 0;

            int lo = 0, hi = ys.Count - 1, found = ys.Count;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (ys[mid] >= viewTop) { found = mid; hi = mid - 1; }
                else lo = mid + 1;
            }

            int i = Math.Min(found, ys.Count - 1);
            while (i > 0 && ys[i - 1] + _layout.GetEffectiveLineHeight(_layout.VisualLines[i - 1]) >= viewTop)
                i--;
            return i;
        }

        /// <summary>
        /// Drops the cached visuals of lines that draw the given image, so they are rebuilt
        /// with its pixels.
        /// </summary>
        /// <remarks>
        /// An image finishing its load changes one or two lines. Bumping RenderVersion, which
        /// is what this replaced, discarded every cached line instead - measured as a burst of
        /// 641 rebuilds in a single frame while scrolling a document with several images, for
        /// the sake of the handful of lines that actually changed.
        ///
        /// Layout is untouched: the size was read from the image header before it loaded, so
        /// nothing moves and only these lines need redrawing.
        /// </remarks>
        internal void DropLineVisualsForImage(string url)
        {
            if (_lineVisuals == null || _content.ParsedBlocks == null) return;

            int limit = Math.Min(_lineVisuals.Length, _layout.VisualLines.Count);
            for (int i = 0; i < limit; i++)
            {
                if (_lineVisuals[i] is not { } dv) continue;
                if (!LineDrawsImage(_layout.VisualLines[i], url)) continue;

                _docsCanvas.ContentLayer.Children.Remove(dv);
                _lineVisuals[i] = null;
                if (_lineFt != null && i < _lineFt.Length) _lineFt[i] = null;
            }
        }

        private bool LineDrawsImage(VisualLine vl, string url)
        {
            var images = vl.Group != null
                ? vl.Group.JoinedParsed.Images
                : (vl.BlockIndex >= 0 && vl.BlockIndex < _content.ParsedBlocks!.Count
                    ? _content.ParsedBlocks[vl.BlockIndex].Images
                    : null);

            if (images == null) return false;
            foreach (var img in images)
                if (string.Equals(img.Url, url, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// A line's height in whole pixels: from its own snapped origin to the next line's.
        /// </summary>
        /// <remarks>
        /// Backgrounds have to use this rather than the effective line height. A line box is
        /// 21.28 px at the default font, and a cached visual is placed at a rounded origin, so
        /// a rect of 21.28 either stops short of the next line - leaving a hairline of
        /// untinted canvas through a code block - or laps over it and doubles the alpha there.
        /// Rounding both ends of the same line makes each one end exactly where the next
        /// begins.
        ///
        /// A paragraph gap is not swallowed by this: the height is derived from the line's own
        /// box, not from the distance to the next line, so a gap stays the canvas colour as it
        /// does today.
        /// </remarks>
        private double SnappedLineHeight(int i, VisualLine vl)
        {
            double y = _layout.LineYPositions[i];
            return Math.Round(y + _layout.GetEffectiveLineHeight(vl)) - Math.Round(y);
        }

        /// <summary>The selection the cached line visuals were built under.</summary>
        private (bool Has, int Sb, int So, int Eb, int Eo, bool Rect, int C0, int C1) _visualsSel;

        /// <summary>
        /// Drops the cached visuals of every line the selection has moved across.
        /// </summary>
        /// <remarks>
        /// A line's picture now contains its slice of the selection, so changing the selection
        /// makes those pictures wrong. Bumping RenderVersion would drop every cached line for
        /// what is usually a one-line change, which is the mistake
        /// <see cref="DropLineVisualsForImage"/> was written to avoid; this drops the union of
        /// the old and the new selection instead.
        ///
        /// Block granularity, and the union rather than the symmetric difference, because both
        /// are simple to get right and a selection is contiguous: dragging down a line touches
        /// the lines of one or two blocks, and only a select-all reaches the whole screen -
        /// once, not per frame.
        ///
        /// The columns are part of the signature as well. A rectangular table selection can be
        /// dragged sideways without its block range changing at all, and the highlight would
        /// otherwise stay where it was.
        /// </remarks>
        private void DropLineVisualsForSelectionChange()
        {
            var doc = _doc.Document;
            var rect = _docsCanvas.TryGetTableRectSelection();

            (bool, int, int, int, int, bool, int, int) now;
            if (rect is { } r)
                now = (true, r.StartBlock, 0, r.EndBlock, 0, true, r.StartCol, r.EndCol);
            else if (doc.HasSelection)
            {
                var (sb, so, eb, eo) = doc.GetOrderedSelection();
                now = (true, sb, so, eb, eo, false, 0, 0);
            }
            else now = (false, 0, 0, 0, 0, false, 0, 0);

            if (now.Equals(_visualsSel)) return;

            int lo = int.MaxValue, hi = int.MinValue;
            if (_visualsSel.Has) { lo = Math.Min(lo, _visualsSel.Sb); hi = Math.Max(hi, _visualsSel.Eb); }
            if (now.Item1) { lo = Math.Min(lo, now.Item2); hi = Math.Max(hi, now.Item4); }
            _visualsSel = now;

            if (lo > hi || _lineVisuals == null || _visualsHi < _visualsLo) return;

            for (int i = _visualsLo; i <= _visualsHi && i < _lineVisuals.Length; i++)
            {
                if (_lineVisuals[i] is not { } dv) continue;
                if (i >= _layout.VisualLines.Count) continue;
                int bi = _layout.VisualLines[i].BlockIndex;
                if (bi < lo || bi > hi) continue;

                _docsCanvas.ContentLayer.Children.Remove(dv);
                _lineVisuals[i] = null;
            }
        }

        /// <summary>The search highlights the cached line visuals were built under.</summary>
        private int _visualsHighlightSig;

        /// <summary>
        /// Drops every cached line when the search highlights change.
        /// </summary>
        /// <remarks>
        /// Wholesale, unlike the selection, and for two reasons. Matches sit anywhere in the
        /// document rather than in one contiguous run, so there is no cheap range to drop; and
        /// the signature is a hash, which says that something changed but not what. It is
        /// affordable because a highlight change is user-initiated - a keystroke in the find
        /// box, or F3 - and costs one screenful of rebuilds, where the selection changes on
        /// every frame of a drag and could not be treated this way.
        /// </remarks>
        private void DropLineVisualsForHighlightChange()
        {
            int now = _search.SearchHighlightSignature;
            if (now == _visualsHighlightSig) return;
            _visualsHighlightSig = now;

            if (_lineVisuals == null || _visualsHi < _visualsLo) return;

            for (int i = _visualsLo; i <= _visualsHi && i < _lineVisuals.Length; i++)
            {
                if (_lineVisuals[i] is not { } dv) continue;
                _docsCanvas.ContentLayer.Children.Remove(dv);
                _lineVisuals[i] = null;
            }
        }

        /// <summary>Renders one line into its own cached visual. False if it already had one.</summary>
        private bool BuildLineVisual(int i)
        {
            if (_lineVisuals == null || i < 0 || i >= _lineVisuals.Length) return false;
            if (i >= _layout.VisualLines.Count) return false;
            if (_lineVisuals[i] != null) return false;

            var vl = _layout.VisualLines[i];
            var dv = new DrawingVisual
            {
                // Rasterised once and composited thereafter. RenderAtScale has to follow
                // DPI and zoom, or the bitmap is resampled and the text is soft.
                CacheMode = new BitmapCache
                {
                    RenderAtScale = _rendering.Measure.DpiScale,
                    SnapsToDevicePixels = false,
                },
                Transform = new TranslateTransform(0, Math.Round(_layout.LineYPositions[i])),
            };
            using (var dc = dv.RenderOpen())
            {
                // lineY == scrollY draws the line at the origin of its own visual.
                double y = _layout.LineYPositions[i];

                // Before the text, so ClearType has a known background to filter against.
                // Full canvas width, because that is what the passes this will eventually
                // absorb cover; the gap a fractional line height leaves against the next
                // line shows the canvas fill, which is this same brush.
                if (_docsCanvas.OpaqueLineVisuals)
                    dc.DrawRectangle(_rendering.Palette.Background, null,
                        new Rect(0, 0, _rendering.ActualWidth, SnappedLineHeight(i, vl)));

                DrawLineContent(dc, i, vl, y, y);
            }

            _lineVisuals[i] = dv;
            _docsCanvas.ContentLayer.Children.Add(dv);
            if (_visualsHi < _visualsLo) { _visualsLo = _visualsHi = i; }
            else { if (i < _visualsLo) _visualsLo = i; if (i > _visualsHi) _visualsHi = i; }
            return true;
        }

        private void TrimLineVisuals(int firstVisible, int lastVisible)
        {
            if (_lineVisuals == null || _visualsHi < _visualsLo) return;

            int lo = Math.Max(0, firstVisible - LineVisualWindow);
            int hi = Math.Min(_lineVisuals.Length - 1, lastVisible + LineVisualWindow);

            for (int i = _visualsLo; i < lo && i <= _visualsHi; i++) Drop(i);
            for (int i = _visualsHi; i > hi && i >= _visualsLo; i--) Drop(i);

            _visualsLo = Math.Max(_visualsLo, lo);
            _visualsHi = Math.Min(_visualsHi, hi);

            void Drop(int i)
            {
                if (_lineVisuals![i] is not { } dv) return;
                _docsCanvas.ContentLayer.Children.Remove(dv);
                _lineVisuals[i] = null;
            }
        }

        private FormattedText?[]? _lineFt;
        private int _lineFtVersion = -1;
        private int _lineFtLo, _lineFtHi = -1;
        private const int LineFtWindow = 400;

        private void EnsureLineFtCache(int count, int version)
        {
            if (_lineFtVersion != version || _lineFt == null || _lineFt.Length < count)
            {
                _lineFt = new FormattedText?[count];
                _lineFtVersion = version;
                _lineFtLo = 0;
                _lineFtHi = -1;
            }
        }

        /// <summary>Drops cached lines that have scrolled well outside the viewport.</summary>
        private void TrimLineFtCache(int firstVisible, int lastVisible)
        {
            if (_lineFt == null || _lineFtHi < _lineFtLo) return;
            int lo = Math.Max(0, firstVisible - LineFtWindow);
            int hi = Math.Min(_lineFt.Length - 1, lastVisible + LineFtWindow);
            for (int i = _lineFtLo; i < lo && i <= _lineFtHi; i++) _lineFt[i] = null;
            for (int i = _lineFtHi; i > hi && i >= _lineFtLo; i--) _lineFt[i] = null;
            _lineFtLo = Math.Max(_lineFtLo, lo);
            _lineFtHi = Math.Min(_lineFtHi, hi);
        }

        private void NoteCached(int i)
        {
            if (_lineFtHi < _lineFtLo) { _lineFtLo = _lineFtHi = i; return; }
            if (i < _lineFtLo) _lineFtLo = i;
            if (i > _lineFtHi) _lineFtHi = i;
        }

        public RenderingContext(
            IRenderingServices rendering,
            ILayoutDataServices layout,
            IParsedContentServices content,
            IDocumentServices doc,
            IScrollServices scroll,
            ITableServices table,
            IVisualModeServices visual,
            IImageServices images,
            ISearchServices search,
            ILoggingServices logging,
            ICanvasOperations canvas,
            INavigationServices navigation,
            DocsCanvas docsCanvas)
        {
            _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _visual = visual ?? throw new ArgumentNullException(nameof(visual));
            _images = images ?? throw new ArgumentNullException(nameof(images));
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _docsCanvas = docsCanvas ?? throw new ArgumentNullException(nameof(docsCanvas));
        }

        /// <summary>
        /// Main rendering orchestration. Renders backgrounds, text, selection, cursor, and images.
        /// </summary>
        public void OnRender(DrawingContext dc)
        {
            // Timed so the scroll controller can pace repaints against what a frame costs.
            long _t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int _firstVisible = -1;

            _rendering.Measure.EnsureMeasured(_docsCanvas);
            dc.DrawRectangle(_rendering.Palette.Background, null,
                new Rect(0, 0, _rendering.ActualWidth, _rendering.ActualHeight));

            if (_content.ParsedBlocks == null)
                return;

            // Whole pixels. Sub-pixel scrolling was tried over cached lines (phase 3) and
            // backed out: translating a bitmap by a fraction resamples it, which softens the
            // text and, as a coast decays and the fraction drifts slowly, beats into a visible
            // interference pattern. Over live-rasterised text it is worse still - each line
            // grid-fits on its own and the spacing between them wriggles.
            double effectiveScroll = Math.Round(_scroll.Scroll.EffectiveOffset);
            double viewTop = effectiveScroll;
            double viewBottom = effectiveScroll + _rendering.ActualHeight;

            // The code, colour block, inline colour and table tints used to be four passes
            // here. They draw from DrawLineContent now, under the opaque fill rather than
            // beneath it. A table's borders are not a fill and stay whole-table geometry, in
            // the overlay below. See design/Opaque Line Visuals.md phases 2 and 3.

            // Selection and the search highlights both draw from DrawLineContent now, inside
            // the line rather than beneath it, and in that order - navigating to a match also
            // selects it, and painted the other way round the selection would cover the
            // current match and make it indistinguishable from the rest. Nothing the canvas
            // paints is under the content layer any more; see design/Opaque Line Visuals.md.
            _search.EnsureSearchMatchesCurrent();

            EnsureLineFtCache(_layout.VisualLines.Count, _docsCanvas.RenderVersion);
            int _lastVisible = -1;

            for (int i = FirstLineAt(viewTop); i < _layout.VisualLines.Count; i++)
            {
                var vl = _layout.VisualLines[i];
                double lineH = _layout.GetEffectiveLineHeight(vl);
                double lineY = _layout.LineYPositions[i];
                if (lineY + lineH < viewTop) continue;
                if (lineY > viewBottom) break;

                if (_firstVisible < 0) _firstVisible = i;
                _lastVisible = i;
            }

            // On the cached path there is nothing to draw here: the line visuals were built
            // and positioned in ArrangeOverride, ahead of this render pass. See UpdateContentLayer.
            if (!_docsCanvas.CachedLineVisuals)
            {
                // The comparison path, reached only when the F9 toggle is enabled: draws
                // every visible line here, as everything did before lines were cached.
                if (_docsCanvas.ContentLayer.Children.Count > 0)
                    _docsCanvas.ContentLayer.Children.Clear();

                for (int i = Math.Max(0, _firstVisible); i <= _lastVisible; i++)
                    DrawLineContent(dc, i, _layout.VisualLines[i], _layout.LineYPositions[i], effectiveScroll);
            }

            if (_firstVisible >= 0) TrimLineFtCache(_firstVisible, _lastVisible);

            // Above the text, so it goes in the overlay child rather than here: a child visual
            // draws after the element's own content, and these would otherwise be underneath.
            using (var odc = _docsCanvas.OverlayLayer.RenderOpen())
            {
                // First in the overlay, so the caret and squiggles still sit above them.
                if (_visual.IsVisual)
                    _table.TableRenderer.DrawTableLines(odc, effectiveScroll, viewTop, viewBottom);

                if (_docsCanvas.SpellCheckEnabled)
                    DrawSpellingErrors(odc, effectiveScroll, viewTop, viewBottom);

                if (_docsCanvas.ShowPageBreaks)
                    DrawPageBreaks(odc, effectiveScroll, viewTop, viewBottom);

                if (_docsCanvas._cursorVisible && _docsCanvas.IsFocused && _layout.VisualLines.Count > 0)
                {
                    int vli = _docsCanvas.CursorToVisualLineIndex();
                    double cx = DocsCanvas._padding + _docsCanvas.CursorXInVisualLine(vli);
                    double cy = _layout.LineYPositions[vli] - effectiveScroll;
                    double lineH = _layout.GetEffectiveLineHeight(_layout.VisualLines[vli]);
                    odc.DrawLine(_rendering.Palette.CursorPen, new Point(cx, cy), new Point(cx, cy + lineH));
                }

                if (!_visual.IsVisual && _images.ImagePreview == DocsCanvas.ImagePreviewMode.OnHover && _docsCanvas._hoveredImage != null)
                    DrawHoverImagePreview(odc);

                if (_docsCanvas.ShowRenderPathBadge)
                    DrawModeBadge(odc);
            }

            // Feeds the adaptive repaint cap: the scroll controller stretches its interval
            // when a frame is too dear to draw at the display's rate.

            // Both at Background priority, below Input and Render. Queued at the default
            // Normal these outrank the very things they interrupt: the caller is a render, so
            // every frame was scheduling work that preempted the next one.
            //
            // The minimap is throttled as well. It rebuilds its bitmap whenever the viewport
            // scrolls past the line range it cached, which while scrolling happens every few
            // frames, and that rebuild is far dearer than a canvas frame. Invalidating it once
            // per canvas frame therefore injected a periodic stall - measured as late frames
            // arriving every third frame, 19ms against a 7.4ms median, with the canvas's own
            // OnRender perfectly normal on those frames. A viewport thumbnail does not need
            // 135 updates a second.
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            bool minimapDue =
                (now - _lastMinimapTick) > System.Diagnostics.Stopwatch.Frequency / MinimapHz;
            if (minimapDue) _lastMinimapTick = now;

            _canvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                () =>
                {
                    if (minimapDue && _canvas.Minimap is FrameworkElement fe)
                        fe.InvalidateVisual();
                    _docsCanvas.ScrollStateChanged?.Invoke();
                });
        }

        /// <summary>Says which of the two paths is drawing. Only shown off the default path.</summary>
        private void DrawModeBadge(DrawingContext dc)
        {
            // F8 first, so the switches read in key order. The fill only exists on the cached
            // path, so on direct draw there is nothing to report for it.
            string path = _docsCanvas.CachedLineVisuals ? "F9: cached visuals" : "F9: direct draw";
            string text = _docsCanvas.CachedLineVisuals
                ? (_docsCanvas.OpaqueLineVisuals ? "F8: opaque (ClearType)" : "F8: transparent (greyscale)")
                  + "  |  " + path
                : path;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                TextMeasurer.NormalTypeface, 11, _rendering.Palette.Syntax, _rendering.Measure.DpiScale);
            double x = _rendering.ActualWidth - ft.Width - 12;
            dc.DrawRectangle(_rendering.Palette.CodeBackground, null,
                new Rect(x - 6, 4, ft.Width + 12, ft.Height + 4));
            dc.DrawText(ft, new Point(x, 6));
        }

        /// <summary>
        /// Draws one visual line's own content. Everything here positions itself as
        /// <c>lineY - scrollY</c>, so passing the two equal draws the line at the origin -
        /// which is how a line is rendered into its own cached visual.
        /// </summary>
        private void DrawLineContent(DrawingContext dc, int i, VisualLine vl,
            double lineY, double scrollY)
        {
            // Before the vl.Length guard, so an empty line inside a code or colour block is
            // still tinted. Drawn from here rather than from OnRender because an opaque line
            // visual covers anything painted beneath it - and because both render paths call
            // this method, so one copy serves them both.
            double bgH = SnappedLineHeight(i, vl);
            DrawCodeBlockBackground(dc, vl, lineY, scrollY, bgH);
            DrawColorBlockBackground(dc, vl, lineY, scrollY, bgH);
            DrawInlineColorBackground(dc, vl, lineY, scrollY, bgH);
            if (_visual.IsVisual && _content.ParsedBlocks != null
                && vl.BlockIndex < _content.ParsedBlocks.Count)
                _table.TableRenderer.DrawTableRowBackground(
                    dc, _content.ParsedBlocks[vl.BlockIndex], lineY, scrollY, bgH);
            DrawSelectionForLine(dc, vl, lineY, scrollY, bgH);
            _search.DrawSearchHighlightsForLine(dc, vl, lineY, scrollY, bgH);

            if (vl.Length > 0)
            {
                if (vl.Group != null)
                {
                    DrawJoinedLine(dc, vl, lineY, scrollY, i);
                }
                else
                {
                    var parsed = _content.ParsedBlocks[vl.BlockIndex];
                    // Materialised lazily: GetBlockText is a StringBuilder.ToString(), and on
                    // a cache hit the line's text is never needed at all.
                    string? _blockTextLazy = null;
                    string blockText() => _blockTextLazy ??= _doc.Document.GetBlockText(vl.BlockIndex);
                    double fontSize = _rendering.Measure.GetBlockFontSize(parsed.Kind);
                    var baseTypeface = TextMeasurer.GetBlockBaseTypeface(parsed.Kind);
                    var map = _visual.IsVisual ? _content.VisualMaps?[vl.BlockIndex] : null;

                    double textX = _docsCanvas._layoutEngine.GetTextStartXForVisualLine(vl, i);

                    if (_visual.IsVisual && parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
                    {
                        DrawBlockquoteBar(dc, lineY, scrollY);
                    }

                    if (_visual.IsVisual && parsed.Kind == BlockKind.ThematicBreak)
                    {
                        double ruleY = lineY - scrollY + 10;
                        double ruleRight = _rendering.ActualWidth - DocsCanvas._padding;
                        dc.DrawLine(_rendering.Palette.TableBorderPen, new Point(DocsCanvas._padding, ruleY), new Point(ruleRight, ruleY));
                    }
                    else if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null)
                    {
                        _table.TableRenderer.DrawTableRow(dc, vl, blockText(), parsed, lineY, scrollY, fontSize, baseTypeface);
                    }
                    else if (map != null)
                    {
                        if (HasImagesOnLine(vl, map))
                        {
                            DrawVisualLineWithImages(dc, vl, blockText(), parsed, map,
                                lineY, scrollY, fontSize, baseTypeface);
                        }
                        else
                        {
                            // In source mode, only draw actual markdown syntax (bullets, numbers, etc)
                            // but NOT continuation indentation - show raw text at column 0
                            if (map.ReplacementPrefix != null && vl.StartOffset == 0 && !map.IsContinuationIndent)
                            {
                                if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                                {
                                    var spacing = _layout.GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                                            new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(lineY - scrollY),
                                            parsed.Kind);
                                    }
                                }
                                else if (parsed.Kind == BlockKind.UnorderedListItem)
                                {
                                    var spacing = _layout.GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX),
                                            new AbsoluteY(lineY - scrollY),
                                            parsed.Kind, parsed.ListNestingLevel);
                                    }
                                }
                                else if (parsed.Kind == BlockKind.OrderedListItem)
                                {
                                    var spacing = _layout.GetVisualLineSpacing(vl);
                                    if (spacing != null)
                                    {
                                        DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX),
                                            new AbsoluteY(lineY - scrollY),
                                            map.ReplacementPrefix!, fontSize, parsed.ListNestingLevel);
                                    }
                                }
                                else
                                {
                                    var prefixFt = new FormattedText(map.ReplacementPrefix!,
                                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                        TextMeasurer.NormalTypeface, fontSize, _rendering.Palette.Syntax, _rendering.Measure.DpiScale);
                                    dc.DrawText(prefixFt, new Point(DocsCanvas._padding, lineY - scrollY));
                                }
                            }

                            var ft = _lineFt![i];
                            if (ft == null)
                            {
                                string displayText = map.BuildDisplayString(blockText(), vl.StartOffset, vl.Length);
                                if (displayText.Length > 0)
                                {
                                    ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                                        FlowDirection.LeftToRight, baseTypeface, fontSize,
                                        _rendering.Palette.Foreground, _rendering.Measure.DpiScale);
                                    ApplyInlineStylesVisual(ft, vl, parsed, map);
                                    if (parsed.Kind == BlockKind.TaskListItemChecked)
                                    {
                                        ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, displayText.Length);
                                        ft.SetTextDecorations(TextDecorations.Strikethrough, 0, displayText.Length);
                                    }
                                    _lineFt[i] = ft;
                                    NoteCached(i);
                                }
                            }
                            if (ft != null)
                                dc.DrawText(ft, new Point(textX, lineY - scrollY));
                        }
                    }
                    else
                    {
                        var ft = _lineFt![i];
                        if (ft == null)
                        {
                            string text = blockText().Substring(vl.StartOffset, vl.Length);
                            ft = new FormattedText(text, CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, baseTypeface, fontSize,
                                _rendering.Palette.Foreground, _rendering.Measure.DpiScale);
                            ApplyInlineStyles(ft, vl, parsed, blockText());
                            _lineFt[i] = ft;
                            NoteCached(i);
                        }
                        dc.DrawText(ft, new Point(textX, lineY - scrollY));

                        if (_docsCanvas._showWhitespace)
                            DrawTrailingSpaceDots(dc, vl, blockText(), parsed, textX, lineY - scrollY);

                        if (_images.ImagePreview == DocsCanvas.ImagePreviewMode.Inline && parsed.Images != null)
                            DrawSourceInlineImages(dc, vl, parsed.Images, lineY, scrollY);
                    }
                }
            }
        }


        private void DrawJoinedLine(DrawingContext dc, VisualLine vl,
            double lineY, double effectiveScroll, int index)
        {
            var group = vl.Group!;

            if (HasImagesOnLine(vl, group.JoinedMap))
            {
                // Not cached: this path draws images as well as text, so it does not reduce to
                // a single FormattedText the way the others do.
                DrawVisualLineWithImages(dc, vl, group.JoinedText, group.JoinedParsed,
                    group.JoinedMap, lineY, effectiveScroll,
                    _rendering.Measure.GetBlockFontSize(BlockKind.Paragraph), TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph));
                return;
            }

            var ft = _lineFt![index];
            if (ft == null)
            {
                ft = BuildJoinedLineText(vl);
                if (ft == null) return;
                _lineFt[index] = ft;
                NoteCached(index);
            }

            dc.DrawText(ft, new Point(DocsCanvas._padding, lineY - effectiveScroll));
        }

        /// <summary>
        /// Builds the exact <see cref="FormattedText"/> that <see cref="DrawJoinedLine"/> draws,
        /// or null when the line renders nothing. Shared with the test hook that measures how
        /// wide a joined line really renders, so layout can be checked against reality.
        /// </summary>
        internal FormattedText? BuildJoinedLineText(VisualLine vl)
        {
            var group = vl.Group!;

            // Build base display string (with "¶" only, no spaces yet)
            var baseDisplay = group.JoinedMap.BuildDisplayString(group.JoinedText, vl.StartOffset, vl.Length);

            // Add visual spaces after pilcrows
            var softBreaks = new HashSet<int>(group.SoftBreakOffsets);
            var sb = new System.Text.StringBuilder();
            int visPos = 0;
            for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
            {
                if (group.JoinedMap.IsHidden(i)) continue;

                // Add the visible character from base display
                if (visPos < baseDisplay.Length)
                    sb.Append(baseDisplay[visPos]);

                // Add visual space after pilcrow
                if (softBreaks.Contains(i) && i < group.JoinedText.Length && group.JoinedText[i] == '¶')
                    sb.Append(' ');

                visPos++;
            }

            string displayText = sb.ToString();
            if (displayText.Length == 0) return null;

            double fontSize = _rendering.Measure.GetBlockFontSize(BlockKind.Paragraph);
            var baseTypeface = TextMeasurer.GetBlockBaseTypeface(BlockKind.Paragraph);

            var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, baseTypeface, fontSize,
                _rendering.Palette.Foreground, _rendering.Measure.DpiScale);
            ApplyInlineStylesVisual(ft, vl, group.JoinedParsed, group.JoinedMap, group.SoftBreakOffsets);

            // Color soft breaks (pilcrow + visual space)
            visPos = 0;
            int displayPos = 0;
            for (int i = vl.StartOffset; i < vl.StartOffset + vl.Length; i++)
            {
                if (group.JoinedMap.IsHidden(i)) continue;

                if (softBreaks.Contains(i) && displayPos < displayText.Length)
                    ft.SetForegroundBrush(_rendering.Palette.Syntax, displayPos, 2);  // color pilcrow + visual space

                // Advance display position (by 2 if soft break with visual space, else by 1)
                displayPos += (softBreaks.Contains(i)) ? 2 : 1;
                visPos++;
            }

            return ft;
        }

        private void ApplyInlineStyles(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            if (parsed.SyntaxTokens != null)
            {
                ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens);
                return;
            }

            foreach (var run in parsed.Runs)
            {
                int runEnd = run.Start + run.Length;
                int vlEnd = vl.StartOffset + vl.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

                int localStart = Math.Max(0, run.Start - vl.StartOffset);
                int localEnd = Math.Min(vl.Length, runEnd - vl.StartOffset);
                int count = localEnd - localStart;
                if (count <= 0) continue;

                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

                switch (run.Style)
                {
                    case InlineStyle.Bold:
                        ft.SetFontWeight(FontWeights.Bold, localStart, count);
                        break;
                    case InlineStyle.Italic:
                        ft.SetFontStyle(FontStyles.Italic, localStart, count);
                        break;
                    case InlineStyle.BoldItalic:
                        ft.SetFontWeight(FontWeights.Bold, localStart, count);
                        ft.SetFontStyle(FontStyles.Italic, localStart, count);
                        break;
                    case InlineStyle.Code:
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, localStart, count);
                        break;
                    case InlineStyle.Strikethrough:
                        ft.SetTextDecorations(TextDecorations.Strikethrough, localStart, count);
                        break;
                    case InlineStyle.Link:
                        ft.SetForegroundBrush(DocsCanvas._checkboxCheckedBrush, localStart, count);
                        ft.SetTextDecorations(TextDecorations.Underline, localStart, count);
                        break;
                }
            }

            ApplyColorSpans(ft, vl, parsed, blockText);
            ApplySyntaxDimming(ft, vl, parsed, blockText);
        }

        private void ApplySyntaxTokens(FormattedText ft, VisualLine vl, IReadOnlyList<SyntaxToken> tokens, BlockVisualMap? map = null)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var token in tokens)
            {
                int tokenEnd = token.Start + token.Length;
                if (tokenEnd <= vl.StartOffset || token.Start >= vlEnd) continue;

                int localStart;
                int count;
                if (map != null)
                {
                    int rawStart = Math.Max(token.Start, vl.StartOffset);
                    int rawEnd = Math.Min(tokenEnd, vlEnd);
                    int vlVisualOffset = map.RawToVisual(vl.StartOffset);
                    int visStart = map.RawToVisual(rawStart) - vlVisualOffset;
                    int visEnd = map.RawToVisual(rawEnd) - vlVisualOffset;
                    localStart = visStart;
                    count = visEnd - visStart;
                }
                else
                {
                    localStart = Math.Max(0, token.Start - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, tokenEnd - vl.StartOffset);
                    count = localEnd - localStart;
                }
                if (count <= 0) continue;

                var brush = GetSyntaxBrush(token.ForegroundArgb);
                ft.SetForegroundBrush(brush, localStart, count);
            }
        }

        private Brush GetSyntaxBrush(int argb)
        {
            if (_docsCanvas._syntaxBrushCache.TryGetValue(argb, out var cached))
                return cached;

            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            _docsCanvas._syntaxBrushCache[argb] = brush;
            return brush;
        }

        private void ApplyColorSpans(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            if (parsed.ColorSpans == null && parsed.BlockColor == null) return;
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;

            int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
                ? MarkdownParser.GetContentEnd(blockText) - 1
                : int.MaxValue;

            if (parsed.BlockColor?.Foreground is { } blockFg)
            {
                int len = Math.Min(vl.Length, hardBreakClip - vl.StartOffset);
                if (len > 0)
                    ft.SetForegroundBrush(_rendering.GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, len);
            }

            if (parsed.ColorSpans != null)
            {
                foreach (var cs in parsed.ColorSpans)
                {
                    int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                    int vlEnd = vl.StartOffset + vl.Length;
                    if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                    int localStart = Math.Max(0, cs.Start - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, csEnd - vl.StartOffset);
                    int count = localEnd - localStart;
                    if (count <= 0) continue;

                    if (cs.Foreground is { } fg)
                    {
                        ft.SetForegroundBrush(_rendering.GetCachedBrush(fg.R, fg.G, fg.B), localStart, count);
                    }
                }
            }
        }

        private void ApplySyntaxDimming(FormattedText ft, VisualLine vl, ParsedBlock parsed, string blockText)
        {
            int vlEnd = vl.StartOffset + vl.Length;

            int ls = parsed.LeadingSpaces;

            if (parsed.Kind >= BlockKind.Heading1 && parsed.Kind <= BlockKind.Heading6)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                if (stripped.Length > 0 && stripped[0] == '#')
                {
                    int hashCount = parsed.Kind - BlockKind.Heading1 + 1;
                    int totalPrefix = ls + hashCount + 1;
                    int localStart = Math.Max(0, 0 - vl.StartOffset);
                    int localEnd = Math.Min(vl.Length, totalPrefix - vl.StartOffset);
                    if (localEnd > localStart)
                        ft.SetForegroundBrush(_rendering.Palette.Syntax, localStart, localEnd - localStart);
                }
            }

            if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked && vl.StartOffset == 0 && vl.Length >= ls + 6)
            {
                ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, ls + 6);
            }
            else if (parsed.Kind == BlockKind.UnorderedListItem && vl.StartOffset == 0 && vl.Length >= ls + 2)
            {
                ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, ls + 2);
            }
            else if (parsed.Kind == BlockKind.OrderedListItem && vl.StartOffset == 0)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                int prefixLen = MarkdownParser.GetOrderedListPrefixLength(stripped);
                if (prefixLen > 0 && vl.Length >= ls + prefixLen)
                    ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, ls + prefixLen);
            }

            if (parsed.Kind == BlockKind.Blockquote && vl.StartOffset == 0)
            {
                var stripped = ls > 0 ? blockText[ls..] : blockText;
                if (stripped.Length > 0 && stripped[0] == '>')
                {
                    int dimLength = ls + 1;
                    if (stripped.Length > 1 && stripped[1] == ' ')
                        dimLength += 1;
                    if (vl.Length >= dimLength)
                        ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, dimLength);
                }
            }

            if (parsed.Kind == BlockKind.LinkDefinition)
                ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, vl.Length);

            if (parsed.Kind is BlockKind.ThemeDefinition or BlockKind.ColorDivOpen or BlockKind.ColorDivClose)
                ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, vl.Length);

            if (parsed.Kind is BlockKind.TableSeparatorRow or BlockKind.ThematicBreak or BlockKind.SetextUnderline)
            {
                ft.SetForegroundBrush(_rendering.Palette.Syntax, 0, vl.Length);
            }
            else if (parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow)
            {
                for (int ci = vl.StartOffset; ci < vlEnd; ci++)
                {
                    if (ci > 0 && blockText[ci - 1] == '\\') continue;
                    if (blockText[ci] == '|')
                        DimRange(ft, vl, ci, 1);
                }
            }

            if (parsed.Images != null)
            {
                foreach (var img in parsed.Images)
                {
                    int imgEnd = img.Start + img.Length;
                    if (imgEnd <= vl.StartOffset || img.Start >= vlEnd) continue;

                    DimRange(ft, vl, img.Start, 2);
                    int closeBracket = img.Start + 2 + img.AltText.Length;
                    DimRange(ft, vl, closeBracket, imgEnd - closeBracket);
                }
            }

            if (parsed.Links != null)
            {
                foreach (var link in parsed.Links)
                {
                    if (link.Text == link.Url) continue;
                    int linkEnd = link.Start + link.Length;
                    if (linkEnd <= vl.StartOffset || link.Start >= vlEnd) continue;

                    DimRange(ft, vl, link.Start, 1);
                    int closeBracket = link.Start + 1 + link.Text.Length;
                    DimRange(ft, vl, closeBracket, linkEnd - closeBracket);
                }
            }

            foreach (var run in parsed.Runs)
            {
                if (run.Style is InlineStyle.Normal or InlineStyle.Image or InlineStyle.Link) continue;
                int runEnd = run.Start + run.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;

                if (run.Style is InlineStyle.Code or InlineStyle.Strikethrough)
                {
                    int markerLen = run.Style == InlineStyle.Code
                        ? CountBackticks(blockText, run.Start)
                        : MarkdownParser.GetMarkerLength(run.Style);
                    if (markerLen == 0) continue;

                    DimRange(ft, vl, run.Start, markerLen);
                    DimRange(ft, vl, runEnd - markerLen, markerLen);
                }
            }

            if (parsed.EmphasisMarkers != null)
            {
                foreach (var marker in parsed.EmphasisMarkers)
                    DimRange(ft, vl, marker.Start, marker.Length);
            }

            if (MarkdownParser.IsTrailingHardBreak(parsed, blockText))
                DimRange(ft, vl, MarkdownParser.GetContentEnd(blockText) - 1, 1);

            if (parsed.Kind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine)
            {
                var tagRanges = MarkdownParser.FindInlineColorTagRanges(blockText);
                if (tagRanges != null)
                {
                    foreach (var tag in tagRanges)
                        DimRange(ft, vl, tag.Start, tag.Length);
                }
            }

            if (parsed.Kind == BlockKind.HtmlBlock)
            {
                var htmlCommentRanges = MarkdownParser.FindHtmlCommentRanges(blockText);
                if (htmlCommentRanges != null)
                {
                    foreach (var commentRange in htmlCommentRanges)
                        DimRange(ft, vl, commentRange.Start, commentRange.Length);
                }
            }
        }

        /// <summary>
        /// Number of extra display characters inserted before <paramref name="rawOffset"/> by
        /// soft breaks: a joined line renders each pilcrow followed by a visual space, which
        /// <see cref="BlockVisualMap.RawToVisual"/> knows nothing about. Without this shift,
        /// every style range after a pilcrow is applied one position too early per soft break
        /// — the wrong characters get bold/monospace, and the line renders at a different
        /// width than layout measured, so its tail is clipped.
        /// </summary>
        private static int SoftBreakShift(int[]? softBreaks, int rawOffset)
        {
            if (softBreaks == null) return 0;
            int shift = 0;
            foreach (int p in softBreaks)
            {
                if (p >= rawOffset) break;
                shift++;
            }
            return shift;
        }

        private void ApplyInlineStylesVisual(FormattedText ft, VisualLine vl,
            ParsedBlock parsed, BlockVisualMap map, int[]? softBreaks = null)
        {
            if (parsed.SyntaxTokens != null)
            {
                ApplySyntaxTokens(ft, vl, parsed.SyntaxTokens, map);
                return;
            }

            int vlEnd = vl.StartOffset + vl.Length;
            int vlVisBase = map.RawToVisual(vl.StartOffset) + SoftBreakShift(softBreaks, vl.StartOffset);
            foreach (var run in parsed.Runs)
            {
                if (run.Style == InlineStyle.Normal || run.Style == InlineStyle.Image) continue;
                int runEnd = run.Start + run.Length;
                if (runEnd <= vl.StartOffset || run.Start >= vlEnd) continue;
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) continue;

                int rawStart = Math.Max(run.Start, vl.StartOffset);
                int rawEnd = Math.Min(runEnd, vlEnd);
                int visStart = map.RawToVisual(rawStart) + SoftBreakShift(softBreaks, rawStart) - vlVisBase;
                int visEnd = map.RawToVisual(rawEnd) + SoftBreakShift(softBreaks, rawEnd) - vlVisBase;
                int count = visEnd - visStart;
                if (count <= 0) continue;

                switch (run.Style)
                {
                    case InlineStyle.Bold:
                        ft.SetFontWeight(FontWeights.Bold, visStart, count);
                        break;
                    case InlineStyle.Italic:
                        ft.SetFontStyle(FontStyles.Italic, visStart, count);
                        break;
                    case InlineStyle.BoldItalic:
                        ft.SetFontWeight(FontWeights.Bold, visStart, count);
                        ft.SetFontStyle(FontStyles.Italic, visStart, count);
                        break;
                    case InlineStyle.Code:
                        ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visStart, count);
                        break;
                    case InlineStyle.Strikethrough:
                        ft.SetTextDecorations(TextDecorations.Strikethrough, visStart, count);
                        break;
                    case InlineStyle.Link:
                        ft.SetForegroundBrush(DocsCanvas._checkboxCheckedBrush, visStart, count);
                        ft.SetTextDecorations(TextDecorations.Underline, visStart, count);
                        break;
                }
            }

            ApplyColorSpansVisual(ft, vl, parsed, map, softBreaks);
        }

        private void ApplyColorSpansVisual(FormattedText ft, VisualLine vl,
            ParsedBlock parsed, BlockVisualMap map, int[]? softBreaks = null)
        {
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
            int ftLen = ft.Text.Length;

            if (parsed.BlockColor?.Foreground is { } blockFg)
            {
                int vlVisLen = Math.Min(ftLen, map.RawToVisual(vl.StartOffset + vl.Length) - map.RawToVisual(vl.StartOffset));
                if (vlVisLen > 0)
                    ft.SetForegroundBrush(_rendering.GetCachedBrush(blockFg.R, blockFg.G, blockFg.B), 0, vlVisLen);
            }

            var colorSpans = map.ColorSpans;
            if (colorSpans == null) return;

            int vlEnd = vl.StartOffset + vl.Length;
            int vlVisBase = map.RawToVisual(vl.StartOffset) + SoftBreakShift(softBreaks, vl.StartOffset);

            foreach (var cs in colorSpans)
            {
                int csEnd = cs.Start + cs.Length;
                if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                int rawStart = Math.Max(cs.Start, vl.StartOffset);
                int rawEnd = Math.Min(csEnd, vlEnd);
                int visStart = map.RawToVisual(rawStart) + SoftBreakShift(softBreaks, rawStart) - vlVisBase;
                int visEnd = map.RawToVisual(rawEnd) + SoftBreakShift(softBreaks, rawEnd) - vlVisBase;
                visEnd = Math.Min(visEnd, ftLen);
                int count = visEnd - visStart;
                if (count <= 0 || visStart < 0 || visStart >= ftLen) continue;

                if (cs.Foreground is { } fg)
                {
                    ft.SetForegroundBrush(_rendering.GetCachedBrush(fg.R, fg.G, fg.B), visStart, count);
                }
            }
        }

        private bool HasImagesOnLine(VisualLine vl, BlockVisualMap map)
        {
            if (map.Images == null) return false;
            int vlEnd = vl.StartOffset + vl.Length;
            foreach (var img in map.Images)
            {
                if (img.Start >= vl.StartOffset && img.Start < vlEnd) return true;
                if (img.Start >= vlEnd) break;
            }
            return false;
        }

        private void DrawVisualLineWithImages(DrawingContext dc, VisualLine vl,
            string blockText, ParsedBlock parsed, BlockVisualMap map,
            double lineY, double effectiveScroll, double fontSize, Typeface baseTypeface)
        {
            if (map.Images == null) return;

            double x = DocsCanvas._padding;
            double screenY = lineY - effectiveScroll;
            double textLineH = _rendering.Measure.GetLineHeight(vl.BlockKind);
            double totalLineH = vl.OverrideHeight > textLineH ? vl.OverrideHeight : textLineH;

            if (map.ReplacementPrefix != null && vl.StartOffset == 0)
            {
                if (parsed.Kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked)
                {
                    var spacing = _layout.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawTaskListCheckbox(dc, parsed.Kind == BlockKind.TaskListItemChecked,
                            new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY), parsed.Kind);
                    }
                }
                else if (parsed.Kind == BlockKind.UnorderedListItem)
                {
                    var spacing = _layout.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawListBullet(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                            parsed.Kind, parsed.ListNestingLevel);
                    }
                    x += _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else if (parsed.Kind == BlockKind.OrderedListItem)
                {
                    var spacing = _layout.GetVisualLineSpacing(vl);
                    if (spacing != null)
                    {
                        DrawOrderedListNumber(dc, new AbsoluteX(spacing.MarkerStartX), new AbsoluteY(screenY),
                            map.ReplacementPrefix, fontSize, parsed.ListNestingLevel);
                    }
                    x += _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else if (map.IsContinuationIndent)
                {
                    x += _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
                else
                {
                    var prefixFt = new FormattedText(map.ReplacementPrefix,
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        TextMeasurer.NormalTypeface, fontSize, _rendering.Palette.Syntax, _rendering.Measure.DpiScale);
                    dc.DrawText(prefixFt, new Point(DocsCanvas._padding, screenY));
                    x += _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix, map.PrefixMeasureKind);
                }
            }

            int vlEnd = vl.StartOffset + vl.Length;
            int segStart = vl.StartOffset;

            foreach (var img in map.Images)
            {
                if (img.Start >= vlEnd) break;
                if (img.Start + img.Length <= vl.StartOffset) continue;

                if (segStart < img.Start)
                    x = DrawTextSegment(dc, blockText, segStart, img.Start, map, parsed, fontSize, baseTypeface, x, screenY);

                var (imgW, imgH) = _images.GetImageSize(img, _layout.LayoutMaxWidth);
                var cached = _images.ImageCache.Get(img.Url, _images.DocumentBasePath, _layout.LayoutMaxWidth);
                double imgY = screenY + totalLineH - imgH;
                if (cached != null)
                {
                    dc.DrawImage(cached.Value.Image, new Rect(x, imgY, imgW, imgH));
                }
                else
                {
                    DrawImagePlaceholder(dc, x, imgY, imgW, imgH, img.AltText);
                }
                x += imgW;

                segStart = img.Start + img.Length;
            }

            if (segStart < vlEnd)
                DrawTextSegment(dc, blockText, segStart, vlEnd, map, parsed, fontSize, baseTypeface, x, screenY);
        }

        private double DrawTextSegment(DrawingContext dc, string blockText,
            int rawStart, int rawEnd, BlockVisualMap map, ParsedBlock parsed,
            double fontSize, Typeface baseTypeface, double x, double screenY)
        {
            string displayText = map.BuildDisplayString(blockText, rawStart, rawEnd - rawStart);
            if (displayText.Length == 0) return x;

            var ft = new FormattedText(displayText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, baseTypeface, fontSize,
                _rendering.Palette.Foreground, _rendering.Measure.DpiScale);

            int visBase = 0;
            int runIdx = 0;
            for (int r = rawStart; r < rawEnd; r++)
            {
                if (map.IsHidden(r)) continue;
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, r, ref runIdx);
                if (style != InlineStyle.Normal && style != InlineStyle.Image && visBase < displayText.Length)
                {
                    switch (style)
                    {
                        case InlineStyle.Bold:
                            ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                            break;
                        case InlineStyle.Italic:
                            ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                            break;
                        case InlineStyle.BoldItalic:
                            ft.SetFontWeight(FontWeights.Bold, visBase, 1);
                            ft.SetFontStyle(FontStyles.Italic, visBase, 1);
                            break;
                        case InlineStyle.Code:
                            ft.SetFontFamily(TextMeasurer.MonoTypeface.FontFamily, visBase, 1);
                            break;
                        case InlineStyle.Strikethrough:
                            ft.SetTextDecorations(TextDecorations.Strikethrough, visBase, 1);
                            break;
                    }
                }
                visBase++;
            }

            dc.DrawText(ft, new Point(x, screenY));
            return x + ft.WidthIncludingTrailingWhitespace;
        }

        private void DrawTaskListCheckbox(DrawingContext dc, bool isChecked, AbsoluteX markerCenterX, AbsoluteY screenY,
            BlockKind blockKind)
        {
            double lineH = _rendering.Measure.GetLineHeight(blockKind);
            double baseline = _rendering.Measure.GetBaseline(blockKind);
            double fontSize = _rendering.Measure.GetBlockFontSize(blockKind);
            double capHeight = fontSize * _rendering.Measure.CapsHeightRatio;
            double boxSize = Math.Round(lineH * 0.65);

            // Align checkbox with text baseline, same as bullets
            double checkboxCenterY = screenY.Value + baseline - capHeight / 2;
            double checkboxX = markerCenterX.Value - boxSize / 2;
            double checkboxY = Math.Round(checkboxCenterY - boxSize / 2);
            var rect = new Rect(checkboxX, checkboxY, boxSize, boxSize);
            double radius = 2.5;

            if (isChecked)
            {
                dc.DrawRoundedRectangle(DocsCanvas._checkboxCheckedBrush, null, rect, radius, radius);
                var pen = new Pen(_rendering.Palette.Background, 1.6);
                pen.Freeze();
                double cx = checkboxX, cy = checkboxY, s = boxSize;
                dc.DrawLine(pen,
                    new Point(cx + s * 0.22, cy + s * 0.52),
                    new Point(cx + s * 0.42, cy + s * 0.72));
                dc.DrawLine(pen,
                    new Point(cx + s * 0.42, cy + s * 0.72),
                    new Point(cx + s * 0.78, cy + s * 0.28));
            }
            else
            {
                var pen = new Pen(_rendering.Palette.Syntax, 1.2);
                pen.Freeze();
                dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            }
        }

        private void DrawListBullet(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
            BlockKind blockKind, int nestingLevel)
        {
            double lineH = _rendering.Measure.GetLineHeight(blockKind);
            double baseline = _rendering.Measure.GetBaseline(blockKind);
            double fontSize = _rendering.Measure.GetBlockFontSize(blockKind);
            double capHeight = fontSize * _rendering.Measure.CapsHeightRatio;
            double bulletSize = Math.Round(lineH * 0.32);

            // markerCenterX is the center of the marker area; adjust to draw position
            double bulletX = markerCenterX.Value - bulletSize / 2;
            double bulletCenterY = screenY.Value + baseline - capHeight / 2;
            double bulletY = Math.Round(bulletCenterY - bulletSize / 2);

            DrawBulletAtPosition(dc, bulletX, bulletY, bulletSize, nestingLevel);
        }

        private void DrawBulletAtPosition(DrawingContext dc, double bulletX, double bulletY, double bulletSize, int nestingLevel)
        {
            int shape = nestingLevel % 3;
            if (shape == 0)
            {
                dc.DrawEllipse(_rendering.Palette.Syntax, null, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                    bulletSize / 2, bulletSize / 2);
            }
            else if (shape == 1)
            {
                var pen = new Pen(_rendering.Palette.Syntax, 1.2);
                pen.Freeze();
                dc.DrawEllipse(null, pen, new Point(bulletX + bulletSize / 2, bulletY + bulletSize / 2),
                    bulletSize / 2, bulletSize / 2);
            }
            else
            {
                dc.DrawRectangle(_rendering.Palette.Syntax, null, new Rect(bulletX, bulletY, bulletSize, bulletSize));
            }
        }

        private void DrawOrderedListNumber(DrawingContext dc, AbsoluteX markerCenterX, AbsoluteY screenY,
            string replacementPrefix, double fontSize, int nestingLevel)
        {
            string trimmed = replacementPrefix.TrimStart();
            string numberText = trimmed.TrimEnd();

            int delimiterPos = numberText.IndexOfAny(new[] { '.', ')' });
            string numberOnly = delimiterPos > 0 ? numberText.Substring(0, delimiterPos) : numberText;

            var ftNumberOnly = new FormattedText(numberOnly, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
                _rendering.Palette.Syntax, _rendering.Measure.DpiScale);

            // Center number at marker center position (adjusted for width)
            double numberX = markerCenterX.Value - ftNumberOnly.WidthIncludingTrailingWhitespace / 2;
            dc.DrawText(ftNumberOnly, new Point(numberX, screenY.Value));

            // Draw delimiter after number
            double delimiterX = numberX + ftNumberOnly.WidthIncludingTrailingWhitespace;
            var ftDelimiter = new FormattedText(numberText.Substring(numberOnly.Length), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextMeasurer.NormalTypeface, fontSize,
                _rendering.Palette.Syntax, _rendering.Measure.DpiScale);
            dc.DrawText(ftDelimiter, new Point(delimiterX, screenY.Value));
        }

        private void DrawBlockquoteBar(DrawingContext dc, double lineY, double effectiveScroll)
        {
            var aligner = new ContentBlockAligner(DocsCanvas._padding, _rendering.Measure.ListIndent);
            double barX = aligner.GetBlockquoteBarX();
            double barWidth = 3;
            double barY = lineY - effectiveScroll;
            double barHeight = _rendering.Measure.GetLineHeight(BlockKind.Blockquote);
            var barBrush = new SolidColorBrush(Color.FromArgb(80, 150, 150, 150));
            barBrush.Freeze();
            dc.DrawRectangle(barBrush, null, new Rect(barX, barY, barWidth, barHeight));
        }

        /// <summary>Tints one line of a fenced or indented code block.</summary>
        private void DrawCodeBlockBackground(DrawingContext dc, VisualLine vl,
            double lineY, double scrollY, double bgH)
        {
            if (vl.BlockKind is not BlockKind.FencedCodeLine and not BlockKind.IndentedCodeLine) return;

            dc.DrawRectangle(_rendering.Palette.CodeBackground, null,
                new Rect(0, lineY - scrollY, _rendering.ActualWidth, bgH));
        }

        /// <summary>Tints one line of a block carrying a colour tag's background.</summary>
        private void DrawColorBlockBackground(DrawingContext dc, VisualLine vl,
            double lineY, double scrollY, double bgH)
        {
            if (_content.ParsedBlocks == null) return;
            if (vl.BlockIndex >= _content.ParsedBlocks.Count) return;

            var parsed = _content.ParsedBlocks[vl.BlockIndex];
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
            if (parsed.BlockColor?.Background is not { } bg) return;

            dc.DrawRectangle(_docsCanvas.GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                new Rect(0, lineY - scrollY, _rendering.ActualWidth, bgH));
        }

        /// <summary>Tints the spans of one line that carry an inline colour background.</summary>
        private void DrawInlineColorBackground(DrawingContext dc, VisualLine vl,
            double lineY, double scrollY, double bgH)
        {
            if (_content.ParsedBlocks == null) return;

            string blockText;
            ParsedBlock parsed;
            BlockVisualMap? map;
            IReadOnlyList<ColorSpan>? colorSpans;

            // Everything that can rule the line out comes before the block text is
            // materialised. GetBlockText is a StringBuilder.ToString(), the drawing path
            // below defers it deliberately for that reason, and a document with no colour
            // spans at all would otherwise pay for one per line on every rebuild - and
            // typing rebuilds every visible line.
            if (vl.Group != null)
            {
                var group = vl.Group;
                parsed = group.JoinedParsed;
                map = group.JoinedMap;
                colorSpans = map.ColorSpans;
                if (colorSpans == null || colorSpans.Count == 0) return;
                if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null) return;
                blockText = group.JoinedText;
            }
            else
            {
                if (vl.BlockIndex >= _content.ParsedBlocks.Count) return;
                parsed = _content.ParsedBlocks[vl.BlockIndex];
                if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
                map = _visual.IsVisual ? _content.VisualMaps?[vl.BlockIndex] : null;
                colorSpans = _visual.IsVisual ? map?.ColorSpans : parsed.ColorSpans;
                if (colorSpans == null || colorSpans.Count == 0) return;
                if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null) return;
                blockText = _doc.Document.GetBlockText(vl.BlockIndex);
            }

            int hardBreakClip = MarkdownParser.IsTrailingHardBreak(parsed, blockText)
                ? MarkdownParser.GetContentEnd(blockText) - 1
                : int.MaxValue;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var cs in colorSpans)
            {
                if (cs.Background == null) continue;
                int csEnd = Math.Min(cs.Start + cs.Length, hardBreakClip);
                if (csEnd <= vl.StartOffset || cs.Start >= vlEnd) continue;

                int rangeStart = Math.Max(cs.Start, vl.StartOffset);
                int rangeEnd = Math.Min(csEnd, vlEnd);

                double x1 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, rangeStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                double x2 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, rangeEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                    x1 += prefixW;
                    x2 += prefixW;
                }

                double w = x2 - x1;
                if (w <= 0) continue;

                var bg = cs.Background.Value;
                dc.DrawRectangle(_docsCanvas.GetCachedBrush(40, bg.R, bg.G, bg.B), null,
                    new Rect(DocsCanvas._padding + x1, lineY - scrollY, w, bgH));
            }
        }

        /// <summary>Draws the selection over one line, if it covers any of it.</summary>
        /// <remarks>
        /// Inside the line visual rather than under it: an opaque line covers anything OnRender
        /// paints beneath. That makes a line's picture depend on the selection, which is view
        /// state and changes without the content changing, so
        /// <see cref="DropLineVisualsForSelectionChange"/> drops the lines it moved across.
        /// </remarks>
        private void DrawSelectionForLine(DrawingContext dc, VisualLine vl,
            double lineY, double scrollY, double bgH)
        {
            var rectSel = _docsCanvas.TryGetTableRectSelection();
            if (rectSel != null)
            {
                DrawTableRectSelectionForLine(dc, vl, lineY, scrollY, bgH, rectSel.Value);
                return;
            }

            if (!_doc.Document.HasSelection) return;

            var (sb, so, eb, eo) = _doc.Document.GetOrderedSelection();

            if (vl.Group != null)
            {
                DrawJoinedSelection(dc, vl, lineY, bgH, scrollY, sb, so, eb, eo);
                return;
            }

            int vlEnd = vl.StartOffset + vl.Length;

            bool startsBeforeSelEnd = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, eb, eo) < 0;
            bool endsAfterSelStart = Document.ComparePositions(vl.BlockIndex, vlEnd, sb, so) > 0;
            if (!startsBeforeSelEnd || !endsAfterSelStart) return;

            int hlStart = Document.ComparePositions(vl.BlockIndex, vl.StartOffset, sb, so) >= 0
                ? vl.StartOffset : so;
            int hlEnd = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) <= 0
                ? vlEnd : eo;

            var parsed = _content.ParsedBlocks![vl.BlockIndex];
            string blockText = _doc.Document.GetBlockText(vl.BlockIndex);
            var map = _visual.IsVisual ? _content.VisualMaps?[vl.BlockIndex] : null;

            double x1, x2;
            if (_visual.IsVisual && parsed.Table != null && parsed.TableRow != null)
            {
                if (_table.TableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                {
                    x1 = _table.TableRenderer.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                    x2 = _table.TableRenderer.CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                }
                else
                {
                    x1 = 0; x2 = 0;
                }
            }
            else
            {
                x1 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);
                x2 = _rendering.MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                    parsed.Runs, parsed.Kind, map);

                if (map != null && map.ReplacementPrefix != null && vl.StartOffset == 0)
                {
                    double prefixW = _rendering.Measure.MeasureReplacementPrefix(map.ReplacementPrefix!, map.PrefixMeasureKind);
                    x1 += prefixW;
                    x2 += prefixW;
                }
            }

            bool selectionContinues = Document.ComparePositions(vl.BlockIndex, vlEnd, eb, eo) < 0;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            double selW = Math.Max(0, x2 - x1);
            if (selW > 0)
                dc.DrawRectangle(_rendering.Palette.Selection, null,
                    new Rect(DocsCanvas._padding + x1, lineY - scrollY, selW, bgH));
        }

        /// <summary>One line's slice of a rectangular table selection.</summary>
        private void DrawTableRectSelectionForLine(DrawingContext dc, VisualLine vl,
            double lineY, double scrollY, double bgH,
            (int StartCol, int EndCol, int StartBlock, int EndBlock, TableInfo Table) r)
        {
            if (vl.BlockIndex < r.StartBlock || vl.BlockIndex > r.EndBlock) return;
            if (!_table.TableColumnWidths.TryGetValue(r.Table, out var colWidths)) return;
            if (_content.ParsedBlocks![vl.BlockIndex].IsTableSeparator) return;

            double xStart = 0;
            for (int c = 0; c < r.StartCol && c < colWidths.Length; c++)
                xStart += colWidths[c];
            double xEnd = xStart;
            for (int c = r.StartCol; c <= r.EndCol && c < colWidths.Length; c++)
                xEnd += colWidths[c];

            dc.DrawRectangle(_rendering.Palette.Selection, null,
                new Rect(DocsCanvas._padding + xStart, lineY - scrollY, xEnd - xStart, bgH));
        }

        private void DrawJoinedSelection(DrawingContext dc, VisualLine vl,
            double lineY, double lineH, double effectiveScroll,
            int sb, int so, int eb, int eo)
        {
            var group = vl.Group!;
            int selStartJoined = group.SourceToJoined(sb, so);
            int selEndJoined = group.SourceToJoined(eb, eo);
            if (selStartJoined < 0)
                selStartJoined = sb < group.FirstBlock ? 0 : group.JoinedText.Length;
            if (selEndJoined < 0)
                selEndJoined = eb > group.LastBlock ? group.JoinedText.Length : 0;

            int vlStart = vl.StartOffset;
            int vlEnd = vl.StartOffset + vl.Length;

            if (vlEnd <= selStartJoined || vlStart >= selEndJoined) return;

            int hlStart = Math.Max(vlStart, selStartJoined);
            int hlEnd = Math.Min(vlEnd, selEndJoined);

            double x1 = _rendering.MeasureJoinedRange(group, vlStart, hlStart - vlStart);
            double x2 = _rendering.MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

            bool selectionContinues = vlEnd < selEndJoined;
            if (selectionContinues && x2 - x1 < 4)
                x2 = x1 + 4;
            else if (selectionContinues)
                x2 += 4;

            double selW = Math.Max(0, x2 - x1);
            if (selW > 0)
                dc.DrawRectangle(_rendering.Palette.Selection, null,
                    new Rect(DocsCanvas._padding + x1, lineY - effectiveScroll, selW, lineH));
        }

        private void DrawTrailingSpaceDots(DrawingContext dc, VisualLine vl,
            string blockText, ParsedBlock parsed, double textX, double screenY)
        {
            if (parsed.Kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine) return;
            if (vl.StartOffset + vl.Length < blockText.Length) return;

            int trailStart = blockText.Length;
            while (trailStart > 0 && blockText[trailStart - 1] == ' ') trailStart--;
            int trailCount = blockText.Length - trailStart;
            if (trailCount == 0) return;

            var measureKind = !_visual.IsVisual && parsed.Kind is BlockKind.TableHeaderRow or BlockKind.TableDataRow
                ? BlockKind.Paragraph : parsed.Kind;

            double x = textX;
            int runIdx = 0;
            for (int i = vl.StartOffset; i < trailStart; i++)
            {
                var style = TextMeasurer.GetStyleAtOffset(parsed.Runs, i, ref runIdx);
                x += _rendering.Measure.MeasureCharWidth(blockText[i], measureKind, style);
            }

            double spaceW = _rendering.Measure.MeasureCharWidth(' ', measureKind, InlineStyle.Normal);
            double dotSize = Math.Max(2, spaceW * 0.25);
            double lineH = _rendering.Measure.GetLineHeight(parsed.Kind);
            double cy = screenY + lineH / 2;

            for (int i = 0; i < trailCount; i++)
            {
                double cx = x + spaceW * (i + 0.5);
                dc.DrawEllipse(_rendering.Palette.Syntax, null, new Point(cx, cy), dotSize / 2, dotSize / 2);
            }
        }

        private void DimRange(FormattedText ft, VisualLine vl, int docStart, int length)
        {
            int vlEnd = vl.StartOffset + vl.Length;
            int localStart = Math.Max(0, docStart - vl.StartOffset);
            int localEnd = Math.Min(vl.Length, docStart + length - vl.StartOffset);
            if (localEnd > localStart)
                ft.SetForegroundBrush(_rendering.Palette.Syntax, localStart, localEnd - localStart);
        }

        private void DrawImagePlaceholder(DrawingContext dc, double x, double y, double w, double h, string? altText)
        {
            dc.DrawRectangle(DocsCanvas._imagePlaceholderBrush, null, new Rect(x, y, w, h));
            if (!string.IsNullOrEmpty(altText))
            {
                var altFt = new FormattedText(altText,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    TextMeasurer.NormalTypeface, Math.Round(11 * _rendering.Measure.ZoomFactor), _rendering.Palette.Syntax, _rendering.Measure.DpiScale);
                altFt.MaxTextWidth = Math.Max(1, w);
                altFt.MaxTextHeight = Math.Max(1, h);
                dc.DrawText(altFt, new Point(x + 2, y + 2));
            }
        }

        private void DrawSourceInlineImages(DrawingContext dc, VisualLine vl,
            IReadOnlyList<InlineImage> images, double lineY, double effectiveScroll)
        {
            double textLineH = _rendering.Measure.GetLineHeight(vl.BlockKind);
            double imgY = lineY - effectiveScroll + textLineH;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var img in images)
            {
                if (img.Start < vl.StartOffset || img.Start >= vlEnd) continue;

                var (imgW, imgH) = _images.GetImageSize(img, _layout.LayoutMaxWidth);
                var cached = _images.ImageCache.Get(img.Url, _images.DocumentBasePath, _layout.LayoutMaxWidth);
                if (cached != null)
                {
                    dc.DrawImage(cached.Value.Image, new Rect(DocsCanvas._padding, imgY, imgW, imgH));
                }
                else
                {
                    DrawImagePlaceholder(dc, DocsCanvas._padding, imgY, imgW, imgH, img.AltText);
                }
                imgY += imgH;
            }
        }

        private void DrawHoverImagePreview(DrawingContext dc)
        {
            var img = _docsCanvas._hoveredImage!.Value;
            double maxPreviewW = Math.Min(_layout.LayoutMaxWidth, 300);
            var (imgW, imgH) = _images.GetImageSize(img, maxPreviewW);
            var cached = _images.ImageCache.Get(img.Url, _images.DocumentBasePath, maxPreviewW);

            double popupX = Math.Min(_docsCanvas._hoverPosition.X, Math.Max(0, _rendering.ActualWidth - imgW - 16));
            double popupY = _docsCanvas._hoverPosition.Y + 20;
            if (popupY + imgH + 8 > _rendering.ActualHeight)
                popupY = Math.Max(0, _docsCanvas._hoverPosition.Y - imgH - 12);

            var borderPen = new Pen(_rendering.Palette.Syntax, 1);
            borderPen.Freeze();
            var bgBrush = _rendering.Palette.Background.Clone();
            bgBrush.Freeze();

            dc.DrawRectangle(bgBrush, borderPen,
                new Rect(popupX - 4, popupY - 4, imgW + 8, imgH + 8));

            if (cached != null)
            {
                dc.DrawImage(cached.Value.Image, new Rect(popupX, popupY, imgW, imgH));
            }
            else
            {
                DrawImagePlaceholder(dc, popupX, popupY, imgW, imgH, img.AltText);
            }
        }

        private static int CountBackticks(string text, int start)
        {
            int count = 0;
            while (start + count < text.Length && text[start + count] == '`') count++;
            return count;
        }

        // Search-related rendering (delegated from parent)
        private void DrawSpellingErrors(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
            => _docsCanvas.DrawSpellingErrors(dc, effectiveScroll, viewTop, viewBottom);

        private void DrawPageBreaks(DrawingContext dc, double effectiveScroll, double viewTop, double viewBottom)
            => _docsCanvas.DrawPageBreaks(dc, effectiveScroll, viewTop, viewBottom);
    }
}
