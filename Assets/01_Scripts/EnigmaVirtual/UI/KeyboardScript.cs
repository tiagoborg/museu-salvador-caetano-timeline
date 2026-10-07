using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual.UI
{
    /// <summary>
    /// Before using unpack prefab!!
    /// </summary>
    public class KeyboardScript : MonoBehaviour
    {
        public enum LayoutLanguage { PT, EN, RU }

        [SerializeField]
        private TMP_InputField textField;
        [SerializeField]
        private bool textFieldInteractable = true;
        [SerializeField, Tooltip("Disables textfield caret and selection color.")]
        private bool fakeDisabledInteraction = false;

        private KeyboardLayout[] layouts;

        [SerializeField]
        private LayoutLanguage currentLayoutLanguage = LayoutLanguage.PT;

        [Space]
        [Header("KEYS SETTINGS")]
        [SerializeField]
        private Font font;
        [SerializeField]
        private int fontSize;
        [SerializeField]
        private FontStyle fontStyle;
        [SerializeField]
        private bool fontResizeTextBestFit;
        [SerializeField]
        private int fontMinSize = 1;
        [SerializeField]
        private int fontMaxSize = 10;
        [SerializeField]
        private bool upperCaseKeyboard = false;
        [Header("Text Color")]
        [SerializeField]
        private Color textNormalColor;
        [SerializeField]
        private Color textPressedColor;
        [SerializeField]
        private Color textDisabledColor;

        [Space(10)]
        [Header("Background Image")]
        [SerializeField]
        private bool useBackgroundImage = true;
        [SerializeField]
        private Sprite backgroundSprite;
        [SerializeField]
        private Color backImgNormalColor;
        [SerializeField]
        private Color backImgPressedColor;
        [SerializeField]
        private Color backImgDisabledColor;

        // stores keyboard keys
        private KeyboardKey[] keyboardKeys;

        [ContextMenu("Update Keyboard")]
        public void UpdateAll()
        {
            keyboardKeys = GetComponentsInChildren<KeyboardKey>();

            KeyboardKeySettings keySettings = new KeyboardKeySettings();
            keySettings.font = font;
            keySettings.fontSize = fontSize;
            keySettings.fontMinSize = fontMinSize;
            keySettings.fontMaxSize = fontMaxSize;
            keySettings.fontStyle = fontStyle;
            keySettings.resizeTextBestFit = fontResizeTextBestFit;
            keySettings.textNormalColor = textNormalColor;
            keySettings.textPressedColor = textPressedColor;
            keySettings.textDisabledColor = textDisabledColor;
            keySettings.backgroundDisabledColor = backImgDisabledColor;
            keySettings.backgroundNormalColor = backImgNormalColor;
            keySettings.backgroundPressedColor = backImgPressedColor;
            keySettings.backgroundSprite = backgroundSprite;
            keySettings.useBackgroundImage = useBackgroundImage;
            keySettings.upperCaseKeyboard = upperCaseKeyboard;

            foreach (KeyboardKey key in keyboardKeys)
                key.SetupKey(keySettings);
        }

        public void SetInputField(TMP_InputField textField)
        {
            if (textField == null)
            {
                Development.LogWarning("InputField reference not set to an instance of object!");
                return;
            }

            this.textField = textField;

            textField.interactable = textFieldInteractable;

            if (fakeDisabledInteraction)
            {
                textField.customCaretColor = true;
                Color tempCol = Color.white;
                tempCol.a = 0;
                textField.caretColor = tempCol;
                textField.selectionColor = tempCol;
            }

            if (!textFieldInteractable)
            {
                ColorBlock temp = textField.colors;
                temp.disabledColor = temp.normalColor;
                textField.colors = temp;
            }
        }

        private void Start()
        {
            keyboardKeys = GetComponentsInChildren<KeyboardKey>();

            // garantes all letters are in same case
            if (upperCaseKeyboard)
                SwitchToUpperCase();
            else
                SwithToLowerCase();

            SetInputField(textField);
        }

        public void SwitchToUpperCase()
        {
            foreach (KeyboardKey key in keyboardKeys)
                key.ToUpperCase();

            upperCaseKeyboard = true;
        }

        public void SwithToLowerCase()
        {
            foreach (KeyboardKey key in keyboardKeys)
                key.ToLowerCase();

            upperCaseKeyboard = false;
        }

        public void CaseToggle()
        {
            if (upperCaseKeyboard)
                SwithToLowerCase();
            else
                SwitchToUpperCase();
        }

        public void OutputAlphaNumeric(string alphabet)
        {
            if (textField)
                textField.text += alphabet;
        }

        public void OutputBackSpace()
        {
            if (textField && textField.text.Length > 0)
            {
                if (textField.text.EndsWith("gmail.com", System.StringComparison.OrdinalIgnoreCase) || textField.text.EndsWith("hotmail.com", System.StringComparison.OrdinalIgnoreCase))
                    textField.text = textField.text.Remove(textField.text.Length - 3);
                else if (textField.text.EndsWith("gmail.", System.StringComparison.OrdinalIgnoreCase))
                    textField.text = textField.text.Remove(textField.text.Length - 6);
                else if (textField.text.EndsWith("hotmail.", System.StringComparison.OrdinalIgnoreCase))
                    textField.text = textField.text.Remove(textField.text.Length - 8);
                else
                    textField.text = textField.text.Remove(textField.text.Length - 1);
            }
        }

        public void CloseAllLayouts()
        {
            for (int i = 0; i < layouts.Length; i++)
                layouts[i].DisablableKeyboard();
        }

        public void ShowLayout(LayoutLanguage layoutLanguage)
        {
            if (currentLayoutLanguage == layoutLanguage)
                return;

            CloseAllLayouts();

            for (int i = 0; i < layouts.Length; i++)
            {
                if (layouts[i].language == layoutLanguage)
                {
                    layouts[i].EnableKeyboard();
                    return;
                }
            }
        }
    }
}