using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public static class MediaFile
    {
        /// <summary>
        /// Image extensions Unity can actually decode at runtime, via
        /// <c>UnityWebRequestTexture</c>/<c>DownloadHandlerTexture</c> - the only image decoder
        /// this framework uses (<c>Texture2D.LoadImage</c> has zero call sites here).
        /// </summary>
        /// <remarks>
        /// <para><b><c>.webp</c> is absent deliberately - do not add it.</b>
        /// The register carried its absence as a defect from v0.4.0, but Unity decodes neither:
        /// measured in this project's editor, <c>DownloadHandlerTexture.GetContent</c> returns
        /// <b>null</b> with <c>result = DataProcessingError</c> on a real webp, and
        /// <c>Texture2D.LoadImage</c> returns false. So this set is not a preference, it is the
        /// decoder's capability, and supporting webp means shipping a decoder rather than editing
        /// this line.</para>
        /// <para>What admitting it would actually cost: <b>a per-image download and a per-image
        /// error</b> in place of one aggregate rejection. It would <i>not</i> fail silently - every
        /// consumer logs on the null (<c>TextureCacheManager</c> and <c>DownloadableImage</c> both
        /// <c>LogError</c>, always compiled). It does not
        /// produce a blank texture; that describes <c>Texture2D.LoadImage</c>, which nothing here
        /// calls.</para>
        /// <para>Kept in step with <c>TextureCacheManager</c>'s own copy, which is separate on
        /// purpose: that one guards null/empty first, and this one does not (see the remarks on
        /// the methods below).</para>
        /// </remarks>
        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

        private static readonly HashSet<string> AudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".ogg" };

        /// <summary>
        /// Validates if image filetype is playable.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="ArgumentException"/> on a null or empty <paramref name="url"/>, because
        /// <c>GetFileType</c> does - it does <b>not</b> return <c>false</c>. Check the string before
        /// asking. Left as-is rather than guarded: the throw is long-standing framework behaviour
        /// and callers in branched projects may rely on it.
        /// </remarks>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool isValidImageFileType(string url)
        {
            return ImageExtensions.Contains(url.GetFileType());
        }

        /// <summary>
        /// Validates if audio filetype is playable.
        /// </summary>
        /// <remarks>Throws on null or empty, as <see cref="isValidImageFileType"/> does.</remarks>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool isValidAudioFileType(string url)
        {
            return AudioExtensions.Contains(url.GetFileType());
        }

        /// <summary>
        /// Video extensions Unity's <c>VideoPlayer</c> accepts.
        /// </summary>
        /// <remarks>
        /// Note <c>.webm</c> is here while <c>.webp</c> is not in <see cref="ImageExtensions"/>,
        /// and that is not an inconsistency: <c>VideoPlayer</c> decodes VP8/VP9 in a container it
        /// supports, whereas <c>Texture2D.LoadImage</c> decodes only PNG and JPG.
        /// </remarks>
        private static readonly HashSet<string> VideoExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ogg", ".mp4", ".webm" };

        /// <summary>
        /// Validates if video filetype is playable.
        /// </summary>
        /// <remarks>
        /// Throws on null or empty, as <see cref="isValidImageFileType"/> does. <b>Pass the url,
        /// not its extension.</b> Handing it <c>url.GetFileType()</c> applies the extraction twice,
        /// which survives for <c>"a.mp4"</c> only because <c>Path.GetExtension(".mp4")</c> returns
        /// <c>".mp4"</c>; an extension-less url (a live stream) extracts to <c>""</c> and the second
        /// application throws. <c>VideoPlayerControllerBase</c> did exactly that and skipped its own
        /// error handling as a result - fixed in v0.49.4. It is broken rather than merely fragile,
        /// as measuring the empty case shows.
        /// </remarks>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool isValidVideoFileType(string url)
        {
            return VideoExtensions.Contains(url.GetFileType());
        }

        public static IEnumerator LoadAudio(string url, Action<AudioClip> callback)
        {
            // Same as the image loaders. No caller in this repo, but it is framework surface a
            // branched project can reach, and it would have failed on Android in exactly the same way.
            url = AppPaths.ToFileUrl(Core.settings.AppData.GetAssetPath(url));

            AudioType audioType = GetAudioTypeFromUrl(url);

            if (audioType == AudioType.UNKNOWN)
            {
                Debug.LogError("Unsupported or unknown audio format from file [" + url + "].");
                yield break;
            }

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Development.LogError("Audio [" + url + "] download failed: " + www.error);
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    callback?.Invoke(clip);
                    Development.Log("Audio [" + url + "] ready!");
                }
            }
        }

        private static AudioType GetAudioTypeFromUrl(string url)
        {
            if (url.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase))
                return AudioType.MPEG;
            else if (url.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                return AudioType.WAV;
            else if (url.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase))
                return AudioType.OGGVORBIS;
            else
                return AudioType.UNKNOWN;
        }
    }
}
