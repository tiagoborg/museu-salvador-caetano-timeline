using UnityEngine;
using System.Collections;
using System.IO;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public enum SequenceState
    {
        Started,
        Playing,
        Paused,
        Ended,
        Looping,
    }

    [RequireComponent(typeof(RawImage))]
    public class ImageSequencer : MonoBehaviour
    {
        public string DirPath;
        public string FileBaseName;
        public string Extension;

        public int Fps = 25;
        public int NumFrames;

        public bool StartsAt0 = true;
        public bool PlayAtStart = true;

        [Header("Events")]
        public UnityEvent SequenceStarted = new UnityEvent();
        public UnityEvent SequencePaused = new UnityEvent();
        public UnityEvent SequenceEnded = new UnityEvent();


        [Header("Debug values")]
        [SerializeField]
        private bool _canPlay = false;
        [SerializeField]
        private int _currentFrame;
        [SerializeField]
        private bool _loop = false;
        private float _time;
        private RawImage _img;
        private int _loopStart = 0;
        private int _loopEnd = 0;
        private string _lastImage;

        private SequenceState _state;

        public SequenceState State
        {
            get { return _state; }
        }

        void Awake()
        {
            _img = GetComponent<RawImage>();
            Reset();
        }

        void Update()
        {
            if (_lastImage != GetCurrentImage())
            {
                StartCoroutine(LoadStreamingAsset());
            }
            if (!_canPlay) return;
            _time += Time.deltaTime;
            if (_time > 1f / Fps)
            {
                _time = 0;
                UpdateImage();
            }
        }

        public void Reset()
        {
            StopLoop();
            _loopStart = 0;
            _loopEnd = 0;
            _currentFrame = StartsAt0 ? 0 : 1;
            _time = 0f;

            _state = SequenceState.Paused;
            _canPlay = false;
        }

        public void Play()
        {
            _canPlay = true;
            SequenceStarted.Invoke();
        }

        public void Play(int frame)
        {
            _canPlay = true;
            _currentFrame = frame >= NumFrames ? 0 : frame;
            SequenceStarted.Invoke();
        }

        public void Pause()
        {
            _canPlay = false;
        }

        public void Loop()
        {
            _loopStart = 0;
            _loopEnd = NumFrames;
            _loop = true;
        }

        public void Loop(int startFrame, int endFrame)
        {
            if (endFrame <= startFrame) return;
            _loopStart = startFrame > NumFrames ? NumFrames : startFrame;
            _loopEnd = endFrame > NumFrames ? NumFrames : endFrame;
            _loop = true;
        }

        public void StopLoop()
        {
            _loop = false;
        }

        public string GetCurrentImage()
        {
            // ToFileUrl rather than an unconditional prefix: on Android streamingAssetsPath is
            // already jar:file://…, so this produced file://jar:file://… - the prefix problem
            // inverted. Route on whether the path is already a URL, never on the platform.
            return AppPaths.ToFileUrl(System.IO.Path.Combine(Application.streamingAssetsPath,
                DirPath + Path.DirectorySeparatorChar + FileBaseName + _currentFrame + "." + Extension));
        }

        public IEnumerator LoadStreamingAsset()
        {
            _lastImage = GetCurrentImage();
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(_lastImage))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Development.LogError(uwr.error);
                    yield break;
                }
                else
                {
                    // Get downloaded asset bundle
                    _img.texture = DownloadHandlerTexture.GetContent(uwr);
                    _img.SetNativeSize();
                }
            }

            yield return null;
        }


        void UpdateImage()
        {
            StartCoroutine(LoadStreamingAsset());
            if (_currentFrame % Fps == 0)
                Resources.UnloadUnusedAssets();

            _currentFrame++;


            _state = SequenceState.Playing;
            if (_loop && _currentFrame > _loopEnd)
            {
                _currentFrame = _loopStart;
                _state = SequenceState.Looping;
            }

            if (_currentFrame > NumFrames)
            {
                if (_loop)
                {
                    _currentFrame = StartsAt0 ? 0 : 1;
                    _state = SequenceState.Looping;
                }
                else
                {
                    _canPlay = false;
                    _state = SequenceState.Ended;
                    _currentFrame = 0;
                    SequenceEnded.Invoke();
                }
            }
        }
    }
}
