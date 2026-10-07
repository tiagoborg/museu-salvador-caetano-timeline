using UnityEngine;
using UnityEngine.UI;

public class CallScreenview : ScreenView
{
    [SerializeField]
    private Button startButton;

    public override void Show()
    {
        base.Show();
        startButton.onClick.AddListener(OnStartButtonClicked);
    }

    public override void Hide()
    {
        startButton.onClick.RemoveListener(OnStartButtonClicked);
        base.Hide();
    }

    private void OnStartButtonClicked()
    {
        Controller.NavigateTo(new EnigmaVirtual.Screens.Navigation.NavigationItem(EnigmaVirtual.Screens.ScreenType.Home));
    }
}
