using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    /// <summary>
    /// Shows one cached texture in a <see cref="RawImage"/>, resizing the rect to the texture's aspect.
    /// </summary>
    /// <remarks>
    /// <b>The texture belongs to <see cref="TextureCacheManager"/>, not to this component.</b> One cached
    /// texture is handed to every loader that asks for the same url, so anything written *onto* the texture
    /// here is written for all of them. That is why there is no per-loader <c>filterMode</c> or mipmap
    /// setting: both are properties of the texture, so they cannot be per-instance while the texture is
    /// shared, and the pair that used to live here silently made the last loader to draw win.
    /// <para>
    /// Cached textures are therefore <b>Bilinear with no mipmaps</b> - what <c>UnityWebRequestTexture</c>
    /// produces - and there is no per-loader override. Import settings are not the answer either: these
    /// textures are built at runtime by <c>DownloadHandlerTexture</c> and never pass through the importer.
    /// A project needing a different filter mode has to set it where the cache takes ownership -
    /// <c>TextureCacheManager.SetTexture</c>, right after <c>DownloadHandlerTexture.GetContent</c>. Be
    /// clear-eyed that this means <b>editing shared framework code</b>: there is no setting for it, and
    /// adding one is a config-format change. It is the only place with a single owner, which is the whole
    /// reason it cannot live here. Per-instance *appearance* still belongs on the <see cref="RawImage"/>.
    /// </para>
    /// </remarks>
    public class TextureCacheLoader : MonoBehaviour
    {
        [SerializeField, ReadOnly]
        private string url;

        [Space]
        [Tooltip("Leave ON. When off, GetOrSetTexture2D hands back a texture that nothing owns and " +
                 "nothing ever destroys, so every load of an uncached image leaks one texture.")]
        [SerializeField]
        private bool cacheIfNotAvailable = true;
        [SerializeField]
        private RawImage rawImage;

        [Header("Effects")]
        [SerializeField]
        private bool useFadeIn = true;
        [SerializeField]
        private float fadeInSpeed = .25f;

        [Header("Image Resizer")]
        [SerializeField]
        private bool useImageResizer = true;
        [Tooltip("Both and Fill are the same here and both mean CONTAIN: the axis that runs out of room " +
                 "first is the one fitted, so the whole image is visible. Note ImageResizer reads the same " +
                 "enum as COVER - it refits when the image would leave a gap. Different components, same " +
                 "Inspector wording, opposite result.")]
        [SerializeField]
        private FitType fitType = FitType.Both;
        [SerializeField]
        private bool centerAnchor = false;

        [Space]
        [SerializeField]
        private bool useMask = false;
        [SerializeField, ConditionalField(nameof(useMask), false, true)]
        private bool showMaskGraphic = false;

        [Header("Deprecated")]
        [Tooltip("No longer applied, and removed next minor version. filterMode is a property of the " +
                 "TEXTURE, and the texture is shared with every other loader showing the same url - so a " +
                 "per-loader value never meant anything but 'last one to draw wins'. Kept only so the " +
                 "change is loud: a non-Bilinear value here logs an error at startup instead of silently " +
                 "becoming Bilinear. There is no setting for it: the only fix is to assign filterMode in " +
                 "TextureCacheManager.SetTexture, which is shared framework code (M2).")]
        [SerializeField]
        private FilterMode filterMode = FilterMode.Bilinear;

        private RectTransform selfRect;
        private Vector2 originalSizeDelta;
        private MaskableGraphic maskableGraphic;
        private bool isReady = false;
        private bool initAttempted = false;
        private Mask mask;
        private Image imageMask;

        /// <summary>
        /// One 2x2 transparent texture for the whole application, not one per <see cref="Clear"/>.
        /// The old code allocated a new <see cref="Texture2D"/> on every call and dropped the previous
        /// one unreferenced - textures are native objects, so nothing ever collected them.
        /// </summary>
        /// <remarks>
        /// <c>HideAndDontSave</c> so a scene load does not destroy it under the loaders still pointing at
        /// it. It is never in <see cref="TextureCacheManager"/>'s dictionary, so <c>ClearCache</c> leaves
        /// it alone too. One 2x2 texture leaks for the process lifetime by design; the alternative is
        /// reference-counting a placeholder, which is not worth it.
        /// </remarks>
        private static Texture2D transparentTexture;

        private static Texture2D TransparentTexture
        {
            get
            {
                if (transparentTexture == null)
                {
                    transparentTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    Color[] pixels = new Color[4];

                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = Color.clear;

                    transparentTexture.SetPixels(pixels);
                    transparentTexture.Apply();
                }

                return transparentTexture;
            }
        }

        private void Reset()
        {
            rawImage = GetComponentInChildren<RawImage>();
        }

        private void Awake()
        {
            // Two flags, not one. isReady says "usable"; initAttempted says "we already tried and said so",
            // which is what stops EnsureReady re-entering and re-logging the same authoring mistake on
            // every single Load() and Clear().
            if (initAttempted)
                return;

            initAttempted = true;

            // enabled = false stops Unity messages but does nothing about a public method call, so
            // isReady is what guards - not the enabled state.
            selfRect = transform as RectTransform;

            if (selfRect == null)
            {
                Development.LogError($"TextureCacheLoader on [{name}] is not on a RectTransform. "
                    + "It is a UI component. Disabled.");
                enabled = false;
                return;
            }

            if (rawImage == null)
            {
                // Was an NRE two lines later whenever useFadeIn was on, which is the default.
                Development.LogError($"TextureCacheLoader on [{name}] has no RawImage assigned. Disabled.");
                enabled = false;
                return;
            }

            if (filterMode != FilterMode.Bilinear)
                Development.LogError($"TextureCacheLoader on [{name}] has filterMode {filterMode}, which is "
                    + "no longer applied - cached textures are shared, so the setting could only ever mean "
                    + "'last loader to draw wins'. It renders Bilinear. There is no setting for this: the "
                    + "only fix is to assign filterMode in TextureCacheManager.SetTexture, which is shared "
                    + "framework code. This field is removed next minor version (M2).");

            // Only for ResetImageSize, which writes it straight back. It is NOT a size: on a
            // stretch-anchored rect sizeDelta is an offset from the anchors and is usually (0,0). The fit
            // maths uses selfRect.rect instead, read at resize time.
            originalSizeDelta = selfRect.sizeDelta;

            // RawImage derives from MaskableGraphic, so this is now unambiguously the RawImage. The old
            // GetComponent<MaskableGraphic>() returned the first one in serialization order, and a second
            // Graphic on the same object is legal - ApplyMask adds one - so it could pick the wrong target.
            maskableGraphic = rawImage;

            if (useFadeIn)
                SetAlpha(0f);

            imageMask = gameObject.GetComponent<Image>();
            mask = gameObject.GetComponent<Mask>();

            isReady = true;
        }

        public void Clear()
        {
            if (!EnsureReady())
                return;

            rawImage.texture = TransparentTexture;
        }

        private void ResetImageSize()
        {
            if (rawImage != null)
                rawImage.rectTransform.sizeDelta = originalSizeDelta;
        }

        // The MediaVO overload of the method below moved to App/Extensions/MediaVOExtensions.cs as an
        // extension method: MediaVO is a project type, and the framework naming it would be one of the
        // dependency cycles. Behaviour is unchanged - it null-checked, picked url or thumb, and called this
        // same primitive overload. Call sites need no edit, because extension methods in the global
        // namespace are in scope wherever App code is.
        /// <summary>
        /// Loads <paramref name="url"/>, falling back to <paramref name="fallbackUrl"/> when it is empty.
        /// </summary>
        /// <remarks>See <c>DownloadableImage.Load(string, string)</c> - same reasoning.</remarks>
        public void Load(string url, string fallbackUrl)
        {
            if (!string.IsNullOrEmpty(url))
            {
                Load(url);
                return;
            }

            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                Development.LogWarning("null or empty primary URL. Switching to the fallback URL!");
                Load(fallbackUrl);
                return;
            }

            Development.LogWarning("null or empty URL and fallback. Image loading aborted!");
        }

        public void Load(string url)
        {
            if (!EnsureReady())
                return;

            ResetImageSize();

            this.url = url;
            Core.main.TextureCacheManager.GetOrSetTexture2D(url, SetRawImageTexture, cacheIfNotAvailable);
        }

        /// <summary>
        /// Runs <see cref="Awake"/> if it has not run yet, and reports whether this loader is usable.
        /// Awake can fail (no RectTransform, no RawImage) and every public entry point has to respect
        /// that rather than run on half-initialised fields.
        /// </summary>
        private bool EnsureReady()
        {
            if (!initAttempted)
                Awake();

            return isReady;
        }

        private void SetRawImageTexture(Texture2D texture)
        {
            Development.CheckPoint();

            if (rawImage == null)
            {
                // `name` throws MissingReferenceException on a destroyed component rather than returning
                // fake-null, and this branch became reachable with one in v0.35.2: a cancelled request now
                // completes instead of vanishing, so the callback can arrive after its owner is gone. The
                // throw would have propagated into TextureCacheManager's notify loop and stranded every
                // request after this one.
                Development.LogError($"TextureCacheLoader on [{(this == null ? "<destroyed>" : name)}] "
                    + $"lost its RawImage before [{url}] arrived.");
                return;
            }

            if (texture == null)
            {
                // Blank rather than stale. The previous image stays on screen otherwise, at the size
                // ResetImageSize just restored - so the panel shows a different media item's picture,
                // wrongly scaled, and only the very first failure of this url was reported by name
                // (TextureCacheManager caches null as its known-bad marker, and every later request takes
                // the cache-hit branch straight to here).
                // Four causes, and each one reports itself further up: an empty url and a cache clear
                // both log from TextureCacheManager, so read the lines above this one before
                // assuming showErrorImage. A cancelled request arrives as null whatever showErrorImage
                // says, which is deliberate - it must not look like a successful load of a broken image.
                Development.LogError($"No texture for [{url}] - the url is empty, showErrorImage is off, "
                    + "the url is cached as known-bad, or a cache clear cancelled the download. Clearing "
                    + "the image rather than leaving the previous one on screen.");
                rawImage.texture = TransparentTexture;
                return;
            }

            // Nothing is written to the texture here. It is the cache's instance, shared with every other
            // loader showing the same url, and the two lines that used to be here - a filterMode assignment
            // and Apply() - were a full GPU re-upload on every load including cache hits, could not add
            // mipmaps to a texture that was not created with them, and threw outright on the error
            // placeholder, which is a non-readable Resources sprite (isReadable: 0). See the class remarks.
            rawImage.texture = texture;

            ResizeImage(texture);

            FadeIn();
        }

        private void SetAlpha(float alpha)
        {
            Color c = maskableGraphic.color;
            maskableGraphic.color = new Color(c.r, c.g, c.b, alpha);
        }

        private void FadeIn()
        {
            if (!useFadeIn)
            {
                SetAlpha(1f);
                return;
            }

            SetAlpha(0f);
            LeanTween.alpha(maskableGraphic.rectTransform, 1f, fadeInSpeed);
        }

        private void ResizeImage(Texture2D texture)
        {
            if (!useImageResizer)
                return;

            // No ImageResizer here. This used to find-or-AddComponent one and then call nothing on it -
            // all four calls were commented out - so it attached a component to the RawImage whose every
            // runtime path is a no-op (its Update only runs outside play mode, and both AutoUpdateSize and
            // resizeOnStart default off). The resize below is what has actually been doing the work.
            ResizeTexture(texture);
        }

        /// <summary>
        /// Sizes the RawImage's rect to <paramref name="texture"/>'s aspect, according to
        /// <c>fitType</c>.
        /// </summary>
        public void ResizeTexture(Texture2D texture)
        {
            // This is public, so it is an entry point in its own right and needs the same gate Load and
            // Clear have. Without it a call before Awake read selfRect as null and originalSizeDelta as
            // (0,0), and the Both/Fill maths below silently resolved to Height every time.
            if (!EnsureReady())
                return;

            if (rawImage == null || texture == null)
            {
                Development.LogError($"TextureCacheLoader on [{name}]: no "
                    + $"{(rawImage == null ? "RawImage" : "texture")} to resize.");
                return;
            }

            if (texture.width <= 0 || texture.height <= 0)
            {
                Development.LogError($"Texture for [{url}] reports {texture.width}x{texture.height}. Cannot resize.");
                return;
            }

            float aspectRatio = (float)texture.width / texture.height;

            // Resolved into a local. This used to assign back to the serialized fitType, so Both survived
            // exactly one image: the first load rewrote the field to Width or Height and every later load -
            // and the Inspector - saw that instead. Fill was worse; it had no case at all and resized
            // nothing.
            FitType fit = fitType;

            if (fit == FitType.Both || fit == FitType.Fill)
            {
                // rect.size, not sizeDelta. On a stretch-anchored rect - anchorMin != anchorMax, which is
                // the normal way to lay out a full-bleed image - sizeDelta is an offset from the anchors
                // and reads (0,0), so the comparison was 0 < 0 for every texture and always chose Height.
                Vector2 container = selfRect.rect.size;

                if (container.x <= 0f || container.y <= 0f)
                {
                    Development.LogError($"TextureCacheLoader on [{name}] measures "
                        + $"{container.x}x{container.y}, so {fitType} cannot be resolved. Fitting width.");
                    fit = FitType.Width;
                }
                else
                    fit = (container.x / texture.width < container.y / texture.height)
                        ? FitType.Width
                        : FitType.Height;
            }

            switch (fit)
            {
                case FitType.Width:
                    rawImage.rectTransform.sizeDelta = new Vector2(rawImage.rectTransform.sizeDelta.x, rawImage.rectTransform.sizeDelta.x / aspectRatio);
                    break;

                case FitType.Height:
                    rawImage.rectTransform.sizeDelta = new Vector2(rawImage.rectTransform.sizeDelta.y * aspectRatio, rawImage.rectTransform.sizeDelta.y);
                    break;
            }

            if (centerAnchor)
            {
                rawImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rawImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rawImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            ApplyMask();
        }

        private void ApplyMask()
        {
            if (!useMask)
            {
                DestroyIfExists(mask);
                DestroyIfExists(imageMask);
                return;
            }

            imageMask = gameObject.GetOrAddComponent<Image>();
            mask = gameObject.GetOrAddComponent<Mask>();
            mask.showMaskGraphic = showMaskGraphic;
        }

        private void DestroyIfExists(Object obj)
        {
            if (obj != null)
                Destroy(obj);
        }
    }
}
