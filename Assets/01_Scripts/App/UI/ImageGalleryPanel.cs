using EnigmaVirtual;
using EnigmaVirtual.Components.UIElements;
using UnityEngine;
using UnityEngine.Events;

public class ImageGalleryPanel : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField] private HorizontalScrollSnap scrollSnap;
    [Space, SerializeField] private bool showPagination;
    [Space, Separator]
    public UnityEvent<MediaVO> onMediaClickEvent;

    private void Awake()
    {
        if (!showPagination)
            scrollSnap.Pagination = null;
    }

    public void ResetPosition()
    {
        scrollSnap.GoToScreen(0);
    }

    public void SetData(MediaVO[] medias)
    {
        ClearMedias();

        if (medias.IsNullOrEmpty())
        {
            Development.LogWarning("null or empty MediaVO list. Ignoring image gallery construction!");
            return;
        }

        for (int i = 0; i < medias.Length; i++)
        {
            GameObject go = Instantiate(prefab);
            ImageGalleryItem ip = go.GetComponent<ImageGalleryItem>();
            ip.SetData(medias[i]);
            ip.onMediaClickEvent.AddListener((MediaVO obj) => onMediaClickEvent?.Invoke(obj));
            scrollSnap.AddChild(go);
        }
    }

    private void ClearMedias()
    {
        scrollSnap.RemoveAllChildren(out GameObject[] gos);
        foreach (GameObject go in gos)
        {
            go.GetComponent<ImageGalleryItem>().onMediaClickEvent.RemoveAllListeners();
            Destroy(go);
        }

        if (scrollSnap.Pagination == null)
            return;

        foreach (Transform trans in scrollSnap.Pagination.transform)
            Destroy(trans.gameObject);
    }
}
