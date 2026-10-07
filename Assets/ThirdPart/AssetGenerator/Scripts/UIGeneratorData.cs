using UnityEngine;

namespace UIGenerator
{
    [CreateAssetMenu( fileName = "UIGeneratorData", menuName = "UIGenerator/Data" )]
    public class UIGeneratorData : ScriptableObject
    {
        public string objectName = "UIGeneratorData";
        public string Version;
        public string MainCanvas;
        public int DesignWidth;
        public int DesignHeight;
        public bool dataHasLoaded = false;
        public UIGeneratorModel psdData = new UIGeneratorModel();

        public void Load( string data )
        {
            Debug.Log( data );
            psdData = new UIGeneratorModel();
            psdData.SetData( data );
        }
    }
}
