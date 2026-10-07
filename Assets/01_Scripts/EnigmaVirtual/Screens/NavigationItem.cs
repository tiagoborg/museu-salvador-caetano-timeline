namespace EnigmaVirtual.Screens.Navigation
{
    [System.Serializable]
    public class NavigationItem : NavigationItemBase
    {
        public NavigationItem(ScreenType screenType, object data = null) : base(screenType, data) { }
    }
}