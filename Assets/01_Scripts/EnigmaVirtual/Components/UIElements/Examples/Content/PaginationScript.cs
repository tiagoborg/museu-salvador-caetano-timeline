using UnityEngine;
using UnityEngine.EventSystems;

namespace EnigmaVirtual.Components.UIElements
{
    public class PaginationScript : MonoBehaviour, IPointerClickHandler
    {
        public HorizontalScrollSnap hss;
        public int Page;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (hss != null)
            {
                hss.GoToScreen(Page);
            }
        }
    }
}