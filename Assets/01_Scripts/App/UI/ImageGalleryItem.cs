using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ImageGalleryItem : MonoBehaviour
{
    [SerializeField] private DownloadableImage image;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private Button button;

    public UnityEvent<MediaVO> onMediaClickEvent;

    private MediaVO item;

    void Start()
    {
        if (!Core.IsSet)
            return;

        Core.LanguageChangedEvent += OnLanguageChanged;
        button.onClick.AddListener(OnButtonClicked);
    }

    private void OnDestroy()
    {
        // Both detaches run unconditionally now; Core.IsSet gated them together. A stale instance of
        // this one throws rather than no-ops - `item` is a MediaVO and survives destruction, so the
        // handler reaches `labelText.text` on a destroyed component - and that throw aborts the rest of
        // the delegate chain, leaving every later subscriber on the old language.
        Core.LanguageChangedEvent -= OnLanguageChanged;

        // The button is a different question from Core: it may already be destroyed, and Unity's
        // overloaded == is what answers that.
        if (button != null)
            button.onClick.RemoveListener(OnButtonClicked);
    }

    private void OnButtonClicked()
    {
        onMediaClickEvent?.Invoke(item);
    }

    private void OnLanguageChanged()
    {
        if (item == null)
            return;

        if (item.title != null)
        {
            // fallToDefault, then a guard. GetLocalized returns null for a language the entry does not
            // carry, and TMP accepts null without complaint - so this used to leave the label blank for
            // the rest of the session with nothing logged and nothing thrown. Falling back to the
            // default language shows the content that does exist; the guard covers an entry that has no
            // languages at all.
            string label = item.title.GetLocalized(fallToDefault: true);

            if (label == null)
            {
                Development.LogWarning($"Media item [{item.id}] has a title with no value in any "
                    + "language. Showing an empty label.");
                label = string.Empty;
            }

            labelText.text = label;
        }
    }

    public void SetData(MediaVO item)
    {
        this.item = item;

        if (item == null)
            return;

        OnLanguageChanged();
        image.Load(item, true);
    }
}
