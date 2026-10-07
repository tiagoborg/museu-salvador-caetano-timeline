using EnigmaVirtual;
using EnigmaVirtual.Screens;
using EnigmaVirtual.Screens.Navigation;
using System.Linq;

public class ScreenController : ScreenControllerBase
{
    /// <summary>
    /// Hands <c>navItem.data</c> to the target screen before it is shown. The base controller never
    /// passes data on; the timeline needs it to know which categories Home selected.
    /// </summary>
    public override void NavigateTo(NavigationItem navItem)
    {
        if (navItem != null && navItem.data != null)
        {
            ScreenViewBase target = screens.FirstOrDefault(screen => screen != null && screen.GetScreenType() == navItem.screenType);
            if (target != null)
                target.SetData(navItem.data);
        }

        base.NavigateTo(navItem);
    }

    //public override void NavigateTo(NavigationItem navItem)
    //{
    //    if (_currentScreen != null)
    //    {
    //        _currentScreen.Hide();
    //        _currentScreen = null;
    //    }

    //    _currentScreen = screens.FirstOrDefault(screen => screen.GetScreenType() == navItem.screenType);

    //    if (_currentScreen != null)
    //    {
    //        navigationHistory.Push(navItem.screenType, navItem.data);

    // KEPT: template scaffolding in App/, which is the folder a derived project edits. It
    // shows how to wire navigation per ScreenType and is meant to be uncommented, not removed.
    //        switch (navItem.screenType)
    //        {
    //            case ScreenType.MenuType1:
    //                _currentScreen.SetData((DATATYPE)navItem.data);
    //                break;
    //            case ScreenType.None:
    //            case ScreenType.Home:
    //            case ScreenType.Call:
    //                navigationHistory.ClearQueue();
    //                break;
    //        }
    //    }

    //    if (_currentScreen != null)
    //        _currentScreen.Show();
    //    else
    //    {
    //        Development.LogIf(Core.settings.System.doLogs, $"ScreenView [{navItem.screenType}] not found!", UnityEngine.LogType.Warning);

    //        if (navItem.screenType != startScreen)
    //            NavigateToStartScreen();
    //    }
    //}
}
