using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public class FileDownloader
    {
        [System.Serializable]
        public class FileDownloadEvents : UnityEvent<string> { }

        // No [Header] here: this is a plain class, not a MonoBehaviour, so the attribute drew
        // nothing anywhere and only suggested an inspector that does not exist.
        public FileDownloadEvents onFileComplete = new FileDownloadEvents();
        public FileDownloadEvents onFileError = new FileDownloadEvents();

        /// <summary>
        /// Downloads <paramref name="file"/> straight to its <c>SavePath</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>The destination is checked before the request, not at write time.</b>
        /// <c>DownloadVO.SavePath</c> is <c>DestinationPath + DestinationName</c> - bare
        /// concatenation - so a name carrying <c>..\</c> resolves outside the folder the caller
        /// chose. These values come from a CMS or a caller list, which is the same reasoning
        /// applied to the asset root: contain the path where it is <i>used</i>. The check has to run
        /// first because the response is streamed to that path, so by write time it is too late.</para>
        ///
        /// <para><b>Streamed to a <c>.part</c> file, then moved.</b> It used to hold the entire
        /// payload in <c>downloadHandler.data</c> and then <c>File.WriteAllBytes</c> it - two full
        /// copies of every file in memory at once, which on a large video is what a kiosk cannot
        /// spare. Streaming fixes that, but <c>DownloadHandlerFile</c> creates and <b>truncates</b>
        /// its target before the response arrives, so streaming straight to the destination means a
        /// 404 replaces good cached media with the error body. <c>WriteAllBytes</c> at least only
        /// touched disk on success. So: stream to <c>.part</c>, move over the destination only once
        /// the request succeeded, and delete it otherwise.</para>
        ///
        /// <para><b>The <c>using</c> does NOT cover every exit</b>, and an earlier version of this
        /// remark claimed it did. A <c>using</c> is <c>try</c>/<c>finally</c>, and Unity abandons a
        /// coroutine's iterator on <c>StopCoroutine</c>, <c>SetActive(false)</c> or destroy without
        /// running it - measured. What makes that survivable here is the
        /// <c>.part</c> name: an abandoned download leaves a stray <c>.part</c>, never a truncated
        /// or half-written real file.</para>
        /// </remarks>
        public IEnumerator DownloadFile(DownloadVO file)
        {
            if (file == null)
            {
                Development.LogError("DownloadFile was given a null DownloadVO; nothing was downloaded.");
                yield break;
            }

            string savePath;

            if (!TryResolveSavePath(file, out savePath))
            {
                onFileError.Invoke("Refused: " + file.FileName);
                yield break;
            }

            string partPath = savePath + ".part";
            bool downloaded = false;

            // The `while (!www.isDone)` that used to follow this was dead: yielding the
            // UnityWebRequestAsyncOperation already resumes only once the request is done.
            using (UnityWebRequest www = UnityWebRequest.Get(file.DownloadPath))
            {
                www.downloadHandler = new DownloadHandlerFile(partPath) { removeFileOnAbort = true };

                // Non-zero, deliberately: 0 means wait forever, and FileController chains its list
                // on onFileComplete - so one unreachable host would stall every remaining file with
                // nothing in the log. A local const rather than operator config, which is why this
                // does not go through WebRequestExtensions' named policies as the settings-driven sites do.
                www.timeout = TimeoutSeconds;

                // The response body still lands inside the contained path, so a redirect cannot
                // escape it - but an untrusted ServerPath should not be chasing 32 hops either.
                www.redirectLimit = 4;

                yield return www.SendWebRequest();

                downloaded = www.result == UnityWebRequest.Result.Success;

                if (!downloaded)
                    Development.LogError($"Download failed for [{file.DownloadPath}]: {www.error}");
            }

            if (!downloaded)
            {
                TryDelete(partPath);
                onFileError.Invoke("Server Error: " + file.DownloadPath);
                yield break;
            }

            if (!TryPromote(partPath, savePath, file.FileName))
            {
                onFileError.Invoke("Error saving: " + file.FileName);
                yield break;
            }

            onFileComplete.Invoke(file.FileName);
        }

        /// <summary>
        /// Obsolete. Renamed to <see cref="DownloadFile"/>.
        /// </summary>
        /// <remarks>
        /// Kept as a forwarder rather than deleted, following <c>StringUtil.ToClock</c>,
        /// <c>Timer.SetDuration</c> and <c>Development.Assert</c>: branched projects call this, and
        /// the warning at each of their call sites is the migration notice.
        /// </remarks>
        [Obsolete("Renamed to DownloadFile (L5). Same arguments, same behaviour. This forwarder is "
            + "removed in v0.55.")]
        public IEnumerator downloadFile(DownloadVO file)
        {
            return DownloadFile(file);
        }

        /// <summary>
        /// Seconds before an unanswered request is abandoned. Never 0 - see the note at the call.
        /// </summary>
        private const int TimeoutSeconds = 60;

        /// <summary>
        /// Moves the completed <c>.part</c> over its destination.
        /// </summary>
        private bool TryPromote(string partPath, string savePath, string fileName)
        {
            try
            {
                if (File.Exists(savePath))
                    File.Delete(savePath);

                File.Move(partPath, savePath);
                return true;
            }
            catch (Exception e)
            {
                Development.LogException(e);
                Development.LogError($"[{fileName}] downloaded but could not be moved into place; "
                    + "the previous copy, if any, is untouched.");
                TryDelete(partPath);
                return false;
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                // Not fatal - a stray .part costs disk, not correctness - but it should still say so.
                Development.LogException(e);
                Development.LogError($"Could not remove the partial download [{path}].");
            }
        }

        /// <summary>
        /// Resolves and contains <paramref name="file"/>'s save path, reporting if it escapes.
        /// </summary>
        private bool TryResolveSavePath(DownloadVO file, out string savePath)
        {
            savePath = null;

            if (string.IsNullOrEmpty(file.DestinationPath) || string.IsNullOrEmpty(file.DestinationName))
            {
                Development.LogError($"Download of [{file.FileName}] refused: it has no destination "
                    + "folder or no destination file name.");
                return false;
            }

            string root;
            string resolved;

            try
            {
                // GetFullPath throws on invalid characters, which a CMS-supplied name can easily
                // carry - so the refusal has to survive that rather than propagate out of a
                // coroutine nobody is catching.
                root = Path.GetFullPath(file.DestinationPath);

                // Path.Combine, not DownloadVO.SavePath: that property is
                // `DestinationPath + DestinationName`, so a DestinationPath without a trailing
                // separator produced "...\folderfile.png" - which this check would then refuse,
                // breaking a caller that used to work.
                resolved = Path.GetFullPath(Path.Combine(file.DestinationPath, file.DestinationName));
            }
            catch (Exception e)
            {
                Development.LogException(e);
                Development.LogError($"Download of [{file.FileName}] refused: its destination path "
                    + "could not be resolved.");
                return false;
            }

            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;

            // Case-INsensitive only where the filesystem is. On Android and Linux a root of
            // /data/media/ with a name of "../MEDIA/x" is a genuinely different directory, and
            // OrdinalIgnoreCase would have passed it.
            StringComparison pathComparison =
                Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

            if (!resolved.StartsWith(root, pathComparison))
            {
                Development.LogError($"Download of [{file.FileName}] REFUSED: its destination "
                    + $"resolves to [{resolved}], which is outside [{root}]. A destination name "
                    + "containing .. escapes the folder the caller chose.");
                return false;
            }

            try
            {
                string folder = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                // Broader than IOException, which was the only thing caught before: a read-only
                // location gives UnauthorizedAccessException and a malformed one ArgumentException,
                // and both used to escape this coroutine entirely.
                Development.LogException(e);
                Development.LogError($"Download of [{file.FileName}] refused: [{resolved}] could not "
                    + "be prepared for writing.");
                return false;
            }

            savePath = resolved;
            return true;
        }
    }
}
