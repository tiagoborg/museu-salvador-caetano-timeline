using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// Paths into <c>Resources/</c>, named once instead of spelled at each call.
    /// </summary>
    /// <remarks>
    /// <para>A <c>Resources.Load</c> path is a string the compiler cannot check: rename or move the
    /// asset and every call site keeps compiling and starts returning null. Naming it once does not
    /// make it checkable, but it does mean a move is one edit rather than a grep, and that the sites
    /// cannot silently drift apart from each other - <c>"Textures/ImageDownloadError"</c> was
    /// written out four times across two files.</para>
    ///
    /// <para><b>Not a general constants bucket.</b> Only paths used by more than one caller belong
    /// here; a single-use literal is clearer at its call site than behind a name.</para>
    /// </remarks>
    public static class AppResources
    {
        /// <summary>
        /// Placeholder shown when an image cannot be downloaded.
        /// </summary>
        /// <remarks>
        /// Loaded by <c>DownloadableImage</c> and <c>TextureCacheManager</c>. Both report at
        /// <c>LogError</c> when it is missing rather than showing nothing, because a blank slot and
        /// a failed download look identical on screen.
        /// </remarks>
        public const string ImageDownloadErrorSprite = "Textures/ImageDownloadError";
    }
}
