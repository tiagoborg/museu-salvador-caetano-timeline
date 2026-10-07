
using EnigmaVirtual.Notifications;
using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual
{

    public class FileController : FileControllerBase {

        private List<DownloadVO> _list;
        private int _filesCount;

        void Start()
        {
            Development.Log("InitDownloader");
            init();
            //List<DownloadVO> tempList = new List<DownloadVO>();
            //tempList.Add(new DownloadVO("http://localhost/m9/", Application.streamingAssetsPath + "/download/", "1.png"));
            //tempList.Add(new DownloadVO("http://localhost/m9/", Application.streamingAssetsPath + "/download/", "2.png"));
            //tempList.Add(new DownloadVO("http://localhost/m9/", Application.streamingAssetsPath + "/download/", "3.png"));
            //downloadList(tempList);
        }

        public void DownloadFileList(List<DownloadVO> list)
        {
            _list = list;
            _filesCount = 0;
            onFileComplete.AddListener(OnFileCompleteForList);
            DownloadNext();
        }

        public void OnFileCompleteForList(string response)
        {
            _filesCount++;
            if (_filesCount==_list.Count)
            {
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success, "Downloader", "List completed");

                onListComplete.Invoke("All Files Downloaded");
                onFileComplete.RemoveListener(OnFileCompleteForList);
            }
            else
            {
                DownloadNext();
            }
        }

        private void DownloadNext()
        {
            dowloadFile(_list[_filesCount]);
        }
    }

}

