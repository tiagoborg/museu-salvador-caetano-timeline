using UnityEngine;
using System.Collections;

using UnityEngine.SceneManagement;

namespace EnigmaVirtual
{

    public class Factory : MonoBehaviour
    {
        private static Hashtable _goList = new Hashtable();

        public static GameObject Get(string goName)
        {
            if (_goList[goName] != null) return _goList[goName] as GameObject;
	        return null;
        }		

		public static GameObject Get(string goName,GameObject goParent)
        {
            if (_goList[goName] != null) return _goList[goName] as GameObject;
            GameObject go = Instantiate(Resources.Load(goName), Vector3.zero, Quaternion.identity) as GameObject;
            go.name = goName;
            _goList[goName] = go;
            go.transform.SetParent(goParent.transform);
            return go;
        }

        public static GameObject Get(string folderPath, string goName)
        {
            if (_goList[goName] != null) return _goList[goName] as GameObject;
            GameObject go = Instantiate(Resources.Load(folderPath + "/" + goName), Vector3.zero, Quaternion.identity) as GameObject;
            go.name = goName;
            _goList[goName] = go;
            return go;
        }

        public static GameObject Get(string folderPath,string goName, GameObject goParent)
        {
            if (_goList[goName] != null) return _goList[goName] as GameObject;
            GameObject go = Instantiate(Resources.Load(folderPath + "/" + goName), Vector3.zero, Quaternion.identity) as GameObject;
            go.name = goName;
            _goList[goName] = go;
            go.transform.SetParent(goParent.transform);
            return go;
        }

        public static void Destroy(string goName)
        {
            if (_goList[goName] == null) return;
            GameObject go = _goList[goName] as GameObject;
            Destroy(go);
            _goList[goName] = null;
        }
    }
}