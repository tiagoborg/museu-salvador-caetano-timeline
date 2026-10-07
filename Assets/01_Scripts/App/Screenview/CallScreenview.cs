using UnityEngine;
using UnityEngine.UI;

public class CallScreenview : ScreenView
{
    [SerializeField]
    private Button startButton;

    private CallBackgroundAnimation _background;
    private bool _listening;

    public override void Show()
    {
        base.Show();

        // Show can arrive while already shown; a second listener would navigate twice per tap.
        if (!_listening)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
            _listening = true;
        }

        if (_background == null)
            _background = GetComponent<CallBackgroundAnimation>();
        if (_background != null)
            _background.Play();
    }

    public override void Hide()
    {
        if (_listening)
        {
            startButton.onClick.RemoveListener(OnStartButtonClicked);
            _listening = false;
        }

        if (_background != null)
            _background.Stop();

        base.Hide();
    }

    private void OnStartButtonClicked()
    {
        Controller.NavigateTo(new EnigmaVirtual.Screens.Navigation.NavigationItem(EnigmaVirtual.Screens.ScreenType.Home));
    }
}
