using System.Windows;
using System.Windows.Media;

namespace RaisinDocs;

public partial class DocsCanvas
{
    private void DrawSourceInlineImages(DrawingContext dc, VisualLine vl,
        IReadOnlyList<InlineImage> images, double lineY, double effectiveScroll)
    {
        double textLineH = _measure.GetLineHeight(vl.BlockKind);
        double imgY = lineY - effectiveScroll + textLineH;
        int vlEnd = vl.StartOffset + vl.Length;

        foreach (var img in images)
        {
            if (img.Start < vl.StartOffset || img.Start >= vlEnd) continue;

            var (imgW, imgH) = GetImageSize(img, _layoutMaxWidth);
            var cached = _imageCache.Get(img.Url, DocumentBasePath, _layoutMaxWidth);
            if (cached != null)
            {
                dc.DrawImage(cached.Value.Image, new Rect(_padding, imgY, imgW, imgH));
            }
            else
            {
                DrawImagePlaceholder(dc, _padding, imgY, imgW, imgH, img.AltText);
            }
            imgY += imgH;
        }
    }

    private void DrawHoverImagePreview(DrawingContext dc)
    {
        var img = _hoveredImage!.Value;
        double maxPreviewW = Math.Min(_layoutMaxWidth, 300);
        var (imgW, imgH) = GetImageSize(img, maxPreviewW);
        var cached = _imageCache.Get(img.Url, DocumentBasePath, maxPreviewW);

        double popupX = Math.Min(_hoverPosition.X, Math.Max(0, ActualWidth - imgW - 16));
        double popupY = _hoverPosition.Y + 20;
        if (popupY + imgH + 8 > ActualHeight)
            popupY = Math.Max(0, _hoverPosition.Y - imgH - 12);

        var borderPen = new Pen(_palette.Syntax, 1);
        borderPen.Freeze();
        var bgBrush = _palette.Background.Clone();
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

    private bool HandleBackSource()
    {
        int prevBlock = _doc.CursorBlock;
        int prevOffset = _doc.CursorOffset;
        _doc.Backspace();
        bool changed = _doc.CursorBlock != prevBlock || _doc.CursorOffset != prevOffset;
        if (changed) _doc.CollapseSelection();
        return changed;
    }

    private bool HandleDeleteSource()
    {
        int prevBlocks = _doc.BlockCount;
        int prevLen = _doc.GetBlockLength(_doc.CursorBlock);
        _doc.Delete();
        return _doc.BlockCount != prevBlocks ||
               _doc.GetBlockLength(_doc.CursorBlock) != prevLen;
    }

    private void HandleLeftSource(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (sb, so, _, _) = _doc.GetOrderedSelection();
            _doc.CursorBlock = sb;
            _doc.CursorOffset = so;
            _doc.CollapseSelection();
        }
        else
        {
            _doc.MoveLeft();
            if (!shift) _doc.CollapseSelection();
        }
    }

    private void HandleRightSource(bool shift)
    {
        if (!shift && _doc.HasSelection)
        {
            var (_, _, eb, eo) = _doc.GetOrderedSelection();
            _doc.CursorBlock = eb;
            _doc.CursorOffset = eo;
            _doc.CollapseSelection();
        }
        else
        {
            _doc.MoveRight();
            if (!shift) _doc.CollapseSelection();
        }
    }
}
