using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static EnigmaVirtual.Notifications.NotificationManager;

namespace EnigmaVirtual.Notifications
{
    public class NotificationPrefab : PanelBase
    {
        public event Action<GameObject> OnCloseClickEvent;

        [SerializeField]
        private TextMeshProUGUI countText;
        [SerializeField]
        private TextMeshProUGUI timeText;
        [SerializeField]
        private TextMeshProUGUI titleText;
        [SerializeField]
        private TextMeshProUGUI descriptionText;
        [SerializeField]
        private Image backgroundImage;
        [SerializeField]
        private Button closeButton;
        [SerializeField]
        private Image iconImage;

        [Space, SerializeField]
        private Timer timer;

        [Space, Header("Icons")]
        [SerializeField]
        private Sprite successIcon;
        [SerializeField]
        private Sprite errorIcon;
        [SerializeField]
        private Sprite warningIcon;
        [SerializeField]
        private Sprite infoIcon;

        [Space, Header("Box Colors")]
        [SerializeField]
        private Color successColor;
        [SerializeField]
        private Color errorColor;
        [SerializeField]
        private Color warningColor;
        [SerializeField]
        private Color infoColor;

        private NotificationsSettings settings;
        private NotificationVO notificationVO;

        public void OnDestroy()
        {
            timer.onTimerCompleteEvent.RemoveAllListeners();
            closeButton.onClick.RemoveAllListeners();
        }

        public override void Hide(bool animate = true, Action callback = null)
        {
            base.Hide(animate, () => OnCloseClickEvent?.Invoke(gameObject));
        }

        public NotificationType notificationType { get { return notificationVO.notificationType; } }

        public void SetData(NotificationVO notificationVO)
        {
            this.notificationVO = notificationVO;
            timeText.text = DateTime.Now.ToString();
            titleText.text = notificationVO.title;
            descriptionText.text = notificationVO.description;

            if (notificationVO.count > 1)
            {
                countText.text = notificationVO.count > 999 ? "999" : notificationVO.count.ToString();
            }

            SetState(notificationVO.notificationType);
            SetTimer(notificationVO.notificationType);

            closeButton.onClick.AddListener(OnButtonClickEvent);

            Show();
        }

        public bool IsDuplicate(NotificationVO notificationVO)
        {
            return this.notificationVO.IsDuplicate(notificationVO);
        }

        private void OnButtonClickEvent()
        {
            closeButton.onClick.RemoveListener(OnButtonClickEvent);
            Hide();
        }

        private void SetState(NotificationType notificationType)
        {
            switch (notificationType)
            {
                case NotificationType.Success:
                    backgroundImage.color = successColor;
                    iconImage.sprite = successIcon;
                    break;
                case NotificationType.Error:
                    backgroundImage.color = errorColor;
                    iconImage.sprite = errorIcon;
                    break;
                case NotificationType.Warning:
                    backgroundImage.color = warningColor;
                    iconImage.sprite = warningIcon;
                    break;
                case NotificationType.Info:
                    backgroundImage.color = infoColor;
                    iconImage.sprite = infoIcon;
                    break;
            }
        }

        private void SetTimer(NotificationType notificationType)
        {
            settings = Core.IsSet ? Core.settings.DebugSettings.Notifications : new NotificationsSettings();

            timer.onTimerCompleteEvent.AddListener(OnTimerComplete);

            switch (notificationType)
            {
                case NotificationType.Success:
                    if (settings.successTimeout > 0)
                        timer.StartTimer(settings.successTimeout);
                    break;
                case NotificationType.Error:
                    if (settings.errorTimeout > 0)
                        timer.StartTimer(settings.errorTimeout);
                    break;
                case NotificationType.Warning:
                    if (settings.warningTimeout > 0)
                        timer.StartTimer(settings.warningTimeout);
                    break;
                case NotificationType.Info:
                    if (settings.infoTimeout > 0)
                        timer.StartTimer(settings.infoTimeout);
                    break;
            }
        }

        private void OnTimerComplete()
        {
            timer.onTimerCompleteEvent.RemoveAllListeners();

            Hide();
        }
    }    
}