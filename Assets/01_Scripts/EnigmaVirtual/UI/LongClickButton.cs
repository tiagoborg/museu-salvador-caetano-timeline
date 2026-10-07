using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class LongClickButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Settings")]
        [SerializeField]
        private float requiredHoldTime;
        [SerializeField]
        private Image fillImage;

        [Header("Events")]
        public UnityEvent onButtonPress;
        public UnityEvent onButtonRelease;
        public UnityEvent onLongClickComplete;

        private bool isPointerDown;
        private float pointerDownTimer;

        private void Start()
        {
            Reset();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPointerDown = true;
            onButtonPress?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            onButtonRelease?.Invoke();
            Reset();
        }

        private void Update()
        {
            if (isPointerDown)
            {
                pointerDownTimer += Time.deltaTime;
                if (pointerDownTimer > requiredHoldTime)
                {
                    onLongClickComplete?.Invoke();
                    Reset();
                }
                UpdateFillImage(pointerDownTimer / requiredHoldTime);
            }
        }

        private void Reset()
        {
            isPointerDown = false;
            pointerDownTimer = 0;
            UpdateFillImage(0);
        }

        private void UpdateFillImage(float value)
        {
            if (fillImage != null)
                fillImage.fillAmount = pointerDownTimer / requiredHoldTime;
        }
    }
}