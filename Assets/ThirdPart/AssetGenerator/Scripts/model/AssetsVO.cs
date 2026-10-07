using UnityEngine;

namespace UIGenerator
{
    [System.Serializable]
    public class AssetsVO
    {
		public string version;
		public string id;
        public string name;
        public float x;
        public float y;
        public float width;
        public float height;
        public float centerX;
        public float centerY;
        public string type;
        public bool hasSprite;

        private Vector2 _designSize;

        public void SetDesignSize( Vector2 size )
        {
            _designSize = size;
        }

        public Vector2 Position
        {
            get { return new Vector2(  x, y ); }
        }


        public Vector2 Center
        {
            get { return new Vector2( centerX, centerY ); }
        }

        public Vector2 Size
        {
            get { return new Vector2( width, height ); }
        }

        public Vector2 DesignSize
        {
            get { return _designSize; }
        }

        public string GetName
        {
            get { return name.Split( "."[0])[0]; }
        }
    }

}
