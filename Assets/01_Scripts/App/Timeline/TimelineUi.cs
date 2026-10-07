using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Look shared by the Home category list and the timeline. Assigned in the Inspector.</summary>
[System.Serializable]
public class TimelineStyle
{
    public TMP_FontAsset boldFont;
    public TMP_FontAsset regularFont;

    [Tooltip("Outline used for cards, category tags and the axis markers. Drawn sliced when it has borders.")]
    public Sprite frameSprite;
    public float framePixelsPerUnitMultiplier = 2f;
    [Tooltip("A LEFT-pointing arrow. Used as-is on back buttons and rotated to point up-right on Home.")]
    public Sprite arrowSprite;

    public Color textColor = Color.white;
    public Color lineColor = new Color(0.42f, 0.42f, 0.42f, 1f);
    public Color frameColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color accentColor = new Color(0.16f, 0.65f, 0.87f, 1f);
    [Tooltip("The design's charcoal #191919: card text sections, category tags and the inside of the axis markers.")]
    public Color backgroundColor = new Color32(0x19, 0x19, 0x19, 0xFF);
    public Color dotActiveColor = Color.white;
    public Color dotInactiveColor = new Color(1f, 1f, 1f, 0.3f);

    // Generated at runtime by TimelineScreenview, which owns and destroys them. Null means squares.
    [System.NonSerialized] public Sprite discSprite;
    [System.NonSerialized] public Sprite ringSprite;
}

/// <summary>Small builders for the UI the timeline creates at runtime.</summary>
public static class TimelineUi
{
    public static RectTransform Rect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Places a rect in reference pixels measured from its parent's top-left corner.</summary>
    public static void TopLeft(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    public static Image MakeImage(Transform parent, string name, Sprite sprite, Color color, bool raycastTarget,
        float pixelsPerUnitMultiplier = 1f)
    {
        Image image = Rect(parent, name).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycastTarget;

        if (sprite != null && sprite.border != Vector4.zero)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        }

        return image;
    }

    public static TextMeshProUGUI MakeText(Transform parent, string name, TMP_FontAsset font, float size, Color color,
        TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();

        // Null keeps TMP's default font, which is better than invisible text when the Inspector field
        // was left empty - and it is reported once by whoever owns the style.
        if (font != null)
            text.font = font;

        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.text = string.Empty;
        return text;
    }

    /// <summary>
    /// An anti-aliased circle, white unless <paramref name="stroke"/> is given: filled when
    /// <paramref name="ringFraction"/> is 0, otherwise a ring whose stroke is that fraction of the radius.
    /// A ring's inside is transparent unless <paramref name="interior"/> is given, in which case it is
    /// filled with that colour. An Image tint multiplies every pixel, so a sprite with baked colours
    /// should be drawn with a white Image colour to show them exactly. The caller owns the sprite AND
    /// its texture and must destroy both - see <see cref="DestroySprite"/>.
    /// </summary>
    public static Sprite MakeCircleSprite(int size, float ringFraction, Color? interior = null, Color? stroke = null)
    {
        Color strokeColor = stroke ?? Color.white;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        float radius = size * 0.5f;
        float inner = radius * (1f - ringFraction);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                // One pixel of edge softening on each boundary.
                float outer = Mathf.Clamp01(radius - distance);
                float ring = ringFraction > 0f ? Mathf.Clamp01(distance - inner) : 1f;

                Color pixel = strokeColor;
                if (interior.HasValue)
                {
                    // The inner edge blends ring into interior instead of into transparency.
                    pixel = Color.Lerp(interior.Value, strokeColor, ring);
                    pixel.a = outer * Mathf.Lerp(interior.Value.a, strokeColor.a, ring);
                }
                else
                {
                    pixel.a = outer * ring * strokeColor.a;
                }

                pixels[y * size + x] = pixel;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    public static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        // Destroy is refused outside Play mode, where editor tooling and self-checks run.
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(sprite.texture);
            UnityEngine.Object.DestroyImmediate(sprite);
        }
    }

    /// <summary>A UI string from the <c>texts</c> block, falling back to the default language.</summary>
    public static string Translate(string key)
    {
        if (!Core.IsSet || Core.main == null || Core.main.AppDataController == null)
            return key;

        return Core.main.AppDataController.Translations.GetTranslation(key, null, true);
    }

    /// <summary>Never null: TMP must not be handed a null string.</summary>
    public static string Localized(LocalizedField<string> field)
    {
        if (field == null)
            return string.Empty;

        return field.GetLocalized(fallToDefault: true) ?? string.Empty;
    }

    public static void ReportMissingStyle(UnityEngine.Object owner, TimelineStyle style)
    {
        if (style == null || style.boldFont == null || style.regularFont == null || style.frameSprite == null
            || style.arrowSprite == null)
        {
            Development.LogWarning($"{owner.name}: TimelineStyle has empty fields (fonts, frame or arrow "
                + "sprite). The UI is still built, with TMP's default font and plain rectangles.");
        }
    }
}
