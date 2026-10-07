using UnityEngine;

namespace EnigmaVirtual.UI
{
    [System.Serializable]
    public class KeyboardKeySettings
    {
        public Font font;
        public int fontSize;
        public FontStyle fontStyle;
        public bool resizeTextBestFit = false;
        public int fontMaxSize = 1;
        public int fontMinSize = 1;
        public Color textNormalColor;
        public Color textPressedColor;
        public Color textDisabledColor;
        public Color backgroundNormalColor;
        public Color backgroundPressedColor;
        public Color backgroundDisabledColor;
        public Sprite backgroundSprite;
        public bool useBackgroundImage;
        public bool upperCaseKeyboard = true;
    }
}
