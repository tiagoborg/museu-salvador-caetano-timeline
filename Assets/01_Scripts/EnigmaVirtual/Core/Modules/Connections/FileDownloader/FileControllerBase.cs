using EnigmaVirtual.Notifications;
using UnityEngine;

using UnityEngine.Events;

namespace EnigmaVirtual
{
    public class FileControllerBase : MonoBehaviour {

        [System.Serializable]
        public class FileDownloadEvents : UnityEvent<string> { }

        [Header("Events")]
        public FileDownloadEvents onFileComplete = new FileDownloadEvents();
        public FileDownloadEvents onListComplete = new FileDownloadEvents();
        public FileDownloadEvents onFileError = new FileDownloadEvents();

        private FileDownloader _fileDownloader = new FileDownloader();

        public void init()
        {
            Development.Log("Init!");
            _fileDownloader.onFileComplete.AddListener(OnFileComplete);
            _fileDownloader.onFileError.AddListener(OnFileError);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="response"></param>
        private void OnFileComplete(string response)
        {
            Development.Log(response);
            onFileComplete.Invoke(response);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="response"></param>
        private void OnFileError(string response)
        {
            onFileError.Invoke(response);

            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "Downloader Error", response);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileItem"></param>
        public void dowloadFile(DownloadVO fileItem)
        {
            StartCoroutine(_fileDownloader.DownloadFile(fileItem));
        }
    }

}
