
namespace EnigmaVirtual
{
    [System.Serializable]
    public class PhidgetsSettings
    {
        public bool isActive = false;
        public PhidgetSettings[] phidgets = new PhidgetSettings[0];
    }
}
