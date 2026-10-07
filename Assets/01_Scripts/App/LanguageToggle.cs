using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanguageToggle : MonoBehaviour
{
    [SerializeField]
    private Toggle toggle;
    [SerializeField]
    private Langs _language;

    [Space]
    [SerializeField]
    private TextMeshProUGUI labelText;
    [SerializeField]
    private bool useLabelColors = false;
    [SerializeField]
    private Color labelNormalColor = Color.white;
    [SerializeField]
    private Color labelSelectedColor = Color.white;

    public Langs language => _language;

    private void Awake()
    {
        // Try to auto-assign common references to avoid editor setup mistakes
        if (toggle == null)
            toggle = GetComponent<Toggle>();

        if (labelText == null)
            labelText = GetComponentInChildren<TextMeshProUGUI>();
    }

    private void Start()
    {
        if (!Core.IsSet || toggle == null)
            return;

        toggle.onValueChanged.AddListener(OnToggleValueChanged);
    }

    private void OnDestroy()
    {
        if (toggle == null)
            return;

        toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
    }

    private void OnToggleValueChanged(bool arg0)
    {
        if (arg0)
            Core.ChangeCurrentLang(language);

        ToggleObjects(arg0);
    }

    private void ToggleObjects(bool value)
    {
        if (useLabelColors && labelText != null)
            labelText.color = value ? labelSelectedColor : labelNormalColor;

        if (toggle != null)
            toggle.interactable = !value;
    }

    public void SetData(Langs language, ToggleGroup group, bool isActive)
    {
        if (toggle != null)
            toggle.group = group;

        this._language = language;

        if (labelText != null)
            labelText.text = language.ToString().ToUpperInvariant();

        ToggleObjects(isActive);
    }

    public void SetToggleActive()
    {
        if (toggle == null)
            return;

        // Set without triggering onValueChanged to avoid unwanted callbacks
        toggle.SetIsOnWithoutNotify(true);
        ToggleObjects(true);
    }
}
