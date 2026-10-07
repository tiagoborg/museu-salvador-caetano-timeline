using UnityEngine;
using System.Collections;

namespace UIGenerator
{
    [System.Serializable]
    public class UIGeneratorModel
    {
        public string Name;
        public string version;
        public UIItemsVO[] assets;

        public void SetData( string data )
        {
            JsonUtility.FromJsonOverwrite( data, this );
        }
    }
}

        