using System;
using System.Collections.Generic;
using EnigmaVirtual;

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
    /// The event's own images, in order, keeping only urls of a supported image type (empty, video,
    /// pdf or extension-less urls are skipped, so they are never requested) and identical urls once (a
    /// repeat would show as two identical slides). Empty when the event has no images - never another
    /// event's: a card must not show a photo that belongs to a different event.
    /// </summary>
    public static List<string> Gallery(TimelineItemVO item)
    {
        List<string> gallery = new List<string>();
        if (item == null || item.images == null)
            return gallery;

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MediaVO image in item.images)
        {
            string url = image != null ? image.url : null;
            if (IsImageUrl(url) && seen.Add(url))
                gallery.Add(url);
        }

        return gallery;
    }

    /// <summary>
    /// Whether a CMS url names an image type the loader can decode (<see cref="MediaFile.isValidImageFileType"/>).
    /// False for null or empty, which that method would throw on, and for a url
    /// <c>Path.GetExtension</c> rejects (illegal path characters), so bad content never stops a rebuild.
    /// </summary>
    public static bool IsImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        try
        {
            return MediaFile.isValidImageFileType(url);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Moves one slide and wraps at both ends.</summary>
    public static int Step(int current, int count, int direction)
    {
        if (count <= 0)
            return 0;

        return ((current + direction) % count + count) % count;
    }

    /// <summary>
    /// Whether a gallery cycles and shows slide indicators. A single image has nothing to cycle and no
    /// dot; an empty gallery has neither.
    /// </summary>
    public static bool CyclesSlides(int galleryCount)
    {
        return galleryCount > 1;
    }

    /// <summary>
    /// Slide indicator dots: one per image for a small gallery, none for 0 or 1 image, and none past
    /// <paramref name="maxDots"/> - a larger gallery shows a "current / total" counter instead.
    /// </summary>
    public static int IndicatorDots(int galleryCount, int maxDots)
    {
        return CyclesSlides(galleryCount) && galleryCount <= maxDots ? galleryCount : 0;
    }

    public static bool IndicatorCounter(int galleryCount, int maxDots)
    {
        return CyclesSlides(galleryCount) && galleryCount > maxDots;
    }

    /// <summary>
    /// One card's slideshow interval in seconds, from a random sample in [0, 1] mapped onto
    /// [min, max]. The bounds may be given in either order; the interval is never below 0.1 s.
    /// </summary>
    public static float SlideInterval(float sample01, float min, float max)
    {
        float low = Math.Min(min, max);
        float high = Math.Max(min, max);
        float interval = low + (high - low) * Math.Max(0f, Math.Min(1f, sample01));
        return Math.Max(0.1f, interval);
    }

    /// <summary>
    /// Crossfade progress in [0, 1]: the incoming image's alpha, the outgoing one's being 1 minus it. A
    /// duration of 0 or less finishes at once.
    /// </summary>
    public static float FadeProgress(float elapsed, float duration)
    {
        if (duration <= 0f)
            return 1f;

        return Math.Max(0f, Math.Min(1f, elapsed / duration));
    }

    /// <summary>
    /// True when the next slide is due at <paramref name="now"/>, and moves <paramref name="due"/> on by
    /// one interval. If more than a whole interval was missed - the app was paused, or the card was
    /// inactive - it restarts from now instead of firing once per missed interval.
    /// </summary>
    public static bool SlideDue(float now, ref float due, float interval)
    {
        if (now < due)
            return false;

        due = now - due >= interval ? now + interval : due + interval;
        return true;
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
