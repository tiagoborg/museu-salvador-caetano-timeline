using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EnigmaVirtual
{
    public class DataInitializer : MonoBehaviour
    {

        private Dictionary<string, FileInfo> _files = new Dictionary<string, FileInfo>();
        private Dictionary<string, object> _loadedAssets = new Dictionary<string, object>();

        void Awake()
        {
            string myPath = "Assets/Resources/";
            DirectoryInfo dirInfo = new DirectoryInfo(myPath);

            LoadFilesList(dirInfo);

            StartCoroutine(LoadAllFiles());

        }

        IEnumerator LoadAllFiles()
        {

            foreach (string key in _files.Keys)
            {
                ResourceRequest request = Resources.LoadAsync(_files[key].FullName);
                while (!request.isDone)
                {
                    yield return null;
                }

                _loadedAssets[key] = request.asset;
            }

            yield return true;
        }

        DirInfo LoadFilesList(DirectoryInfo dirInfo)
        {
            DirInfo dir = new DirInfo();
            FileInfo[] files = dirInfo.GetFiles();

            foreach (FileInfo t in files)
            {
                if (!t.Extension.Equals(".meta"))
                {
                    FileInfo file = t;
                    dir.files.Add(file);
                    _files[file.Name] = file;
                }
            }

            DirectoryInfo[] dirs = dirInfo.GetDirectories();

            foreach (DirectoryInfo t in dirs)
                dir.subfolders.Add(LoadFilesList(t));

            return dir;
        }

        // A commented CheckProgress() coroutine lived here: a busy-wait polling
        // ResourceRequest.progress in a `while (!loaded)` with no yield inside the inner loop,
        // which would have hung the frame rather than waited on it. Superseded, not lost work.

        class DirInfo
        {
            public List<DirInfo> subfolders;
            public List<FileInfo> files;

            public DirInfo()
            {
                subfolders = new List<DirInfo>();
                files = new List<FileInfo>();
            }
        }
    }
   
}
