using EnigmaVirtual;
using TMPro;

public static class TextMeshProUGUIExtension
{
    public static void SetText(this TextMeshProUGUI textMeshProUGUI, LocalizedField<string> values, MultiLangText.TextCase textCase = MultiLangText.TextCase.None, bool bold = false, bool italic = false, bool underlined = false, bool strikethrough = false)
    {
        if (values == null)
        {
            textMeshProUGUI.text = string.Empty;
            Development.LogError("Null LocalizedField!");
            return;
        }

        if (values.TryGetLocalized(out string translation))
            textMeshProUGUI.text = translation.ConvertTextTagsForUnity();
        else
            textMeshProUGUI.text = string.Empty;

        if (!string.IsNullOrEmpty(textMeshProUGUI.text))
        {
            if (bold)
                textMeshProUGUI.text = textMeshProUGUI.text.Bold();
            if (italic)
                textMeshProUGUI.text = textMeshProUGUI.text.Italics();
            if (underlined)
                textMeshProUGUI.text = textMeshProUGUI.text.Underlined();
            if (strikethrough)
                textMeshProUGUI.text = textMeshProUGUI.text.Strikethrough();

            if (textCase == MultiLangText.TextCase.UpperCase)
                textMeshProUGUI.text = textMeshProUGUI.text.ToUpper();
            else if (textCase == MultiLangText.TextCase.LowerCase)
                textMeshProUGUI.text = textMeshProUGUI.text.ToLower();
        }
    }

    public static bool isTextOverflowing(this TextMeshProUGUI textMeshProUGUI)
    {
        float preferredHeight = textMeshProUGUI.preferredHeight;
        float actualHeight = textMeshProUGUI.rectTransform.rect.height;

        return preferredHeight > actualHeight;
    }
}