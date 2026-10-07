/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual;
// VideoPlayerControllerBase is in EnigmaVirtual.Components.Video, not EnigmaVirtual - C# does not
// import child namespaces, so `using EnigmaVirtual;` alone is CS0246 here.
using EnigmaVirtual.Components.Video;
using UnityEngine.Events;

/// <summary>
/// The <see cref="MediaVO"/> overloads of the framework's media loaders, moved here so the framework
/// stops naming a project type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Global namespace on purpose.</b> Extension methods are only in scope where their namespace is, and
/// every file under <c>App/</c> declares no namespace - so putting this here means the existing call sites
/// keep compiling untouched. A branched project that namespaces its own code needs a <c>using</c> for
/// wherever it puts this file.
/// </para>
/// <para>
/// <b>Overload resolution is unchanged.</b> Instance methods are preferred over extension methods, so
/// <c>Load(someString)</c> still binds to the framework's own <c>Load(string)</c> exactly as before. The
/// one spelling that changes meaning is <c>Load(null)</c>, which used to be a compile error - ambiguous
/// between the <c>MediaVO</c> and <c>string</c> overloads - and now resolves to <c>string</c>. Nothing
/// could have been calling it, since it did not compile.
/// </para>
/// <para>
/// <b>The <c>.GetTranslation()</c> calls are gone, and that is not a behaviour change.</b> It was an
/// identity function - <c>=&gt; message</c> - so <c>media.url.GetTranslation()</c> only ever meant
/// <c>media.url</c>. The move here removed its last call site and v0.24.1 deleted it. Why the URL
/// is not translatable in the first place, and what making it so would cost, is on
/// <see cref="MediaVO"/>.
/// </para>
/// </remarks>
public static class MediaVOExtensions
{
    /// <summary>Prepares playback of a media item's video URL.</summary>
    public static void Prepare(this VideoPlayerControllerBase player, MediaVO value,
        bool playOnPrepared = false)
    {
        Development.CheckPoint();

        if (value == null)
        {
            Development.LogWarning("Null MediaVO!");
            return;
        }

        player.Prepare(value.url, playOnPrepared);
    }

    /// <summary>Plays a media item's video URL.</summary>
    public static void Play(this VideoPlayerControllerBase player, MediaVO value)
    {
        Development.CheckPoint();

        if (value == null)
        {
            Development.LogWarning("Null MediaVO!");
            return;
        }

        player.Play(value.url);
    }

    /// <summary>
    /// Loads a media item's image: the thumb when <paramref name="isThumb"/>, otherwise the full URL,
    /// falling back from thumb to full URL.
    /// </summary>
    /// <remarks>
    /// Which field to prefer is the only thing here that knows about <see cref="MediaVO"/>. The fallback
    /// rule, the failure reporting and the event bookkeeping stay in <see cref="DownloadableImage"/>, so
    /// they remain shared across every branched project instead of being copied into each one.
    /// </remarks>
    public static void Load(this DownloadableImage image, MediaVO media, bool isThumb = false,
        UnityAction onLoadComplete = null, UnityAction<string> onLoadFailed = null)
    {
        if (media == null)
        {
            Development.LogError("Image loading aborted: the MediaVO passed to Load is null.");
            return;
        }

        // Registered before the call, because DownloadableImage can complete synchronously - a cached
        // or local image resolves inside Load, so subscribing afterwards would miss its own event.
        if (onLoadComplete != null)
            image.onLoadCompleteEvent.AddListener(onLoadComplete);
        if (onLoadFailed != null)
            image.onLoadFailedEvent.AddListener(onLoadFailed);

        if (image.Load(isThumb ? media.thumb : media.url, isThumb ? media.url : null))
            return;

        // Refused before it started, so no event will ever be raised for this call and the handlers
        // just registered would sit on the events belonging to whatever load is still running - and
        // fire for *that* one. Take them back off. This is the caller half of it; the component
        // cannot solve it alone, because the events are per-instance and two callers are in play.
        if (onLoadComplete != null)
            image.onLoadCompleteEvent.RemoveListener(onLoadComplete);
        if (onLoadFailed != null)
            image.onLoadFailedEvent.RemoveListener(onLoadFailed);

        Development.LogError($"Image load for [{(isThumb ? media.thumb : media.url)}] was refused "
            + "before it started, so its callbacks were removed again and neither will fire.");
    }

    /// <summary>
    /// Loads a media item's texture into the cache, falling back from thumb to full URL.
    /// </summary>
    public static void Load(this TextureCacheLoader loader, MediaVO media, bool isThumb = false)
    {
        if (media == null)
        {
            Development.LogWarning("null (MediaVO) Texture loading aborted!");
            return;
        }

        loader.Load(isThumb ? media.thumb : media.url, isThumb ? media.url : null);
    }
}

