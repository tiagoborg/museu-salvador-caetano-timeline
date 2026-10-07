using static EnigmaVirtual.Notifications.NotificationManager;

namespace EnigmaVirtual.Notifications
{
    [System.Serializable]
    public class NotificationVO
    {
        public NotificationType notificationType;
        public string title;
        public string description;

        public int count = 1;

        public void IncreaseCount() { ++count; }

        public bool IsDuplicate(NotificationVO notificationVO)
        {
            return this.notificationType == notificationVO.notificationType && this.title.ToUpper() == notificationVO.title.ToUpper() && this.description.ToUpper() == notificationVO.description.ToUpper();
        }
    }
}