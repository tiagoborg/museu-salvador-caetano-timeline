using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EnigmaVirtual.UI
{
    [RequireComponent(typeof(EventTrigger))]
    public class KeyboardKey : MonoBehaviour
    {
        [SerializeField]
        private string value;

        [Space]
        [SerializeField]
        private Button selectable;
        [SerializeField]
        private EventTrigger eventTrigger;
        [SerializeField]
        private KeyboardScript keyboard;

        [SerializeField]
        private bool isCaseUpdatable = true;
        [Space]
        [SerializeField]
        private Text text;
        [SerializeField]
        private Image image;
        [SerializeField]
        private Image background;

        [Space, SerializeField]
        private UnityEvent onPointerDown;
        [Space, SerializeField]
        private UnityEvent onPointerUp;

        [Space, SerializeField]
        private Color textNormalColor;
        [SerializeField]
        private Color textPressedColor;
        [SerializeField]
        private Color textDisabledColor;
        [SerializeField]
        private Color backgroundNormalColor;
        [SerializeField]
        private Color backgroundPressedColor;
        [SerializeField]
        private Color backgroundDisabledColor;

        private bool isDown = false;
        private bool isUpperCase = false;

        private void Reset()
        {
            if (keyboard == null)
                keyboard = gameObject.GetComponentInParent(typeof(KeyboardScript)) as KeyboardScript;
            if (selectable == null)
                selectable = GetComponent<Button>();
            if (text == null)
                text = transform.Find("Label").GetComponent<Text>();
            if (eventTrigger == null)
                eventTrigger = GetComponent<EventTrigger>();
            if (image == null)
                image = transform.Find("Image").GetComponent<Image>();
            if (background == null)
                background = transform.Find("BackgroundImage").GetComponent<Image>();
        }

        public void SetupKey(KeyboardKeySettings keySettings)
        {
            if (keySettings == null)
                return;

            if (text)
            {
                text.text = value;
                text.font = keySettings.font;
                text.fontSize = keySettings.fontSize;
                text.fontStyle = keySettings.fontStyle;
                text.resizeTextForBestFit = keySettings.resizeTextBestFit;
                text.resizeTextMaxSize = keySettings.fontMaxSize;
                text.resizeTextMinSize = keySettings.fontMinSize;

                if (keySettings.upperCaseKeyboard)
                    ToUpperCase();
                else
                    ToLowerCase();
            }

            textNormalColor = keySettings.textNormalColor;
            textPressedColor = keySettings.textPressedColor;
            textDisabledColor = keySettings.textDisabledColor;
            backgroundNormalColor = keySettings.backgroundNormalColor;
            backgroundPressedColor = keySettings.backgroundPressedColor;
            backgroundDisabledColor = keySettings.backgroundDisabledColor;

            if (background)
            {
                background.gameObject.SetActive(keySettings.useBackgroundImage);
                background.sprite = keySettings.backgroundSprite;
                background.type = Image.Type.Sliced;
            }
        }

        private void Start()
        {
            EventTrigger.Entry downEntry = new EventTrigger.Entry();
            downEntry.eventID = EventTriggerType.PointerDown;
            downEntry.callback.AddListener((eventData) => { OnPointerDown(); });
            eventTrigger.triggers.Add(downEntry);

            EventTrigger.Entry upEntry = new EventTrigger.Entry();
            upEntry.eventID = EventTriggerType.PointerUp;
            upEntry.callback.AddListener((eventData) => { OnPointerUp(); });
            eventTrigger.triggers.Add(upEntry);

            text.text = value;

            if (isUpperCase)
                ToUpperCase();
            else
                ToLowerCase();
        }

        private void Update()
        {
            if (!selectable.interactable)
            {
                if (text)
                    text.color = textDisabledColor;
                if (image)
                    image.color = textDisabledColor;
                if (background)
                    background.color = backgroundDisabledColor;
            }
            else if (selectable.interactable)
            {
                if (isDown)
                {
                    if (text)
                        text.color = textPressedColor;
                    if (image)
                        image.color = textPressedColor;
                    if (background)
                        background.color = backgroundPressedColor;
                }
                else
                {
                    if (text)
                        text.color = textNormalColor;
                    if (image)
                        image.color = textNormalColor;
                    if (background)
                        background.color = backgroundNormalColor;
                }
            }
        }

        private void OnPointerDown()
        {
            isDown = true;
            onPointerDown?.Invoke();
        }

        private void OnPointerUp()
        {
            isDown = false;
            onPointerUp?.Invoke();
        }

        public void AlphabetFunction()
        {
            keyboard.OutputAlphaNumeric(text.text);
        }

        public void BackSpace()
        {
            keyboard.OutputBackSpace();
        }

        public void KeyboardCaseToggle()
        {
            keyboard.CaseToggle();
        }

        public void KeyboardToUpperCase()
        {
            keyboard.SwitchToUpperCase();
        }

        public void KeyboardToLowerCase()
        {
            keyboard.SwithToLowerCase();
        }

        public void ToUpperCase()
        {
            if (!isCaseUpdatable || string.IsNullOrEmpty(value) || System.Char.IsNumber(value[0]))
                return;

            text.text = value.ToUpper();
            isUpperCase = true;
        }

        public void ToLowerCase()
        {
            if (!isCaseUpdatable || string.IsNullOrEmpty(value) || System.Char.IsNumber(value[0]))
                return;

            text.text = value.ToLower();
            isUpperCase = false;
        }
    }
}
