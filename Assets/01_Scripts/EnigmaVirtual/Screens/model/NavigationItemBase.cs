namespace EnigmaVirtual.Screens.Navigation
{
    [System.Serializable]
    public abstract class NavigationItemBase
    {
        public ScreenType screenType;
        public int id;
        public object data;

        public NavigationItemBase(ScreenType screenType, object data = null)
        {
            this.screenType = screenType;
            this.data = data;
        }
    }
}