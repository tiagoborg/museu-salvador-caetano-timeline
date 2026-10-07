using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual.Notifications
{
    public class NotificationManager : MonoBehaviour
    {
        public enum NotificationType
        {
            Success = 0,
            Error,
            Warning,
            Info
        }

        [SerializeField]
        private GameObject notificationPrefab;
        [SerializeField]
        private Transform notificationParent;
        [SerializeField]
        private ScrollRect scrollRect;

        private List<GameObject> notificationQueue = new List<GameObject>();
        private int queueMaxSize = 10;
        private List<NotificationVO> notificationHistory = new List<NotificationVO>();

        public void ShowNotification(NotificationType notificationType, string title, string description = "")
        {
            // Log BEFORE any UI work, and log every notification here rather than at the call
            // sites. Several callers use this as their *only* failure report from inside a catch
            // block - AppDataController has nine, including the appSettings deserialize failure
            // that then continues on silent `new AppSettings()` defaults. The result was an
            // on-screen toast nobody reads on an unattended kiosk and nothing in the log on any
            // platform. One log here covers every caller, including the next one.
            //
            // Error goes to LogError specifically because LogError is always compiled while
            // LogWarning is not - so this is the part that survives on a build without DO_LOGS,
            // which is every platform except Android and Standalone.
            string logLine = string.IsNullOrEmpty(description) ? title : title + ": " + description;

            switch (notificationType)
            {
                case NotificationType.Error:
                    Development.LogError(logLine);
                    break;
                case NotificationType.Warning:
                    Development.LogWarning(logLine);
                    break;
                default:
                    Development.Log(logLine);
                    break;
            }

            // Everything below is the on-screen half, and it is reached from catch blocks. An
            // unassigned prefab would make Instantiate throw *from the reporting path*, replacing
            // the caller's real exception with a NullReference raised while reporting it - the
            // same failure mode a reporter must never have. The log above has already run, so returning here loses
            // the toast, not the information.
            if (notificationPrefab == null || notificationParent == null)
            {
                Development.LogError("notificationPrefab or notificationParent "
                    + "is unassigned, so the on-screen notification was skipped. The message it would "
                    + "have shown is logged immediately above.");
                return;
            }

            NotificationVO notificationVO = new NotificationVO() { notificationType = notificationType, title = title, description = description };

            RemoveActiveDuplicateNotifications(notificationVO);
            notificationVO.count = AddToNotificationHistory(notificationVO);

            // Instantiate the notification prefab
            GameObject notificationObject = Instantiate(notificationPrefab, notificationParent);

            // Set the title and description texts
            NotificationPrefab np = notificationObject.GetComponent<NotificationPrefab>();

            if (np == null)
            {
                Development.LogError("the assigned notificationPrefab has no "
                    + "NotificationPrefab component. Same reasoning as above - the message is logged, "
                    + "the toast is skipped.");
                Destroy(notificationObject);
                return;
            }

            np.SetData(notificationVO);
            np.OnCloseClickEvent += Np_OnCloseClickEvent;

            AddToNotificationQueue(notificationObject);

            // Adjust positions of existing notifications
            StartCoroutine(UpdateNotificationPositions());
        }

        private int AddToNotificationHistory(NotificationVO notificationVO)
        {
            foreach (NotificationVO not in notificationHistory)
            {
                if (not.IsDuplicate(notificationVO))
                {
                    not.IncreaseCount();
                    return not.count;
                }
            }

            notificationHistory.Add(notificationVO);

            return 1;
        }

        private void RemoveActiveDuplicateNotifications(NotificationVO notificationVO)
        {
            foreach (GameObject notificationObject in notificationQueue)
            {
                NotificationPrefab np = notificationObject.GetComponent<NotificationPrefab>();
                if (np.IsDuplicate(notificationVO))
                {
                    np.Hide();
                }
            }
        }

        private void AddToNotificationQueue(GameObject notificationObject)
        {
            // Add the notification to the queue
            notificationQueue.Add(notificationObject);

            if (notificationQueue.Count > queueMaxSize)
            {
                foreach (GameObject go in notificationQueue)
                {
                    NotificationPrefab np = notificationQueue[0].GetComponent<NotificationPrefab>();
                    if (np.notificationType != NotificationType.Error)
                    {
                        np.Hide();
                        break;
                    }
                }
            }
        }

        private void Np_OnCloseClickEvent(GameObject notification)
        {
            CloseNotification(notification);
        }

        public void CloseNotification(GameObject notification)
        {
            // Remove the notification from the queue
            notificationQueue.Remove(notification);

            // Destroy the notification object
            Destroy(notification);

            // Adjust positions of remaining notifications
            StartCoroutine(UpdateNotificationPositions());
        }

        private IEnumerator UpdateNotificationPositions()
        {
            yield return new WaitForEndOfFrame();

            scrollRect.verticalNormalizedPosition = 0;
        }
    }
}