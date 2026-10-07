using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual.Components.UIElements
{
    [RequireComponent(typeof(Toggle))]
    public class PaginationToggle : MonoBehaviour
    {
        [SerializeField]
        private Text pageNumberText = null;
        [SerializeField]
        private bool alphaImage = false;
        [SerializeField]
        private float defaultAlpha = 1;
        [SerializeField]
        private float selectedAlpha = 0.7f;
        [SerializeField]
        private Sprite defaultSprite = null;
        [SerializeField]
        private Sprite selectedSprite = null;
        [SerializeField]
        private Image image = null;
        [SerializeField]
        private Toggle toggle = null;

        public bool isOn 
        { 
            get { 
                return toggle.isOn; 
            } 
            set { 
                SetState(value); 
            } 
        }

        private void Awake()
        {
            if (toggle == null)
                toggle = GetComponent<Toggle>();
            if (image == null)
                image = GetComponent<Image>();
        }

        private void Reset()
        {
            Awake();
        }

        public void Start()
        {
            if (toggle == null)
            {
                Development.LogWarning("Toggle component missing!");
                return;
            }

            SetState(toggle.isOn);
            toggle.onValueChanged.AddListener(SetState);
        }

        private void SetState(bool state)
        {
            toggle.isOn = state;

            if (selectedSprite != null && defaultSprite != null)
                image.sprite = state ? selectedSprite : defaultSprite;

            if (!alphaImage)
                return;

            Color temp = image.color;
            temp.a = state ? selectedAlpha : defaultAlpha;
            image.color = temp;
        }

        public void SetPageNumber(int page)
        {
            if (pageNumberText != null)
                pageNumberText.text = page.ToString();
            else
                Development.LogWarning("Text component missing!");
        }
    }
}