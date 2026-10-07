using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public enum FitType
    {
        Width,
        Height,
        Both,
        Fill
    }

    [ExecuteInEditMode]
    [RequireComponent(typeof(MaskableGraphic))]
    public class ImageResizer : MonoBehaviour
    {
        public FitType FitType = FitType.Both;

        [Tooltip("Forces the anchors to center image inside parent")]
        public bool ForceCenterAnchor = false;
        [Tooltip("Create Mask and Image Component on parent - if it doesn't already have one")]
        public bool UseMask = false;
        [Tooltip("Auto execute Resize on texture changes")]
        public bool AutoUpdateSize = false;

        [SerializeField] private bool resizeOnStart = false;

        private RectTransform _rect;
        private RectTransform _parent;
        private MaskableGraphic _graphic;

        [SerializeField] private Image _image;
        [SerializeField] private RawImage _rawImage;

        private bool _initiated;
        private int _textID;
        private MaskableGraphic parentMaskableGraphic;
        private Mask parentMask;

        private void Awake()
        {
            Init();
        }

        void Start()
        {
            if (resizeOnStart && ((_image && _image.sprite) || (_rawImage && _rawImage.texture)))
                DoResize();
        }

        private void Update()
        {
            if (!Application.isPlaying && _initiated && _graphic != null && _parent != null)
                _graphic.GetComponent<RectTransform>().sizeDelta = _parent.sizeDelta;
        }

        void FixedUpdate()
        {
            if (AutoUpdateSize && _initiated && _graphic != null && _graphic.mainTexture != null && !_graphic.mainTexture.GetHashCode().Equals(_textID))
            {
                _textID = _graphic.mainTexture.GetHashCode();
                DoResize();
            }
        }

        private void Init()
        {
            if (_initiated) return;

            _rect = GetComponent<RectTransform>();

            if (transform.parent == null)
            {
                Debug.LogWarning("ImageResizer: GameObject must have a parent. Init aborted.", gameObject);
                return;
            }

            _parent = transform.parent.GetComponent<RectTransform>();
            _graphic = GetComponent<MaskableGraphic>();

            if (_graphic != null && _graphic.mainTexture != null)
            {
                _textID = _graphic.mainTexture.GetHashCode();
                _image = _graphic as Image;
                _rawImage = _graphic as RawImage;
            }
            parentMaskableGraphic = _parent.GetComponent<MaskableGraphic>();
            parentMask = _parent.GetComponent<Mask>();

            _initiated = true;
        }

        [ContextMenu("Force Resize")]
        public void ForceResize()
        {
            DoResize();
        }

        public void DoResize()
        {
            Init();

            if (UseMask)
            {
                if (!parentMaskableGraphic)
                    transform.parent.gameObject.AddComponent<Image>();
                if (!parentMask)
                {
                    parentMask = transform.parent.gameObject.AddComponent<Mask>();
                    parentMask.showMaskGraphic = false;
                }
            }
            else
            {
                // Application.isPlaying, not #if UNITY_EDITOR. The problem being solved is real -
                // this class is [ExecuteInEditMode], and Destroy does nothing useful in edit mode, which
                // is the editor error the #if was added for. But UNITY_EDITOR is defined in *play* mode
                // too, so the compile-time form sent editor play mode down the DestroyImmediate branch
                // while a player took Destroy: a behaviour divergence on the path that gets tested most,
                // and DestroyImmediate on a Mask/MaskableGraphic forces a synchronous canvas rebuild.
                //
                // The predicate wanted is "am I running", which is a runtime question. Edit mode still
                // gets DestroyImmediate; play mode now matches the player.
                if (Application.isPlaying)
                {
                    if (parentMaskableGraphic)
                        Destroy(parentMaskableGraphic);
                    if (parentMask)
                        Destroy(parentMask);
                }
                else
                {
                    if (parentMaskableGraphic)
                        DestroyImmediate(parentMaskableGraphic);
                    if (parentMask)
                        DestroyImmediate(parentMask);
                }
            }

            if (_image && _image.sprite != null)
                StartCoroutine(DoResizeImage());
            else if (_rawImage)
                DoResizeRawImage();
        }

        private IEnumerator DoResizeImage()
        {
            yield return new WaitForEndOfFrame();

            var parentSize = new Vector2();
            parentSize.x = _parent.rect.width;
            parentSize.y = _parent.rect.height;

            if (_image.sprite == null || _image.sprite.texture == null)
            {
                Debug.LogWarning("ImageResizer: Image sprite or texture is null. Resize aborted.", gameObject);
                yield break;
            }

            var imageSize = new Vector2();
            imageSize.x = _image.sprite.texture.width;
            imageSize.y = _image.sprite.texture.height;

            if (imageSize.x == 0 || imageSize.y == 0)
            {
                Debug.LogWarning("ImageResizer: Image dimensions are zero. Using fallback 1:1 ratio.", gameObject);
                _rect.sizeDelta = new Vector2(1, 1);
                yield break;
            }

            var nSize = imageSize;
            float ratio = 0;

            switch (FitType)
            {
                case FitType.Width:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    break;
                case FitType.Height:
                    ratio = imageSize.x / imageSize.y;
                    nSize.y = parentSize.y;
                    nSize.x = ratio * nSize.y;
                    break;
                case FitType.Both:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    if (nSize.y < parentSize.y)
                    {
                        ratio = imageSize.x / imageSize.y;
                        nSize.y = parentSize.y;
                        nSize.x = ratio * nSize.y;
                    }
                    break;
                case FitType.Fill:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    if (nSize.y < parentSize.y)
                    {
                        ratio = imageSize.x / imageSize.y;
                        nSize.y = parentSize.y;
                        nSize.x = ratio * nSize.y;
                    }
                    break;
            }
            _rect.sizeDelta = nSize;
            if (ForceCenterAnchor)
            {
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
            }
        }
        private void DoResizeRawImage()
        {
            if (!_rawImage.mainTexture) return;

            var parentSize = new Vector2();
            parentSize.x = _parent.rect.width;
            parentSize.y = _parent.rect.height;

            var imageSize = new Vector2();
            imageSize.x = _rawImage.mainTexture.width;
            imageSize.y = _rawImage.mainTexture.height;

            if (imageSize.x == 0 || imageSize.y == 0)
            {
                Debug.LogWarning("ImageResizer: RawImage dimensions are zero. Using fallback 1:1 ratio.", gameObject);
                _rect.sizeDelta = new Vector2(1, 1);
                return;
            }

            var nSize = imageSize;
            float ratio = 0;

            switch (FitType)
            {
                case FitType.Width:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    break;
                case FitType.Height:
                    ratio = imageSize.x / imageSize.y;
                    nSize.y = parentSize.y;
                    nSize.x = ratio * nSize.y;
                    break;
                case FitType.Both:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    if (nSize.y < parentSize.y)
                    {
                        ratio = imageSize.x / imageSize.y;
                        nSize.y = parentSize.y;
                        nSize.x = ratio * nSize.y;
                    }
                    break;
                case FitType.Fill:
                    ratio = imageSize.y / imageSize.x;
                    nSize.x = parentSize.x;
                    nSize.y = ratio * nSize.x;
                    if (nSize.y < parentSize.y)
                    {
                        ratio = imageSize.x / imageSize.y;
                        nSize.y = parentSize.y;
                        nSize.x = ratio * nSize.y;
                    }
                    break;
            }
            _rect.sizeDelta = nSize;
            if (ForceCenterAnchor)
            {
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
            }
        }
    }
}
