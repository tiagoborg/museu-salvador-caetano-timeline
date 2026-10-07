/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

/// <summary>
/// add android:usesCleartextTraffic="true" on AndroidManifest for nonsecure sites
/// </summary>
namespace EnigmaVirtual.Components.Video
{
    public class OnVideoPlayEvent : UnityEvent
    {
        public OnVideoPlayEvent() : base() { }
    }
    public class OnVideoStopEvent : UnityEvent
    {
        public OnVideoStopEvent() : base() { }
    }
    public class OnVideoPauseEvent : UnityEvent
    {
        public OnVideoPauseEvent() : base() { }
    }
    public class OnVideoEndReachedEvent : UnityEvent
    {
        public OnVideoEndReachedEvent() : base() { }
    }
    public class OnVideoPreparedEvent : UnityEvent
    {
        public OnVideoPreparedEvent() : base() { }
    }
    public class OnVideoNewFrameEvent : UnityEvent<ulong, ulong>
    {
        public OnVideoNewFrameEvent() : base() { }
    }
    public class OnErrorEvent : UnityEvent<string>
    {
        public OnErrorEvent() : base() { }
    }

    public abstract class VideoPlayerControllerBase : MonoBehaviour
    {
        /// <summary>
        /// Resources path of the placeholder blitted when a video fails.
        /// </summary>
        /// <remarks>
        /// A local const rather than an entry in <c>AppResources</c>, which by its own rule admits
        /// only paths with more than one caller - this has exactly one. Named at all because the
        /// literal appeared inside the call it guards, where the error message could not repeat it.
        /// </remarks>
        private const string ErrorSpritePath = "Textures/videoDownloadError";

        public VideoSource source
        {
            get { return videoPlayer.source; }
            set
            {
                _source = value;
                videoPlayer.source = value;
            }
        }

        public VideoClip clip
        {
            get { return videoPlayer.clip; }
            set
            {
                _clip = value;
                videoPlayer.clip = value;
            }
        }
        public bool loop
        {
            get { return videoPlayer.isLooping; }
            set
            {
                _loop = value;
                videoPlayer.isLooping = value;
            }
        }
        public virtual string url
        {
            get { return videoPlayer.url; }
            set
            {
                _url = value;
                videoPlayer.url = value;
            }
        }
        public bool waitForFirstFrame
        {
            get { return videoPlayer.waitForFirstFrame; }
            set
            {
                _waitForFirstFrame = value;
                videoPlayer.waitForFirstFrame = value;
            }
        }
        public bool skipOnDrop
        {
            get { return videoPlayer.skipOnDrop; }
            set
            {
                _skipOnDrop = value;
                videoPlayer.skipOnDrop = value;
            }
        }

        [SerializeField]
        private VideoSource _source;
        [ConditionalField(nameof(_source), false, VideoSource.Url)]
        [SerializeField]
        protected string _url;
        [ConditionalField(nameof(_source), false, VideoSource.VideoClip)]
        [SerializeField]
        private VideoClip _clip;

        [Space]
        [SerializeField]
        protected bool playOnAwake;
        [ConditionalField(nameof(playOnAwake), false, false)]
        [SerializeField]
        protected bool prepareOnAwake;
        [SerializeField]
        private bool _waitForFirstFrame = true;
        [SerializeField]
        private bool _loop;
        [SerializeField]
        private bool _skipOnDrop = true;

        [Range(0f, 10f)]
        public float playbackSpeed = 1;
        public VideoRenderMode renderMode = VideoRenderMode.RenderTexture;

        [ConditionalField(nameof(renderMode), false, VideoRenderMode.RenderTexture)]
        [SerializeField] private RenderTexture _targetTexture;
        [ConditionalField(nameof(renderMode), false, new object[] { VideoRenderMode.CameraNearPlane, VideoRenderMode.CameraFarPlane })]
        [SerializeField] private Camera _camera;
        [ConditionalField(nameof(renderMode), false, VideoRenderMode.MaterialOverride)]
        [SerializeField] private Renderer _renderer;

        public VideoAspectRatio aspectRatio = VideoAspectRatio.FitHorizontally;
        [Space]
        public VideoAudioOutputMode audioOutputMode = VideoAudioOutputMode.Direct;
        [Space]
        public int startFrame = 0;
        public bool useVideoDimensions = false;
        [Range(0f, 1f)]
        public float targetCameraAlpha = 1;

        [Space]
        public bool resetTextureOnStop = true;

        [Space(10)]
        public OnVideoPlayEvent onVideoPlayEvent = new OnVideoPlayEvent();
        public OnVideoStopEvent onVideoStopEvent = new OnVideoStopEvent();
        public OnVideoPauseEvent onVideoPauseEvent = new OnVideoPauseEvent();
        public OnVideoEndReachedEvent onVideoEndReachedEvent = new OnVideoEndReachedEvent();
        public OnVideoPreparedEvent onVideoPreparedEvent = new OnVideoPreparedEvent();
        /// <summary>
        /// current frame, postion
        /// </summary>
        public OnVideoNewFrameEvent onVideoNewFrameEvent = new OnVideoNewFrameEvent();
        public OnErrorEvent onErrorEvent = new OnErrorEvent();

        [HideInInspector]
        protected VideoPlayer videoPlayer;

        //
        // Summary:
        //     The frame index of the currently available frame in VideoPlayer.texture. (Read Only)
        public ulong frame { get { return (ulong)videoPlayer.frame; } }
        //
        // Summary:
        //     Number of frames in the current video content. (Read Only)
        public ulong frameCount { get { return videoPlayer.frameCount; } }
        //
        // Summary:
        //     Whether playback is paused. (Read Only)
        public bool isPaused { get { return videoPlayer.isPaused; } }
        //
        // Summary:
        //     Whether content is being played. (Read Only)
        public bool isPlaying { get { return videoPlayer.isPlaying; } }
        //
        // Summary:
        //     The clock time that the VideoPlayer follows to schedule its samples. The clock
        //     time is expressed in seconds. (Read Only)
        public double clockTime { get { return videoPlayer.clockTime; } }
        //
        // Summary:
        //     The frame rate of the clip or URL in frames/second. (Read Only)
        public float frameRate { get { return videoPlayer.frameRate; } }
        //
        // Summary:
        //     The length of the VideoClip, or the URL, in seconds. (Read Only)
        public double length { get { return videoPlayer.length; } }
        //
        // Summary:
        //     Whether current time can be changed using the time or timeFrames property. (Read Only)
        public bool canSetTime { get { return videoPlayer.canSetTime; } }
        //
        // Summary:
        //     The presentation time of the currently available frame in VideoPlayer.texture.
        public double time
        {
            get { return videoPlayer.time; }
            set { videoPlayer.time = value; }
        }
        //
        // Summary:
        //     Whether current volume can be changed using the SetDirectAudioVolume method. (Read Only)
        public bool canSetDirectAudioVolume { get { return videoPlayer.canSetDirectAudioVolume; } }
        //
        // Summary:
        //     Video width. (Read Only)
        public uint videoWidth { get { return videoPlayer.width; } }
        //
        // Summary:
        //     Video height. (Read Only)
        public uint videoHeight { get { return videoPlayer.height; } }
        public bool isPrepared { get { return videoPlayer.isPrepared; } }

        public RenderTexture renderTexture { get { return videoPlayer.targetTexture; } }

        private bool playOnPrepared = false;
        private bool isInitialised = false;

        private void Awake()
        {
            if (isInitialised)
                return;

            isInitialised = true;
            videoPlayer = gameObject.AddComponent(typeof(VideoPlayer)) as VideoPlayer;
            videoPlayer.playOnAwake = false;
        }

        protected virtual void Start()
        {
            source = _source;
            if (source == VideoSource.Url)
                url = _url;
            if (source == VideoSource.VideoClip)
                clip = _clip;

            waitForFirstFrame = _waitForFirstFrame;
            loop = _loop;
            skipOnDrop = _skipOnDrop;
            videoPlayer.playbackSpeed = playbackSpeed;

            videoPlayer.renderMode = renderMode;
            if (renderMode == VideoRenderMode.RenderTexture)
                videoPlayer.targetTexture = _targetTexture;
            if (renderMode == VideoRenderMode.CameraFarPlane || renderMode == VideoRenderMode.CameraNearPlane)
                videoPlayer.targetCamera = _camera;
            if (renderMode == VideoRenderMode.MaterialOverride)
                videoPlayer.targetMaterialRenderer = _renderer;

            videoPlayer.aspectRatio = aspectRatio;
            videoPlayer.audioOutputMode = audioOutputMode;

            videoPlayer.targetCameraAlpha = targetCameraAlpha;
            videoPlayer.frame = startFrame;
            videoPlayer.playOnAwake = playOnAwake;

            videoPlayer.loopPointReached += VideoPlayer_loopPointReached;
            videoPlayer.errorReceived += VideoPlayer_errorReceived;

            if (playOnAwake)
                Play();
            else if (prepareOnAwake)
            {
                if ((source == VideoSource.Url && !string.IsNullOrEmpty(url)) || (source == VideoSource.VideoClip && clip != null))
                {
                    videoPlayer.prepareCompleted += VideoPlayer_prepareCompleted;
                    Prepare();
                }
            }
        }

        protected void Update()
        {
            if (!isPlaying)
                return;

            onVideoNewFrameEvent?.Invoke(frame, frame / frameCount);
        }

        private void VideoPlayer_errorReceived(VideoPlayer source, string message)
        {
            if (message.Contains("cannot play"))
            {
                Stop();
                DisplayError();
                Development.LogError(message);
            }
        }

        /// <summary>
        /// Displaeys error image on render texture if active in settings.
        /// </summary>
        private void DisplayError()
        {
            if (renderMode == VideoRenderMode.RenderTexture)
            {
                if (renderTexture != null)
                {
                    if (Core.settings.System.showImageDownloadError)
                    {
                        // Guarded because this is the error path: an unguarded `.texture` on a missing
                        // or renamed asset turns "the video failed" into a NullReferenceException
                        // raised while reporting it, on the one path least likely to be exercised
                        // before a site visit. The two image sites already guard this exact case and
                        // log at LogError, so the framework was disagreeing with itself.
                        Sprite errorSprite = Resources.Load<Sprite>(ErrorSpritePath);

                        // Both halves, matching what DownloadableImage checks. A sprite whose texture
                        // reference broke on import loads non-null and would reach Blit with a null
                        // source, so guarding only the sprite looks like parity without being it.
                        if (errorSprite != null && errorSprite.texture != null)
                        {
                            Graphics.Blit(errorSprite.texture, renderTexture);
                        }
                        else
                        {
                            // The consequence is a CLEARED surface, not a frozen frame. errorReceived
                            // calls Stop() before this, and Stop() releases the target texture whenever
                            // resetTextureOnStop is set - which it is by default - and that target
                            // texture is this renderTexture. An earlier version of this message said
                            // "keeps whatever frame it last held", which would send the reader looking
                            // for a frozen video they do not have.
                            Development.LogError($"Resources/{ErrorSpritePath} is missing or has no "
                                + "texture, so the video error placeholder cannot be shown. The "
                                + "RenderTexture is left as Stop() left it - cleared, or undefined if "
                                + "resetTextureOnStop is off - so the surface goes blank rather than "
                                + "showing an error.");
                        }
                    }
                }
                else
                    Development.LogWarning("Target RenderTexture missing!");
            }
        }

        /// <summary>
        /// Resizes UI rectransform for videoclip fit.
        /// </summary>
        /// <param name="rect"></param>
        internal void ResizeUI(ref RectTransform rect)
        {
            Vector2 parentSize = rect.sizeDelta;

            Vector2 imageSize = new Vector2(videoWidth, videoHeight);

            Vector2 newSize = imageSize;
            float ratio = imageSize.y / imageSize.x;
            newSize.x = parentSize.x;
            newSize.y = ratio * newSize.x;

            if (newSize.y > parentSize.y)
            {
                ratio = imageSize.x / imageSize.y;
                newSize.y = parentSize.y;
                newSize.x = ratio * newSize.y;
            }

            rect.sizeDelta = newSize;
        }

        protected virtual void OnDestroy()
        {
            videoPlayer.loopPointReached -= VideoPlayer_loopPointReached;
            videoPlayer.prepareCompleted -= VideoPlayer_prepareCompleted;
            videoPlayer.errorReceived -= VideoPlayer_errorReceived;
            if (_targetTexture != null)
                _targetTexture.Release();
        }

        /// <summary>
        /// Initiates playback engine preparation to play video from url. 
        /// Forces source to VideoSource.Url
        /// </summary>
        /// <param name="value">video url</param>
        /// <param name="playOnPrepared">callback action</param>
        // The MediaVO overload of the method below moved to App/Extensions/MediaVOExtensions.cs as an
        // extension method: MediaVO is a project type, and the framework naming it would be one of the
        // dependency cycles. Behaviour is unchanged - it null-checked, picked url or thumb, and called this
        // same primitive overload. Call sites need no edit, because extension methods in the global
        // namespace are in scope wherever App code is.
        public void Prepare(string value, bool playOnPrepared = false)
        {
            Development.CheckPoint();

            source = VideoSource.Url;
            url = value;

            Prepare(playOnPrepared);
        }

        /// <summary>
        /// Initiates playback engine preparation.
        /// </summary>
        /// <param name="playOnPrepared"></param>
        public void Prepare(bool playOnPrepared = false)
        {
            Development.CheckPoint();

            if (!ValidateSourceAndRender())
                return;

            this.playOnPrepared = playOnPrepared;
            videoPlayer.prepareCompleted += VideoPlayer_prepareCompleted;
            videoPlayer.Prepare();
        }

        /// <summary>
        /// Set the direct-output audio volume for the specified track.
        /// </summary>
        /// <param name="volume">New volume, between 0 and 1.</param>
        /// <param name="trackIndex">Track index for which the volume is set.</param>
        public void SetDirectAudioVolume(float volume, ushort trackIndex = 0)
        {
            videoPlayer.SetDirectAudioVolume(trackIndex, volume);
        }

        /// <summary>
        /// Set the direct-output audio mute status for the specified track.
        /// </summary>
        /// <param name="mute">Mute on/off.</param>
        /// <param name="trackIndex">Track index for which the mute is set.</param>
        public void SetDirectAudioMute(bool mute, ushort trackIndex = 0)
        {
            videoPlayer.SetDirectAudioMute(trackIndex, mute);
        }

        protected virtual void VideoPlayer_prepareCompleted(VideoPlayer vp)
        {
            //Debug.LogError(videoPlayer.width + " " + videoPlayer.height);

            Development.CheckPoint();

            onVideoPreparedEvent?.Invoke();

            videoPlayer.prepareCompleted -= VideoPlayer_prepareCompleted;

            if (useVideoDimensions)
            {
                _targetTexture.width = (int)videoPlayer.width;
                _targetTexture.height = (int)videoPlayer.height;
            }

            if (playOnPrepared)
                Play();
        }

        private void VideoPlayer_loopPointReached(VideoPlayer vp)
        {
            onVideoEndReachedEvent?.Invoke();

            if (!loop)
                Stop();
        }

        /// <summary>
        /// Validates video source and render mode settings.
        /// </summary>
        /// <returns></returns>
        private bool ValidateSourceAndRender()
        {
            if (source == VideoSource.Url)
            {
                if (!string.IsNullOrEmpty(url))
                {
                    // `url`, not `url.GetFileType()`: the predicate takes the extension itself, so
                    // passing one applied it twice. Harmless for "a.mp4" - Path.GetExtension(".mp4")
                    // is ".mp4" - but an extension-less url (a live stream) extracts to "" and the
                    // second application THREW ArgumentException, so this whole guard was skipped
                    // and DisplayError below never ran. Measured both ways.
                    if (!MediaFile.isValidVideoFileType(url))
                    {
                        DisplayError();
                        Development.LogError("Invalid video filetype [" + url.GetFileType() + "]");
                        return false;
                    }
                }
                else
                {
                    DisplayError();
                    onErrorEvent?.Invoke("Empty or null video url!");
                    Development.LogWarning("Empty or null video url!");
                    return false;
                }
            }
            else
            {
                if (clip == null)
                {
                    DisplayError();
                    onErrorEvent?.Invoke("Empty or null clip!");
                    Development.LogWarning("Empty or null clip!");
                    return false;
                }
            }

            if (renderMode == VideoRenderMode.RenderTexture)
            {
                if (renderTexture == null)
                {
                    Development.LogWarning("Target RenderTexture missing!");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Starts playback of video from url. 
        /// Forces source to VideoSource.Url 
        /// </summary>
        /// <param name="value">video url</param>
        /// <param name="playOnPrepared">callback action</param>
        // The MediaVO overload of this method moved to App/Extensions/MediaVOExtensions.cs.
        // Note it is no longer virtual-overridable there: extension methods cannot be overridden,
        // so a branched project that overrode Play(MediaVO) gets CS0115 and must override
        // Play(string) instead - which is the right seam anyway.
        public virtual void Play(string value)
        {
            Development.CheckPoint();
            //targetTexture.Release();

            source = VideoSource.Url;
            url = value;

            Play();
        }

        /// <summary>
        /// Starts playback.
        /// </summary>
        public virtual void Play()
        {
            Development.CheckPoint();
            if (!ValidateSourceAndRender())
                return;

            onVideoPlayEvent?.Invoke();
            if (!isInitialised)
                Awake();
            videoPlayer.Play();
        }

        /// <summary>
        /// Pauses the playback and leaves the current time intact.
        /// </summary>
        public virtual void Pause()
        {
            Development.CheckPoint();
            onVideoPauseEvent?.Invoke();
            videoPlayer.Pause();
        }

        /// <summary>
        /// Stops the playback and sets the current time to 0.
        /// </summary>
        public virtual void Stop()
        {
            Development.CheckPoint();
            onVideoStopEvent?.Invoke();
            videoPlayer.Stop();
            if (resetTextureOnStop)
                _targetTexture.Release();
        }
    }
}