using System;
using System.Collections.Generic;

/// <summary>
/// The timeline's placement rules, with no Unity objects in them so <c>TimelineLayoutSelfCheck</c>
/// can exercise them without a scene.
/// </summary>
public static class TimelineLayout
{
    public const int FirstDecade = 1920;
    /// <summary>Four-digit years only. A larger year from the JSON would add one decade per ten years.</summary>
    public const int LastSupportedYear = 9999;
    public const int RowCount = 2;

    /// <summary>
    /// Whether an event can sit on the axis. Everything else is excluded from layout and galleries,
    /// and reported once by the timeline screen.
    /// </summary>
    public static bool IsSupportedYear(int year)
    {
        return year >= FirstDecade && year <= LastSupportedYear;
    }

    public static int DecadeOf(int year)
    {
        return (int)Math.Floor(year / 10.0) * 10;
    }

    /// <summary>
    /// The last decade on the axis. Always computed from the COMPLETE content, never the filtered
    /// view, so switching categories off cannot shrink the axis.
    /// </summary>
    public static int LastDecade(IEnumerable<TimelineItemVO> all)
    {
        int last = FirstDecade;

        if (all != null)
        {
            foreach (TimelineItemVO item in all)
            {
                if (item != null && IsSupportedYear(item.year))
                    last = Math.Max(last, DecadeOf(item.year));
            }
        }

        return last;
    }

    // Items fill a decade top to bottom, then move one column right.
    public static int ColumnOf(int indexInDecade) { return indexInDecade / RowCount; }
    public static int RowOf(int indexInDecade) { return indexInDecade % RowCount; }
    public static int ColumnCount(int itemCount) { return (itemCount + RowCount - 1) / RowCount; }

    /// <summary>
    /// The first column of a decade has its images at the bottom, the second on top, alternating.
    /// The count restarts in every decade.
    /// </summary>
    public static bool ImageOnTop(int columnInDecade) { return columnInDecade % 2 == 1; }

    public static int Compare(TimelineItemVO a, TimelineItemVO b)
    {
        int byYear = a.year.CompareTo(b.year);
        return byYear != 0 ? byYear : a.id.CompareTo(b.id);
    }

    /// <summary>
    /// The items of one decade that belong to an active category, in chronological order. An empty
    /// category set yields nothing, which is how every decade collapses when all are switched off.
    /// </summary>
    public static List<TimelineItemVO> ItemsInDecade(IEnumerable<TimelineItemVO> all, int decade, ICollection<int> categories)
    {
        List<TimelineItemVO> result = new List<TimelineItemVO>();

        if (all == null || categories == null)
            return result;

        foreach (TimelineItemVO item in all)
        {
            if (item != null && IsSupportedYear(item.year) && DecadeOf(item.year) == decade
                && categories.Contains(item.categoryId))
                result.Add(item);
        }

        result.Sort(Compare);
        return result;
    }

    /// <summary>
    /// One gallery per category: every image of every event in that category, in event order and
    /// then image order, with identical urls kept once. Deduplicated because placeholder content
    /// reuses the same file, and a repeat would show as two identical slides.
    /// </summary>
    public static Dictionary<int, List<string>> BuildGalleries(IEnumerable<TimelineItemVO> all)
    {
        Dictionary<int, List<string>> galleries = new Dictionary<int, List<string>>();
        Dictionary<int, HashSet<string>> seen = new Dictionary<int, HashSet<string>>();

        if (all == null)
            return galleries;

        // An event that is never shown does not contribute slides either.
        List<TimelineItemVO> ordered = new List<TimelineItemVO>();
        foreach (TimelineItemVO item in all)
        {
            if (item != null && IsSupportedYear(item.year))
                ordered.Add(item);
        }
        ordered.Sort(Compare);

        foreach (TimelineItemVO item in ordered)
        {
            if (!galleries.TryGetValue(item.categoryId, out List<string> gallery))
            {
                gallery = new List<string>();
                galleries[item.categoryId] = gallery;
                seen[item.categoryId] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (item.images == null)
                continue;

            foreach (MediaVO image in item.images)
            {
                string url = image != null ? image.url : null;
                if (!string.IsNullOrEmpty(url) && seen[item.categoryId].Add(url))
                    gallery.Add(url);
            }
        }

        return galleries;
    }

    /// <summary>The slide a card opens on: its own first image, or the gallery's first when it has none.</summary>
    public static int StartSlide(IList<string> gallery, TimelineItemVO item)
    {
        if (gallery == null || item == null || item.images == null)
            return 0;

        foreach (MediaVO image in item.images)
        {
            if (image == null || string.IsNullOrEmpty(image.url))
                continue;

            int index = gallery.IndexOf(image.url);
            return index >= 0 ? index : 0;
        }

        return 0;
    }

    /// <summary>Moves one slide and wraps at both ends.</summary>
    public static int Step(int current, int count, int direction)
    {
        if (count <= 0)
            return 0;

        return ((current + direction) % count + count) % count;
    }

    /// <summary>
    /// +1 for next (a drag to the left), -1 for previous, 0 below the threshold. Evaluated once, at
    /// the end of the gesture, so one gesture moves at most one slide however far it travels.
    /// </summary>
    public static int SwipeDirection(float dragDeltaX, float threshold)
    {
        if (Math.Abs(dragDeltaX) < threshold)
            return 0;

        return dragDeltaX < 0 ? 1 : -1;
    }

    public struct CardFit
    {
        public bool wide;
        public float fontSize;
        /// <summary>False when the text does not fit the wide card even at the minimum size.</summary>
        public bool fits;
    }

    /// <summary>
    /// Picks one of the two card sizes. Narrow when the text fits it at full size; otherwise wide,
    /// at the largest size between <paramref name="fontSize"/> and <paramref name="minFontSize"/>
    /// that fits. Never a third width and never a taller card.
    /// </summary>
    /// <param name="measure">Rendered text height for (text width, font size).</param>
    public static CardFit FitCard(Func<float, float, float> measure, float narrowTextWidth, float wideTextWidth,
        float textHeight, float fontSize, float minFontSize, float fontStep)
    {
        if (measure(narrowTextWidth, fontSize) <= textHeight)
            return new CardFit { wide = false, fontSize = fontSize, fits = true };

        float step = fontStep > 0f ? fontStep : 0.5f;

        for (float size = fontSize; size >= minFontSize - 0.001f; size -= step)
        {
            if (measure(wideTextWidth, size) <= textHeight)
                return new CardFit { wide = true, fontSize = size, fits = true };
        }

        return new CardFit { wide = true, fontSize = minFontSize, fits = false };
    }

    public struct DotStrip
    {
        public float size;
        /// <summary>Distance from one dot's left edge to the next.</summary>
        public float pitch;
        /// <summary>Left edge of the first dot. The last dot ends exactly at the strip's right edge.</summary>
        public float firstX;
    }

    /// <summary>
    /// Right-aligned slide indicators that always stay inside <paramref name="width"/>. The default
    /// size and spacing are kept while they fit; beyond that both shrink by the same factor. A very
    /// large gallery therefore gets very small dots rather than dots outside the card.
    /// </summary>
    public static DotStrip FitDots(int count, float width, float size, float spacing)
    {
        if (count <= 0 || width <= 0f)
            return new DotStrip { size = 0f, pitch = 0f, firstX = Math.Max(width, 0f) };

        float total = count * size + (count - 1) * spacing;
        float scale = total > width ? width / total : 1f;

        return new DotStrip
        {
            size = size * scale,
            pitch = (size + spacing) * scale,
            // A shrunk strip fills the width exactly, so it starts at 0. Computing width - total * scale
            // instead lands a few millionths of a pixel below zero.
            firstX = scale < 1f ? 0f : width - total,
        };
    }

    /// <summary>
    /// A decade's width: a fixed narrow strip when it is empty, otherwise its columns plus padding.
    /// </summary>
    public static float DecadeWidth(IList<float> columnWidths, float emptyWidth, float padLeft, float columnGap, float padRight)
    {
        if (columnWidths == null || columnWidths.Count == 0)
            return emptyWidth;

        float width = padLeft + padRight + columnGap * (columnWidths.Count - 1);
        foreach (float column in columnWidths)
            width += column;

        return width;
    }
}
