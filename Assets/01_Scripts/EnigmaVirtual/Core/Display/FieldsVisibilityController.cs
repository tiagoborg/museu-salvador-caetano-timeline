using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class FieldsVisibilityController : MonoBehaviour
    {
        [SerializeField]
        private Sprite openSprite;
        [SerializeField]
        private Sprite closedSprite;
        [SerializeField]
        private Image buttonImage = null;

        private RectTransform contentHolder;
        private SettingInput[] si;
        private bool isVisible = true;

        void Awake()
        {
            contentHolder = transform.parent.GetComponent<RectTransform>();
            si = gameObject.GetComponentsInChildren<SettingInput>();
        }

        void Start()
        {
            SetButtonImage();
            // No handle: Start runs once, so nothing can stack a second one.
            this.CallLater(0.2f, OnButtonClick);
        }

        public void OnButtonClick()
        {
            isVisible = !isVisible;
            for (int i = 0; i < si.Length; i++)
                si[i].transform.parent.gameObject.SetActive(isVisible);

            SetButtonImage();

            StartCoroutine(ForceRebuild());
        }

        private void SetButtonImage()
        {
            if (isVisible)
                buttonImage.sprite = closedSprite;
            else
                buttonImage.sprite = openSprite;
        }

        private IEnumerator ForceRebuild()
        {
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentHolder);
        }
    }
}
