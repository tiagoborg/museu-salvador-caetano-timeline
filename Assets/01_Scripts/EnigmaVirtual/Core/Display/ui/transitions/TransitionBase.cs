
using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public enum TransitionType
    {
        SlideInRight,
        SlideInLeft,
        SlideInBottom,
        SlideOutBottom,
        SlideInTop,
        SmoothIn,
        SmoothOut,
        FadeIn,
        FadeOut,
        SlideOutLeft
    }

    [RequireComponent(typeof(MaskableGraphic))]
    public class TransitionBase : MonoBehaviour
    {
        public TransitionType TransitionIn;
        [HideInInspector]
        public float Padding = 25f;
        public float Time = .8f;
        public float Delay = 0f;
        public LeanTweenType TransitionEase;

        [HideInInspector]
        public float scaleStart = 0;
        [HideInInspector]
        public float scaleEnd = 1;
        [HideInInspector]
        public bool fadeToo = true;

        // private
        private RectTransform _rectTransform;
        private Vector2 _initialSize;
        private Vector2 _initialPosition;
        private Vector2 _initialPivot;
        private Vector2 _design;
        private MaskableGraphic _image;
        private float _initialAlpha;
        private TransitionType _currentTransition;
        protected bool _autoStart = false;
        protected bool _startPositioned = false;
        private bool _deactivate = false;

        private float ScreenX
        {
            get { return _design.x * _rectTransform.anchorMin.x; }
        }

        private float ScreenY
        {
            get { return _design.y * _rectTransform.anchorMin.y; }
        }

        private float SizeX
        {
            get { return _initialSize.y * _initialPivot.y; }
        }

        private float SizeY
        {
            get { return _initialSize.y * (1f - _initialPivot.y); }
        }


        /// <summary>
        /// Events
        /// </summary>
        public delegate void TransitionEnded();
        public delegate void TransitionStarted();

        public event TransitionEnded OnTransitionEnded;
        public event TransitionStarted OnTransitionStarted;


        public virtual void Awake()
        {
            CanvasScaler canvas = GetComponentInParent<CanvasScaler>();
            _rectTransform = GetComponent<RectTransform>();
            _initialSize = _rectTransform.sizeDelta;
            _initialPosition = _rectTransform.anchoredPosition;
            _initialPivot = _rectTransform.pivot;
            _design = canvas.referenceResolution;
            _currentTransition = TransitionIn;
            _image = GetComponent<MaskableGraphic>();
        }

        public virtual void Start()
        {
            Play(_startPositioned, _autoStart);
        }

        public void Play()
        {
            Play(true, true);
        }

        public void PlayAndDeactivate()
        {
            _deactivate = true;
            Play(true, true);
        }


        public void Play(bool position, bool autoPlay)
        {
            switch (_currentTransition)
            {
                case TransitionType.SlideInLeft:
                    SlideInLeft(position, autoPlay);
                    break;
                case TransitionType.SlideOutLeft:
                    SlideOutLeft(position, autoPlay);
                    break;
                case TransitionType.SlideInRight:
                    SlideInRight(position, autoPlay);
                    break;
                case TransitionType.SlideInBottom:
                    SlideInBottom(position, autoPlay);
                    break;
                case TransitionType.SlideOutBottom:
                    SlideOutBottom(position, autoPlay);
                    break;
                case TransitionType.SlideInTop:
                    SlideInTop(position, autoPlay);
                    break;
                case TransitionType.SmoothIn:
                    SmoothIn(position, autoPlay);
                    break;
                case TransitionType.SmoothOut:
                    SmoothOut(autoPlay);
                    break;
                case TransitionType.FadeIn:
                    FadeIn(position ? 0f : _image.color.a, autoPlay);
                    break;
                case TransitionType.FadeOut:
                    FadeOut(autoPlay);
                    break;
                default:
                    break;
            }
        }

        public void SlideInRight()
        {
            SlideInRight(true, true);
        }

        public void SlideInLeft()
        {
            SlideInLeft(true, true);
        }

        public void SlideInTop()
        {
            SlideInTop(true, true);
        }

        public void SlideInBottom()
        {
            SlideInBottom(true, true);
        }

        public void SmoothIn()
        {
            SmoothIn(true, true);
        }

        public void SmoothOut()
        {
            SmoothOut(true);
        }

        /// <summary>
        /// Slides the recttransform in from the Right
        /// </summary>
        public void SlideInRight(bool position, bool play)
        {
            if (position)
            {
                _rectTransform.anchoredPosition = new Vector2(ScreenX + SizeX + Padding, _initialPosition.y);
            }
            if (!play) return;

            if (_image is RawImage || _image is Text)
            {

                LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear);
            LeanTween.move(_rectTransform, _initialPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the recttransform in from the Right
        /// </summary>
        public void SlideInLeft(bool position, bool play)
        {
            if (position)
            {
                _rectTransform.anchoredPosition =
                    new Vector2(0f - (ScreenX + SizeX + Padding), _initialPosition.y);
            }
            if (!play) return;

            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear);
            LeanTween.move(_rectTransform, _initialPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the rectTransform in from the Right
        /// </summary>
        public void SlideOutLeft(bool position, bool play)
        {
            if (!play) return;
            Vector2 newPosition = new Vector2(_initialPosition.x - SizeX - Padding, _initialPosition.y);

            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 1f, 0f, Time).setDelay(Delay).setEase(TransitionEase).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 0f, Time).setDelay(Delay).setEase(TransitionEase);
            LeanTween.move(_rectTransform, newPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the recttransform in from the bottom
        /// </summary>
        public void SlideInBottom(bool position, bool play)
        {
            if (position)
            {
                _rectTransform.anchoredPosition = new Vector2(_initialPosition.x, -ScreenY - SizeY - Padding);
            }
            if (!play) return;
            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setDelay(Delay);
            LeanTween.move(_rectTransform, _initialPosition, Time).setDelay(Delay).setEase(TransitionEase).setDelay(Delay).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the recttransform in from the bottom
        /// </summary>
        public void SlideOutBottom(bool position, bool play)
        {
            Vector2 newPosition = new Vector2(_rectTransform.anchoredPosition.x,
                -ScreenY - SizeY - Padding);
            if (!play) return;


            if (_image is RawImage || _image is Text)
            {

                LeanTween.value(_image.gameObject, 1f, 0f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 0f, Time).setDelay(Delay).setEase(LeanTweenType.linear);
            LeanTween.move(_rectTransform, newPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the recttransform in from the top
        /// </summary>
        public void SlideInTop(bool position, bool play)
        {
            if (position)
            {
                _rectTransform.anchoredPosition = new Vector2(_initialPosition.x,
                    ScreenY + SizeY + Padding);
            }

            if (!play) return;
            if (_image is RawImage || _image is Text)
            {

                LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear);
            LeanTween.move(_rectTransform, _initialPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// Slides the recttransform 
        /// </summary>
        public void SlideOutTop(bool position, bool play)
        {
            Vector2 newPosition = new Vector2(_rectTransform.anchoredPosition.x,
                _design.y + SizeY + Padding);
            if (!play) return;
            if (_image is RawImage || _image is Text)
            {

                LeanTween.value(_image.gameObject, 1f, 0f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 0f, Time).setDelay(Delay).setEase(LeanTweenType.linear);
            LeanTween.move(_rectTransform, newPosition, Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// 
        /// </summary>
        public void SmoothIn(bool position, bool play)
        {
            //if (position)
            //{
            //    _rectTransform.localScale = new Vector2(0.0f, 0.0f);
            //}
            if (!play) return;
            if (fadeToo)
            {
                Alpha(0f);
                if (_image is RawImage || _image is Text)
                {
                    LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                    {
                        var c = _image.color;
                        c.a = val;
                        _image.color = c;
                    });
                }
                else
                    LeanTween.alpha(_rectTransform, 1f, Time);
            }
            _rectTransform.localScale = new Vector3(scaleStart, scaleStart, 1f);
            LeanTween.scale(_rectTransform, new Vector3(scaleEnd, scaleEnd, 1f), Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// 
        /// </summary>
        public void SmoothOut(bool play)
        {
            if (!play) return;
            if (fadeToo)
            {
                if (_image is RawImage || _image is Text)
                {
                    LeanTween.value(_image.gameObject, 1f, 0f, Time).setDelay(Delay).setEase(LeanTweenType.linear).setOnUpdate((float val) =>
                    {
                        var c = _image.color;
                        c.a = val;
                        _image.color = c;
                    });
                }
                else
                    LeanTween.alpha(_rectTransform, 0f, Time);
            }
            _rectTransform.localScale = new Vector3(scaleStart, scaleStart, 1f);
            LeanTween.scale(_rectTransform, new Vector3(scaleEnd, scaleEnd, 1f), Time).setDelay(Delay).setEase(TransitionEase).setOnComplete(Completed).setOnStart(Started);
        }

        /// <summary>
        /// 
        /// </summary>
        public void FadeOut(bool play)
        {
            if (!play) return;
            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 1f, 0f, Time).setDelay(Delay).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                }).setOnComplete(Deactivate);
            }
            else
                LeanTween.alpha(_rectTransform, 0f, Time).setDelay(Delay).setOnComplete(Deactivate);
        }

        private void Deactivate()
        {
            if (_deactivate)
            {
                _deactivate = false;
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void FadeOut(float time)
        {
            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 1f, 0f, time).setDelay(Delay).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 0f, time).setDelay(Delay);
        }

        /// <summary>
        /// 
        /// </summary>
        public void FadeIn(float starValue, bool play)
        {
            Alpha(starValue);
            if (!play) return;
            if (_image is RawImage || _image is Text)
            {
                LeanTween.value(_image.gameObject, 0f, 1f, Time).setDelay(Delay).setOnUpdate((float val) =>
                {
                    var c = _image.color;
                    c.a = val;
                    _image.color = c;
                });
            }
            else
                LeanTween.alpha(_rectTransform, 1f, Time).setDelay(Delay);
        }

        /// <summary>
        /// 
        /// </summary>
        public void SetTransitionIn()
        {
            _currentTransition = TransitionIn;
        }


        /// <summary>
        /// 
        /// </summary>
        public void SetTransition(TransitionType type)
        {
            _currentTransition = type;
        }

        public void Alpha(float value)
        {
            _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, value);
        }

        /// <summary>
        /// Event dispatched when a transition started
        /// </summary>
        private void Started()
        {
            if (OnTransitionStarted != null) OnTransitionStarted();
        }


        /// <summary>
        /// Event dispatched when a transition ended
        /// </summary>
        private void Completed()
        {
            if (OnTransitionEnded != null) OnTransitionEnded();
        }

        public void Pause()
        {
            LeanTween.pause(gameObject);
        }
    }
}
