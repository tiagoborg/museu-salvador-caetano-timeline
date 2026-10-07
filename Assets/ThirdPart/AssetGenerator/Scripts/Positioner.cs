using UnityEngine;

namespace UIGenerator
{
    public class Positioner : MonoBehaviour
    {
        public bool Locked = false;
        public bool Rename = false;
        public AssetsVO assetData;
        public Vector2 designSize;

        private RectTransform _rect;
        private Vector2 _topLeft;
        private Vector2 _size;

        //void Start()
        //{
        //    //assetData = Core.Assets.data;
        //}

        public void SetPosition()
        {
            if (Locked) return;
            if (_rect == null) _rect = GetComponent<RectTransform>();
            SetDesignSize();
            _rect.sizeDelta = assetData.Size;
            _rect.anchoredPosition = new Vector2(_topLeft.x + (assetData.x + _size.x), _topLeft.y - (_size.y + assetData.y));
            if (Rename) gameObject.name = assetData.name;
        }

        public void SetPositionForget()
        {
            SetPosition();
            DestroyImmediate(this);
        }

        private void SetDesignSize()
        {
            _topLeft = new Vector2(-(designSize.x * _rect.anchorMin.x), designSize.y * (1 - _rect.anchorMin.y));
            _size = new Vector2(assetData.width * _rect.pivot.x, assetData.height * (1f - _rect.pivot.y));
        }
    }
}

