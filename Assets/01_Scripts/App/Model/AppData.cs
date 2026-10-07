using EnigmaVirtual;
using System;
using System.Linq;

// Content models are public FIELDS, never auto-properties.
//
// Not because of this file - AppData is read by Newtonsoft only, which sees both kinds, and no
// serialized field holds any of these types, so neither the JsonUtility path nor the Inspector
// applies here. The reason is that this file is the template every project is branched from, and a
// field is the only member kind all three serializers in play can see:
//
//   Newtonsoft  (appData.json)     fields and properties
//   JsonUtility (appSettings.json) FIELDS ONLY - and it ROUND-TRIPS: AppDataController.SaveAppSettings
//                                  writes with JsonUtility.ToJson, so a property on a settings DTO is
//                                  not merely unread, it is ERASED on the next save
//   Unity's serializer             fields only
//
// So copying a property from here into a settings model silently destroys data. MediaWalker reads
// both member kinds, so nothing forces the choice at the point of use - which is exactly why the
// template has to make it consistently rather than leave it to whoever copies it next.
//
// Note the Inspector is NOT a reason: LocalizedField<T> derives from Dictionary and Unity cannot
// serialize that, so a MediaVO in a serialized field would show id/thumb/url and silently omit
// title/description/keywords - a worse lie than showing nothing at all.

[System.Serializable]
public class AppData : AppDataBase
{
    public CategoryVO[] categories;
    public TimelineItemVO[] timeline;

    public TimelineItemVO[] GetTimelineItemsByCategoryId(int categoryId)
    {
        if (timeline == null || timeline.Length == 0)
            return null;
        return timeline.Where(item => item.categoryId == categoryId).ToArray();
    }

    public TimelineItemVO[] GetTimelineItemsByYearInterval(TimelineItemVO[] timelineItems, int minYear, int maxYear)
    {
        if (timelineItems == null || timelineItems.Length == 0)
            return null;
        return timelineItems.Where(item => item.year >= minYear && item.year <= maxYear).ToArray();
    }

    /// <summary>
    /// The loaded content model, typed. Use this from App code instead of <c>Core.main.AppData</c>:
    /// that property is declared as <c>AppDataBase</c> so the framework assembly does not depend on
    /// App, which means the members declared above are invisible through it.
    ///
    /// Returns null before the boot chain has loaded content, which is normal - anything reading this
    /// from <c>Awake</c> or <c>Start</c> may be too early. It is null rather than throwing so a caller
    /// can poll or guard, and it reports the one misconfiguration that looks identical to "not loaded
    /// yet" but never resolves.
    /// </summary>
    public static AppData Current
    {
        get
        {
            AppDataBase loaded = Core.main != null ? Core.main.AppData : null;

            if (loaded == null)
                return null;

            if (loaded is AppData typed)
                return typed;

            // Deserialized into the base type, so none of the members above were populated and nothing
            // failed to compile - MediaWalker is reflective and every consumer takes object. The cause
            // is always the same: MainController.SetupAppData looks for a component overriding
            // AppDataType, and falls back to a plain AppDataController when the scene has none.
            Development.LogError("AppData.Current: content deserialized into AppDataBase, so menus, "
                + "ctaMedia and items are all empty. ProjectAppDataController is not authored on a "
                + "GameObject under Core in the active scene - add it there. Nothing else reports this.");

            return null;
        }
    }
}

[System.Serializable]
public class CategoryVO
{
    public int id;
    public LocalizedField<string> name;
}

[System.Serializable]
public class TimelineItemVO : ItemBaseVO
{
    public int id;
    public int categoryId;
    public int year;
    public LocalizedField<string> content;
    public MediaVO[] images;
}

[System.Serializable]
public class MenuVO : ItemBaseVO
{
    public int interactable;
    public MediaVO media;
    public MediaLibraryVO[] mediaLibraries;
    public string primaryColor;
    public string secondaryColor;
}

[System.Serializable]
public class MediaLibraryVO : ItemBaseVO
{
    public MediaVO media;
    public MediaVO[] medias;
    public LocalizedField<string> description;
}

[System.Serializable]
public class ItemBaseVO
{
    public int id;
    public LocalizedField<string> title;
}

/// <summary>
/// A media item. <b><c>thumb</c> and <c>url</c> ship as plain strings, so per-language media does not
/// work out of the box - but this is a project file, and making them translatable is a per-project
/// decision</b>. This class is here to be edited.
/// </summary>
/// <remarks>
/// Note the asymmetry before deciding: <c>title</c>, <c>description</c> and <c>keywords</c> are
/// <c>LocalizedField&lt;string&gt;</c> while the two URL fields are not.
/// Do not reintroduce a <c>string.GetTranslation()</c> identity extension over these call sites:
/// <c>Load(media.url.GetTranslation())</c> reads as though it resolves a language and cannot.
/// <para>
/// <b>To make media per-language:</b> change these two fields to <c>LocalizedField&lt;string&gt;</c>
/// here, then fix the four methods in <see cref="MediaVOExtensions"/> that pass them into
/// <c>string</c> parameters. Nothing in the framework needs to change - those overloads live
/// under <c>App/</c>, which is what makes this a project decision rather than a framework one.
/// </para>
/// <para>
/// Two traps on that path. It is a <b>content-format change</b>: <c>appData.json</c> must then
/// supply an object per field (<c>"url": { "pt": "a.mp4", "en": "b.mp4" }</c>) rather than a bare
/// string, so every deployed payload and the API that produces it change with it. And the obvious
/// <c>GetLocalized()</c> at those call sites returns <b><c>null</c></b> when the language is
/// missing, not <c>string.Empty</c> - which then arrives as the <c>url</c> argument of
/// <c>Load(url, fallbackUrl)</c>. Decide what a missing language should do before wiring it.
/// </para>
/// </remarks>
[System.Serializable]
public class MediaVO : ItemBaseVO
{
    public LocalizedField<string> description;
    public LocalizedField<string> keywords;
    public string thumb;
    public string url;
}

[System.Serializable]
public class GalleryVO : ItemBaseVO
{
    public MediaVO[] medias;
    public LocalizedField<MediaVO> features;

    /// <summary>
    /// Returns the medias whose localized keywords include any of <paramref name="searchKeywords"/>.
    /// Never throws on incomplete content - a media with no keywords simply doesn't match.
    /// No search terms means no results, not all results.
    /// </summary>
    internal MediaVO[] GetItems(string[] searchKeywords)
    {
        if (medias == null || medias.Length == 0)
            return Array.Empty<MediaVO>();

        if (searchKeywords == null || searchKeywords.Length == 0)
            return Array.Empty<MediaVO>();

        return medias
            .Where(media => HasMatchingKeyword(media, searchKeywords))
            .ToArray();
    }

    private static bool HasMatchingKeyword(MediaVO media, string[] searchKeywords)
    {
        // media and media.keywords both come straight from CMS JSON, so either can be absent.
        // The old code guarded the *result* of GetLocalized but not the object it was called on,
        // so one record without a keywords block threw inside the Where and took the whole
        // gallery with it.
        if (media?.keywords == null)
            return false;

        string localized = media.keywords.GetLocalized();
        if (string.IsNullOrEmpty(localized))
            return false;

        // RemoveEmptyEntries: a double space would otherwise yield "" and match an empty search term.
        foreach (string key in localized.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            for (int i = 0; i < searchKeywords.Length; i++)
            {
                if (string.Equals(key, searchKeywords[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static readonly char[] KeywordSeparators = { ' ' };
}
