using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class DownloadableImage : MonoBehaviour
    {
        [SerializeField] private Image _image;
        [SerializeField] private RawImage _rawImage;

        [Header("Downloadable Image")]
        public string url;
        [SerializeField] private bool downloadOnStart = false;

        [Header("Effects")]
        [SerializeField] private bool useFadeIn = true;
        [SerializeField] private float fadeInSpeed = .25f;

        [Header("Image Resizer")]
        [SerializeField] private bool useImageResizer = true;
        [SerializeField] private FitType fitType = FitType.Both;
        [SerializeField] private bool centerAnchor = false;
        [SerializeField] private bool useMask = false;

        [Space]
        [SerializeField] private bool preserveAspect = true;
        [SerializeField] private bool setNativeSize = false;
        [SerializeField] private bool resetTransformSizeOnLoad = true;

        [Space]
        public UnityEvent onLoadCompleteEvent = new UnityEvent();
        public UnityEvent onLoadSucessEvent = new UnityEvent();
        public UnityEvent<string> onLoadFailedEvent = new UnityEvent<string>();

        [ReadOnly, SerializeField] private bool _isLoading;

        public bool isLoading { get { return _isLoading; } }

        private Vector2 originalSizeDelta;
        private MaskableGraphic maskableGraphic;

        /// <summary>
        /// The texture and sprite this component created for the image it is currently showing, so the
        /// next load can free them. See <see cref="ReleaseCreated"/>.
        /// </summary>
        /// <remarks>
        /// <b>The texture is the leak; the sprite is the rounding error.</b> Worth stating, because the
        /// obvious fix - destroying only the sprite, a few hundred bytes - leaves the pixels behind.
        /// <c>DownloadHandlerTexture.GetContent</c> allocates a
        /// <see cref="Texture2D"/> per download and nothing destroyed it: megabytes per image shown,
        /// unbounded over a kiosk day, with <c>Resources.UnloadUnusedAssets</c> never running on this
        /// path.
        /// <para>
        /// Only what this component created is tracked. The error placeholder is a <c>Resources</c>
        /// asset shared by every <c>DownloadableImage</c>, so it never becomes
        /// <c>_createdTexture</c> - destroying it would corrupt it for the rest of the session, the
        /// same trap <c>TextureCacheManager</c> guards against. And <see cref="SetImageSprite"/> is
        /// public and <c>UnityEvent</c>-bindable, so it deliberately does <b>not</b> take ownership:
        /// only <see cref="OnSpriteCreated"/>, the loader's own callback, knows a sprite was made for
        /// this component. Destroying an asset is silent in a player.
        /// </para>
        /// </remarks>
        private Sprite _createdSprite;
        private Texture _createdTexture;

        private void Reset()
        {
            if (_image == null)
                _image = GetComponent<Image>();

            if (_rawImage == null)
                _rawImage = GetComponent<RawImage>();
        }

        private void Awake()
        {
            originalSizeDelta = transform.GetComponent<RectTransform>().sizeDelta;
            maskableGraphic = default;

            if (_image != null)
                maskableGraphic = _image.GetComponent<MaskableGraphic>();
            else if (_rawImage != null)
                maskableGraphic = _rawImage.GetComponent<MaskableGraphic>();

            maskableGraphic.color = new Color(maskableGraphic.color.r, maskableGraphic.color.g, maskableGraphic.color.b, 0f);

            ResetImageSize();
        }

        void Start()
        {
            if (downloadOnStart && !string.IsNullOrEmpty(url))
                Load(url);
        }

        /// <summary>
        /// Releases anyone waiting on a load that this component is about to stop running.
        /// </summary>
        /// <remarks>
        /// <para><b>OnDisable, not OnDestroy, and not a try/finally in the coroutine.</b> Unity stops a
        /// behaviour's coroutines when it is disabled *or* destroyed, so <c>DownloadImage_IE</c> dies
        /// either way with nothing after its <c>yield</c> ever running. <c>OnDisable</c> runs in both
        /// cases; <c>OnDestroy</c> alone would have missed the worse one, where a
        /// <c>SetActive(false)</c> leaves <c>_isLoading</c> **true** forever and the in-flight guard then
        /// refuses every future <c>Load</c> on that object while reporting "already loading" - a lie
        /// that sends you to the wrong bug.</para>
        ///
        /// <para><b>A <c>try/finally</c> around the coroutine body does not work, and this was
        /// measured rather than assumed.</b> It looks correct, and <c>TextureCacheManager</c>'s own
        /// comment cites it as precedent, but
        /// <c>TextureCacheManager</c>'s own comment as precedent. Unity **abandons** a stopped
        /// iterator rather than disposing it: measured twice with a control, a <c>finally</c> runs on
        /// normal completion (True) but not for <c>StopCoroutine</c>, <c>StopAllCoroutines</c>,
        /// <c>SetActive(false)</c>, <c>enabled = false</c> or <c>DestroyImmediate</c> (all False). The
        /// version of this fix built on that premise reported nothing at all.</para>
        ///
        /// <para><c>ReleaseCreated</c> deliberately stays in <c>OnDestroy</c>: freeing the texture on a
        /// temporary hide would blank the image on re-enable.</para>
        /// </remarks>
        void OnDisable()
        {
            if (!_isLoading || _quitting)
                return;

            // Says what actually happens, not what would be reassuring: FailLoad raises
            // onLoadFailedEvent but *removes* the onLoadComplete listeners without invoking them, so a
            // caller that subscribed only to completion is dropped rather than released. An earlier
            // draft claimed both were released, which would have told the person reading the log to
            // stop looking. Logged before the notify so the evidence survives a throwing listener, and
            // always compiled.
            Development.LogError($"Image load for [{url}] was cancelled before it finished - the "
                + "DownloadableImage was disabled or destroyed. onLoadFailedEvent is raised; any "
                + "onLoadComplete listeners are dropped without firing. During a screen change this "
                + "is expected.");

            try
            {
                FailLoad("Load cancelled before it finished");
            }
            catch (Exception e)
            {
                // A listener whose target is a destroyed UnityEngine.Object is silently skipped by
                // UnityEvent (BaseInvokableCall.AllowInvoke) and strands nobody - measured in the
                // editor. The
                // unfiltered case is a lambda capturing a destroyed component: its target is a
                // compiler-generated class, which that check does not recognise, and a throw there
                // aborts the listeners after it. UnityEvent has no per-listener isolation and no
                // public accessor to add any - GetCallsCount and PrepareInvoke are both private, so
                // not even a derived event type can enumerate them, so a per-callback try/catch is
                // unavailable here. This catch stops the throw escaping OnDisable; it
                // cannot stop the stranding.
                Development.LogException(e);
            }
        }

        void OnDestroy()
        {
            // Set before StopAllCoroutines, so the finally in DownloadImage_IE - which that call
            // triggers - can refuse a listener that answers the cancellation by starting another load
            // on a component that is going away.
            _destroyed = true;

            StopAllCoroutines();

            // The last load has no successor to free it, so a gallery torn down between screens would
            // leak one texture per item without this. Deliberately not in OnDisable: a hidden
            // component is expected to still show its image when it comes back.
            ReleaseCreated();
        }

        /// <summary>Set in <see cref="OnDestroy"/> so a re-entrant load cannot start on a dying object.</summary>
        private bool _destroyed;

        /// <summary>Set once when the player begins shutting down, to keep teardown quiet.</summary>
        private static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HookQuitting()
        {
            // Static, so it survives the scene loads the boot chain performs, and re-armed on every
            // play session because RuntimeInitializeOnLoadMethod runs again.
            _quitting = false;
            Application.quitting += () => _quitting = true;
        }

        /// <summary>
        /// Destroys the texture and sprite this component created for the load it is currently showing.
        /// Called just before a replacement takes their place, and on teardown.
        /// </summary>
        /// <remarks>
        /// The graphic is cleared before the destroy, and only when it is still pointing at *our*
        /// object - if something else overwrote it, that owner's assignment stands and we free only
        /// our orphan. See the remarks on <see cref="_createdSprite"/> for what is deliberately never
        /// tracked here.
        /// </remarks>
        private void ReleaseCreated()
        {
            if (_createdSprite != null)
            {
                if (_image != null && _image.sprite == _createdSprite)
                    _image.sprite = null;

                Destroy(_createdSprite);
                _createdSprite = null;
            }

            if (_createdTexture != null)
            {
                if (_rawImage != null && _rawImage.texture == _createdTexture)
                    _rawImage.texture = null;

                Destroy(_createdTexture);
                _createdTexture = null;
            }
        }

        private void ResetImageSize()
        {
            if (_image != null)
                _image.GetComponent<RectTransform>().sizeDelta = originalSizeDelta;
            else if (_rawImage != null)
                _rawImage.GetComponent<RectTransform>().sizeDelta = originalSizeDelta;
        }

        //public void LoadMediaThumb(MediaVO media, UnityAction onLoadComplete = null, UnityAction<string> onLoadFailed = null)
        //{
        //    Load(media, true, onLoadComplete, onLoadFailed);
        //}

        // The MediaVO overload of the method below moved to App/Extensions/MediaVOExtensions.cs as an
        // extension method: MediaVO is a project type, and naming it here would make the framework
        // depend on App. Behaviour is unchanged - it null-checked, picked url or thumb, and called this
        // same primitive overload. Call sites need no edit, because extension methods in the global
        // namespace are in scope wherever App code is.
        /// <summary>
        /// Loads <paramref name="url"/>, falling back to <paramref name="fallbackUrl"/> when it is empty.
        /// </summary>
        /// <remarks>
        /// The fallback rule and the failure reporting live here, in the class that owns
        /// <see cref="onLoadFailedEvent"/>, rather than in the caller. Putting them in an App-side
        /// extension would enforce this class's own invariant - "that event means I tried and failed" -
        /// from outside it, and fork four methods of shared behaviour on every branched project. The caller now supplies two candidate strings and
        /// nothing else.
        /// </remarks>
        public bool Load(string url, string fallbackUrl)
        {
            if (!string.IsNullOrEmpty(url))
                return Load(url);

            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                Development.LogWarning("null or empty primary URL. Loading the fallback URL!");

                return Load(fallbackUrl);
            }

            // These aborts leave a blank image on screen. At Warning they vanished on any build without
            // DO_LOGS, so a kiosk showed a hole and said nothing.
            Development.LogError("Image loading aborted: both the URL and its fallback are null or empty.");
            FailLoad("null or empty URL");

            return false;
        }

        /// <returns>
        /// <b>false</b> when the load was refused and no event will ever be raised for it - today only
        /// the re-entrancy guard, which cannot report through per-instance events because the listeners
        /// belong to the load already running. A caller that registers handlers before calling
        /// must register them only when this returns <b>true</b>.
        /// </returns>
        public bool Load(string url)
        {
            Development.Log("Load image: " + url);

            if (_destroyed)
            {
                // Reachable from the finally above: FailLoad clears _isLoading before invoking, so a
                // listener answering the cancellation with another Load would otherwise pass the
                // guard below and StartCoroutine on a dying component. Dormant here - no runtime
                // listener in this repo retries - and live for any branched project that does.
                Development.LogError($"Image load for [{url}] refused: this DownloadableImage is "
                    + "being destroyed.");

                return false;
            }

            if (_isLoading)
            {
                // Split out from the empty-url case below: a re-entrant Load while one is still in
                // flight reported "Invalid or empty url", which sent everyone hunting a content
                // problem that did not exist.
                //
                // It raises nothing now, deliberately. Invoking and clearing
                // onLoadFailedEvent - but those listeners belong to the load that is still running, so
                // the rejected caller's problem was reported as the *in-flight* load's failure, and
                // that load's own failure later invoked an empty list. This exit cannot report through
                // the events at all: they are per-instance and there are two callers in play. It
                // reports through the return value instead.
                Development.LogError($"Image load for [{url}] ignored: this DownloadableImage is "
                    + $"already loading [{this.url}]. Wait for onLoadCompleteEvent, or use a second "
                    + "instance. Load returned false; no event was raised, because the events belong "
                    + "to the load still in flight.");

                return false;
            }

            if (string.IsNullOrEmpty(url))
            {
                Development.LogError("Image loading aborted: null or empty url.");
                FailLoad("Invalid or empty url");
                return false;
            }
            
            if (!MediaFile.isValidImageFileType(url))
            {
                // With the shipped showImageDownloadError:true this exit used to raise the *wrong*
                // event. Showing the placeholder went
                // through SetImageTexture/SetRawImageTexture, which are the success setters, so an
                // unusable filetype announced onLoadCompleteEvent - a false success - and then returned
                // without ever invoking onLoadFailedEvent. So the caller was told the image loaded, and
                // the onLoadFailed listener it had registered was never invoked and never cleared,
                // surviving into the *next* Load on this instance. Note this holds only with
                // showImageDownloadError on; with it off the exit is silent instead.
                Development.LogError($"Invalid image filetype [{url.GetFileType()}] for [{url}]");

                ShowErrorPlaceholder();
                FailLoad("Invalid image filetype");

                return false;
            }

            if (resetTransformSizeOnLoad)
                ResetImageSize();

            this.url = Core.settings.AppData.GetAssetPath(url);

            maskableGraphic.color = new Color(maskableGraphic.color.r, maskableGraphic.color.g, maskableGraphic.color.b, 0f);

            StopAllCoroutines();

            _isLoading = true;

            if (_image != null)
                StartCoroutine(DownloadImage_IE(this.url, SetImageTexture));
            else if (_rawImage != null)
                StartCoroutine(DownloadImage_IE(this.url, SetRawImageTexture));
            else
            {
                // No coroutine starts, so without this _isLoading stays true forever and every later
                // Load on this instance is refused by the guard above - silently, since nothing else
                // reports. ShowErrorPlaceholder already logs this exact condition, which made the
                // omission four lines up the odd one out.
                Development.LogError("No Image or RawImage is wired on this DownloadableImage, so "
                    + $"[{this.url}] cannot be shown.");
                FailLoad("No Image or RawImage target");

                return false;
            }

            return true;
        }        

        [ContextMenu("Force Load")]
        public void ForceLoad()
        {
            Load(url);
        }

        /// <summary>
        /// Set reference to Image
        /// </summary>
        /// <param name="value"></param>
        public void SetImage(Image value)
        {
            _image = value;
            _rawImage = null;
        }

        /// <summary>
        /// Set referente to RawImage
        /// </summary>
        /// <param name="value"></param>
        public void SetRawImage(RawImage value)
        {
            _rawImage = value;
            _image = null;
        }

        private IEnumerator DownloadImage_IE(string url, System.Action<Texture> callback)
        {
            Development.Log("Downloading image: " + url);

            // Was two platform-gated prefix tests - #if UNITY_ANDROID + StartsWith("/storage/") and
            // #if UNITY_IOS + StartsWith("/Users/") - neither of which ever matched a real path.
            // "/storage/" is only persistentDataPath with Write Permission = External(SDCard) and this
            // project ships ForceSDCardPermission: 0; "/Users/" is a macOS path, not an iOS one. So
            // every media file under persistentDataPath went to UnityWebRequest with no scheme, which
            // is live on Android now that asset paths resolve under persistentDataPath.
            //
            // Assigns the parameter, not the field: this.url keeps the scheme-free value, so ForceLoad
            // cannot double-prefix and the Inspector shows what the operator configured.
            url = AppPaths.ToFileUrl(url);

            // using: UnityWebRequest holds a native handle and its download buffer until disposed, and
            // this one never was. GetContent's Texture2D outlives the request - TextureCacheManager
            // already relies on exactly that, storing the result in its cache from inside a using.
            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Development.LogError(string.Concat("Error ", url, " www: ", www.error));

                    // The placeholder must not go out through `callback`, which is the *success*
                    // path - SetRawImageTexture and SetImageSprite both raise and clear
                    // onLoadCompleteEvent. So a failed load announced completion, before
                    // onLoadFailedEvent, to listeners that were about to be told the opposite.
                    //
                    // Both graphic paths behave the same way. The Image path does NOT diverge:
                    // ImageLoaderThread.LoadImage is synchronous and says so in its own remarks, and
                    // MainController enqueues straight into it. There is no asynchronous hazard
                    // here - it was one bug on two identical paths.
                    ShowErrorPlaceholder();
                    FailLoad(www.error);
                }
                else
                {
                    Texture texture = DownloadHandlerTexture.GetContent(www);

                    if (texture != null)
                    {
                        // Order matters: free the previous load first, then claim this one, then show
                        // it. All synchronous, so the graphic is never blank across a frame boundary.
                        ReleaseCreated();
                        _createdTexture = texture;

                        callback?.Invoke(texture);
                        onLoadSucessEvent?.Invoke();
                    }
                    else
                    {
                        Development.LogError("Invalid texture in DownloadHandlerTexture!");
                        FailLoad("Invalid texture in DownloadHandlerTexture.");
                    }
                }
            }
        }

        /// <summary>
        /// Shows the shared error placeholder without going anywhere near the success path.
        /// </summary>
        /// <remarks>
        /// <para>Every failure that wants something on screen calls this. It deliberately raises no
        /// event and does not touch <c>_isLoading</c> - <see cref="FailLoad"/> owns both, so "show
        /// something" and "announce the outcome" cannot drift apart again.</para>
        ///
        /// <para>The placeholder is a shared <c>Resources</c> asset: it is never assigned to
        /// <c>_createdTexture</c> and never destroyed, because every <c>DownloadableImage</c> in the
        /// scene is looking at the same instance. <see cref="ReleaseCreated"/> still runs, so the
        /// texture this component *did* create is freed before the placeholder covers it.</para>
        ///
        /// <para>The <c>Image</c> branch assigns the sprite straight from <c>Resources</c> rather than
        /// handing its texture to <c>ImageLoaderThread</c> to have a sprite built from it, which is
        /// what the old code did - a thread-pool round trip to reconstruct an asset we were already
        /// holding.</para>
        /// </remarks>
        private void ShowErrorPlaceholder()
        {
            if (!Core.settings.System.showImageDownloadError)
                return;

            Sprite placeholder = Resources.Load<Sprite>(AppResources.ImageDownloadErrorSprite);

            if (placeholder == null || placeholder.texture == null)
            {
                // Always compiled: without this the failure shows nothing and says nothing about why
                // the thing that was supposed to explain the failure is also missing.
                Development.LogError("The error placeholder is missing from "
                    + $"Resources/{AppResources.ImageDownloadErrorSprite}, so a failed image load will show whatever "
                    + "was on screen before it.");
                return;
            }

            ReleaseCreated();

            if (_image != null)
            {
                _image.sprite = placeholder;

                if (setNativeSize)
                    _image.SetNativeSize();
                if (preserveAspect)
                    _image.preserveAspect = true;
            }
            else if (_rawImage != null)
            {
                _rawImage.texture = placeholder.texture;
            }
            else
            {
                Development.LogError("No Image or RawImage is wired on this DownloadableImage, so the "
                    + "error placeholder cannot be shown.");
                return;
            }

            ResizeImage(placeholder.texture.width, placeholder.texture.height);
            FadeIn();
        }

        /// <summary>
        /// The single terminal-failure exit: reports the reason, drops both subscriptions, and clears
        /// the loading flag.
        /// </summary>
        /// <remarks>
        /// <para>Clearing <b>both</b> event lists is the point. A load that has failed will never
        /// complete, so a surviving <c>onLoadCompleteEvent</c> listener does not wait harmlessly - it
        /// waits for the *next* <c>Load</c> on this instance and fires for that one instead. That is
        /// reachable from every exit, not just the obvious one.</para>
        ///
        /// <para>"Both lists" means both <b>runtime</b> lists. <c>RemoveAllListeners</c> does not touch
        /// persistent, Inspector-wired calls, so a prefab that hooks <c>onLoadCompleteEvent</c> in the
        /// editor keeps firing on every later success exactly as before - which is what makes the
        /// subscribe-once-and-reuse pattern survive this change, and that distinction is the reason
        /// the design is defensible.</para>
        ///
        /// <para>Not used by the re-entrancy guard in <see cref="Load(string)"/>, and that is
        /// deliberate: there the failing call is the *new* one while a previous load is still in
        /// flight, so clearing <c>_isLoading</c> would cancel the bookkeeping of a load that is still
        /// running. (That guard does still clear <c>onLoadFailedEvent</c>, which belongs to the
        /// in-flight load - a separate defect, and not this one.)</para>
        /// </remarks>
        private void FailLoad(string reason)
        {
            // Before the invoke, not after. Retrying from the failure handler is the natural consumer
            // move now that a failure no longer raises completion - and with the old ordering that
            // retry hit the re-entrancy guard, was refused as "already loading", and re-entered this
            // same event recursively.
            _isLoading = false;

            onLoadCompleteEvent?.RemoveAllListeners();
            onLoadFailedEvent?.Invoke(reason);
            onLoadFailedEvent?.RemoveAllListeners();
        }

        private void SetRawImageTexture(Texture texture)
        {
            Development.CheckPoint();

            if (_rawImage == null || texture == null)
            {
                Development.LogError("Missing RawImage or invalid texture!");
                FailLoad("Missing RawImage or invalid texture.");
                return;
            }

            _rawImage.texture = texture;

            ResizeImage(texture.width, texture.height);

            onLoadCompleteEvent?.Invoke();
            onLoadCompleteEvent?.RemoveAllListeners();
            _isLoading = false;
            FadeIn();
        }

        private void SetImageTexture(Texture texture)
        {
            Development.Log("Setting Image!");
            Core.main.EnqueueImageLoadThreadPool(new ImageQueueItem(texture, OnSpriteCreated));
        }

        private void FadeIn()
        {
            if (!useFadeIn)
                maskableGraphic.color = new Color(maskableGraphic.color.r, maskableGraphic.color.g, maskableGraphic.color.b, 1f);
            else
            {
                maskableGraphic.color = new Color(maskableGraphic.color.r, maskableGraphic.color.g, maskableGraphic.color.b, 0f);
                LeanTween.alpha(maskableGraphic.GetComponent<RectTransform>(), 1f, fadeInSpeed);
            }
        }

        /// <summary>
        /// <see cref="ImageLoaderThread"/>'s callback: the one path that knows the sprite was created
        /// for this component, so the only one that may claim ownership of it.
        /// </summary>
        /// <remarks>
        /// Separate from the public <see cref="SetImageSprite"/> on purpose. That method is
        /// <c>UnityEvent</c>-bindable with a dragged <c>Sprite</c> argument, and if it took ownership,
        /// a project wiring a sprite *asset* to it would have that asset destroyed on the next load -
        /// silently, in a player.
        /// </remarks>
        private void OnSpriteCreated(Sprite sprt)
        {
            _createdSprite = sprt;
            SetImageSprite(sprt);
        }

        /// <summary>
        /// Shows <paramref name="sprt"/> on this component's <c>Image</c>. Does <b>not</b> take
        /// ownership - the caller keeps it, and this component will never destroy it.
        /// </summary>
        public void SetImageSprite(Sprite sprt)
        {
            Development.CheckPoint();

            if (_image != null && sprt != null)
            {
                _image.sprite = sprt;

                if (setNativeSize)
                    _image.SetNativeSize();
                if (preserveAspect)
                    _image.preserveAspect = true;

                ResizeImage(sprt.texture.width, sprt.texture.height);
                FadeIn();
            }
            else
            {
                Development.LogError("Image not shown: the RawImage/Image target or the created "
                    + "sprite is null after a successful download.");
                FailLoad("NULL Image or sprite value!");
                return;
            }

            onLoadCompleteEvent?.Invoke();
            onLoadCompleteEvent?.RemoveAllListeners();
            _isLoading = false;
        }

        private void ResizeImage(int width, int height)
        {
            if (useImageResizer)
            {
                ImageResizer imageResizer = transform.GetComponentInChildren<ImageResizer>();
                if (imageResizer == null)
                {
                    if (_image != null)
                        imageResizer = _image.gameObject.AddComponent<ImageResizer>();
                    else if (_rawImage != null)
                        imageResizer = _rawImage.gameObject.AddComponent<ImageResizer>();
                }

                if (imageResizer == null)
                {
                    // Was an unguarded dereference two lines down whenever neither _image nor _rawImage
                    // was assigned: nothing to attach the resizer to, so nothing to configure.
                    Development.LogError($"DownloadableImage on [{name}] has no Image or RawImage, so "
                        + "there is nowhere to put an ImageResizer. Not resizing.");
                    return;
                }

                // Resolved into a local. Assigning back to the serialized fitType meant Both survived
                // exactly one image - the first load rewrote the field, so every later image reused the
                // first one's orientation and the Inspector showed the rewritten value. Same defect as
                // TextureCacheLoader's, but this is the copy that ships on a prefab.
                FitType fit = fitType;

                if (fit == FitType.Both)
                {
                    // rect.size, not sizeDelta: on a stretch-anchored rect sizeDelta is an offset from
                    // the anchors and reads (0,0), which made this comparison 0 < 0 for every texture.
                    RectTransform selfRect = transform as RectTransform;
                    Vector2 container = selfRect != null ? selfRect.rect.size : Vector2.zero;

                    if (container.x <= 0f || container.y <= 0f)
                    {
                        Development.LogError($"DownloadableImage on [{name}] measures "
                            + $"{container.x}x{container.y}, so Both cannot be resolved. Fitting width.");
                        fit = FitType.Width;
                    }
                    else
                        fit = (container.x / width < container.y / height)
                            ? FitType.Width
                            : FitType.Height;
                }

                imageResizer.FitType = fit;
                imageResizer.UseMask = useMask;
                imageResizer.ForceCenterAnchor = centerAnchor;
                imageResizer.DoResize();
            }
        }

        public byte[] GetImageData()
        {
            if (_image != null)
                return _image.sprite.texture.EncodeToJPG();
            else if (_rawImage != null)
                return (_rawImage.texture as Texture2D).EncodeToJPG();
            else
                return null;
        }
    }
}