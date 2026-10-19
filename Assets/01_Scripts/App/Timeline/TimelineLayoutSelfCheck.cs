#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Exercises <see cref="TimelineLayout"/> without a scene. Run from <c>Gema/Timeline/Run Layout
/// Self-Check</c>, or through the pipeline's eval as <c>TimelineLayoutSelfCheck.Run()</c>. Throws on the
/// first failed assertion and returns a PASS line otherwise.
/// </summary>
public static class TimelineLayoutSelfCheck
{
    [UnityEditor.MenuItem("Gema/Timeline/Run Layout Self-Check")]
    private static void RunFromMenu()
    {
        UnityEngine.Debug.Log(Run());
    }

    public static string Run()
    {
        int checks = 0;
        Action<bool, string> check = (condition, what) =>
        {
            checks++;
            if (!condition)
                throw new Exception("TimelineLayout self-check FAILED: " + what);
        };

        TimelineItemVO a = Item(1, 0, 1950, "a.jpg", "shared.png");
        TimelineItemVO b = Item(2, 0, 1926, "shared.png");
        TimelineItemVO c = Item(3, 1, 2011, "c.jpg");
        TimelineItemVO d = Item(4, 0, 1955);
        TimelineItemVO e = Item(0, 0, 1955, "", "e.jpg");
        TimelineItemVO[] all = { a, b, c, d, e };

        // Axis: 1920 to the decade of the latest year in the complete content.
        check(TimelineLayout.LastDecade(all) == 2010, "last decade of 2011 is 2010");
        check(TimelineLayout.LastDecade(new TimelineItemVO[0]) == TimelineLayout.FirstDecade, "empty content keeps 1920");
        check(TimelineLayout.DecadeOf(1929) == 1920 && TimelineLayout.DecadeOf(1930) == 1930, "decade boundaries");

        // Only four-digit years from 1920 reach the axis. Out-of-range years must not extend it.
        TimelineItemVO huge = Item(90, 0, 10000000, "huge.jpg");
        TimelineItemVO max = Item(91, 0, int.MaxValue, "max.jpg");
        TimelineItemVO early = Item(92, 0, 1919, "early.jpg");
        TimelineItemVO[] invalid = { a, c, huge, max, early };
        check(TimelineLayout.LastDecade(invalid) == 2010, "years 10000000 and int.MaxValue do not extend the axis");
        check(TimelineLayout.LastDecade(new[] { Item(93, 0, 9999) }) == 9990, "9999 is the last supported year");
        check(!TimelineLayout.IsSupportedYear(10000) && !TimelineLayout.IsSupportedYear(1919)
            && TimelineLayout.IsSupportedYear(1920) && TimelineLayout.IsSupportedYear(9999), "supported range is 1920-9999");
        check(TimelineLayout.ItemsInDecade(invalid, 1910, new HashSet<int> { 0 }).Count == 0, "1919 is not laid out");

        // Two rows, filling top to bottom, then right.
        int[,] slots = { { 0, 0 }, { 0, 1 }, { 1, 0 }, { 1, 1 }, { 2, 0 } };
        for (int i = 0; i < 5; i++)
            check(TimelineLayout.ColumnOf(i) == slots[i, 0] && TimelineLayout.RowOf(i) == slots[i, 1], "slot of index " + i);
        check(TimelineLayout.ColumnCount(5) == 3 && TimelineLayout.ColumnCount(0) == 0, "column count");

        // Image bottom in the first column, top in the second, alternating.
        check(!TimelineLayout.ImageOnTop(0) && TimelineLayout.ImageOnTop(1) && !TimelineLayout.ImageOnTop(2), "image side alternates");

        // Decade filtering and order: year, then id. All categories off leaves nothing.
        List<TimelineItemVO> fifties = TimelineLayout.ItemsInDecade(all, 1950, new HashSet<int> { 0 });
        check(fifties.Count == 3 && fifties[0] == a && fifties[1] == e && fifties[2] == d, "1950s sorted by year then id");
        check(TimelineLayout.ItemsInDecade(all, 1950, new HashSet<int>()).Count == 0, "all categories off empties a decade");
        check(TimelineLayout.ItemsInDecade(all, 2010, new HashSet<int> { 0 }).Count == 0, "filtered-out decade is empty");

        // Galleries: the event's own images only, in order, empties skipped, urls deduplicated. An event
        // without images gets an empty gallery even when others in its category have some.
        List<string> ga = TimelineLayout.Gallery(a);
        check(ga.Count == 2 && ga[0] == "a.jpg" && ga[1] == "shared.png", "a's gallery is [a, shared], got ["
            + string.Join(", ", ga) + "]");
        check(TimelineLayout.Gallery(d).Count == 0, "d has no images: empty, nothing borrowed from a, b or e");
        List<string> ge = TimelineLayout.Gallery(e);
        check(ge.Count == 1 && ge[0] == "e.jpg", "e skips its empty url");
        check(TimelineLayout.Gallery(Item(5, 0, 1960, "x.jpg", "x.jpg")).Count == 1, "a repeated url is kept once");
        check(TimelineLayout.Gallery(null).Count == 0, "null item: empty gallery");

        // Only supported image types reach a gallery, so nothing else is ever requested. Counter-test:
        // upper-case and jpeg/png urls, with spaces and accents as the CMS sends them, are kept.
        List<string> typed = TimelineLayout.Gallery(Item(6, 0, 1960, "v.mp4", "doc.pdf", "noext", "a.webp",
            "x.gif", "bad<|>\".jpg", "uploads/Imagens Cronologia/1 certidão.JPG", "b.jpeg", "c.png"));
        check(typed.Count == 3 && typed[0] == "uploads/Imagens Cronologia/1 certidão.JPG" && typed[1] == "b.jpeg"
            && typed[2] == "c.png", "only png/jpg/jpeg kept, got [" + string.Join(", ", typed) + "]");
        check(!TimelineLayout.IsImageUrl(null) && !TimelineLayout.IsImageUrl(""), "null and empty are not images, no throw");

        // Cycling wraps both ways; an empty gallery stays at 0.
        check(TimelineLayout.Step(2, 3, 1) == 0 && TimelineLayout.Step(0, 3, -1) == 2, "step wraps");
        check(TimelineLayout.Step(0, 0, 1) == 0, "step on an empty gallery");

        // Slideshow: a single image stays put; only two or more cycle and show dots.
        check(TimelineLayout.Step(0, 1, 1) == 0, "a single image does not advance");
        check(!TimelineLayout.CyclesSlides(0) && !TimelineLayout.CyclesSlides(1) && TimelineLayout.CyclesSlides(2),
            "only galleries of two or more cycle and show dots");

        // Per-card interval: sample mapped onto [min, max], bounds in either order, never below 0.1 s.
        check(TimelineLayout.SlideInterval(0f, 4f, 8f) == 4f && TimelineLayout.SlideInterval(1f, 4f, 8f) == 8f
            && TimelineLayout.SlideInterval(0.5f, 4f, 8f) == 6f, "interval spans 4..8");
        check(TimelineLayout.SlideInterval(2f, 4f, 8f) == 8f && TimelineLayout.SlideInterval(0.5f, 8f, 4f) == 6f,
            "out-of-range sample clamps and swapped bounds still work");
        check(TimelineLayout.SlideInterval(0f, 0f, 0f) == 0.1f, "interval never reaches zero");

        // Timing: not before due; then one interval later; a long gap restarts instead of bursting.
        float due = 10f;
        check(!TimelineLayout.SlideDue(9.9f, ref due, 5f) && due == 10f, "not due early");
        check(TimelineLayout.SlideDue(12f, ref due, 5f) && due == 15f, "due advances by one interval");
        due = 10f;
        check(TimelineLayout.SlideDue(30f, ref due, 5f) && due == 35f, "a missed stretch restarts from now");
        check(!TimelineLayout.SlideDue(30f, ref due, 5f), "no second advance in the same moment");

        // Card size: a fake measure where text needs (chars * size * 0.6 / width) lines of size * 1.2.
        Func<int, Func<float, float, float>> measureOf = chars =>
            (width, size) => (float)Math.Ceiling(chars * size * 0.6f / width) * size * 1.2f;

        TimelineLayout.CardFit shortFit = TimelineLayout.FitCard(measureOf(40), 232f, 352f, 118f, 14f, 11f, 0.5f);
        check(!shortFit.wide && shortFit.fits && shortFit.fontSize == 14f, "short text is narrow");

        TimelineLayout.CardFit mediumFit = TimelineLayout.FitCard(measureOf(200), 232f, 352f, 118f, 14f, 11f, 0.5f);
        check(mediumFit.wide && mediumFit.fits && mediumFit.fontSize == 14f, "medium text is wide at full size");

        TimelineLayout.CardFit longFit = TimelineLayout.FitCard(measureOf(320), 232f, 352f, 118f, 14f, 11f, 0.5f);
        check(longFit.wide && longFit.fits && longFit.fontSize < 14f && longFit.fontSize >= 11f,
            "long text is wide at a reduced size, got " + longFit.fontSize);

        TimelineLayout.CardFit hugeFit = TimelineLayout.FitCard(measureOf(5000), 232f, 352f, 118f, 14f, 11f, 0.5f);
        check(hugeFit.wide && !hugeFit.fits && hugeFit.fontSize == 11f, "oversized text is reported as not fitting");

        // Slide indicators on a narrow card's inner width (260 - 2 x 14). A 30-image gallery
        // must stay inside the card; a small one keeps the default 6 + 6 geometry, right-aligned.
        // Tolerance in pixels, for float rounding only: a thousandth of a pixel is never visible, while a
        // real overflow is whole pixels.
        const float inner = 232f;
        const float pixelTolerance = 0.001f;
        TimelineLayout.DotStrip many = TimelineLayout.FitDots(30, inner, 6f, 6f);
        float manyEnd = many.firstX + 29 * many.pitch + many.size;
        check(many.firstX >= 0f && manyEnd <= inner + pixelTolerance && many.size > 0f && many.pitch > many.size,
            $"30 dots inside [0,{inner}]: first {many.firstX}, end {manyEnd}, size {many.size}");
        check(Math.Abs(manyEnd - inner) < pixelTolerance, "30 dots end at the right edge");

        TimelineLayout.DotStrip few = TimelineLayout.FitDots(3, inner, 6f, 6f);
        check(few.size == 6f && few.pitch == 12f && Math.Abs(few.firstX - (inner - 30f)) < pixelTolerance,
            "3 dots keep the default size and spacing, right-aligned");
        check(TimelineLayout.FitDots(1, inner, 6f, 6f).size == 6f, "a single dot is kept at full size");
        check(TimelineLayout.FitDots(0, inner, 6f, 6f).size == 0f, "no gallery, no dots");

        // Bounded indicator: dots only for 2..max images; past max a counter; never both, never for 0 or 1.
        check(TimelineLayout.IndicatorDots(0, 5) == 0 && TimelineLayout.IndicatorDots(1, 5) == 0
            && !TimelineLayout.IndicatorCounter(1, 5), "0 or 1 image: no indicator");
        check(TimelineLayout.IndicatorDots(2, 5) == 2 && TimelineLayout.IndicatorDots(5, 5) == 5
            && !TimelineLayout.IndicatorCounter(5, 5), "up to max: one dot per image");
        check(TimelineLayout.IndicatorDots(6, 5) == 0 && TimelineLayout.IndicatorCounter(6, 5)
            && TimelineLayout.IndicatorDots(100, 5) == 0 && TimelineLayout.IndicatorCounter(100, 5), "past max: counter, no dots");

        // Decade width: a strip when empty, columns plus padding otherwise.
        check(TimelineLayout.DecadeWidth(new List<float>(), 50f, 20f, 18f, 18f) == 50f, "empty decade is a strip");
        check(TimelineLayout.DecadeWidth(new List<float> { 260f, 380f }, 50f, 20f, 18f, 18f) == 696f, "two-column decade width");

        return "TimelineLayout self-check PASS (" + checks + " checks)";
    }

    private static TimelineItemVO Item(int id, int categoryId, int year, params string[] urls)
    {
        TimelineItemVO item = new TimelineItemVO { id = id, categoryId = categoryId, year = year };
        item.images = new MediaVO[urls.Length];
        for (int i = 0; i < urls.Length; i++)
            item.images[i] = new MediaVO { url = urls[i] };
        return item;
    }
}
#endif
