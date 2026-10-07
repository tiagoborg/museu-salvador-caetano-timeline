/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class AppInfo : MonoBehaviour
    {
        [SerializeField]
        private float Delay = 3f;
        [SerializeField]
        private bool UsesConfigPanel = false;
        [SerializeField]
        private GameObject GuideImage = null;
        [SerializeField]
        private ConfigsPanel ConfigsPanel = null;
        [SerializeField]
        private Text Title = null;
        [SerializeField]
        private Text SubTitle = null;
        [Header("Buttons")]
        [SerializeField]
        private RectTransform ButtonsPanel = null;
        [SerializeField]
        private Button ShutDownButton = null;
        [SerializeField]
        private Button CloseButton = null;
        [SerializeField]
        private Button SettingsButton = null;
        [SerializeField]
        private Button GuideButton = null;
        [SerializeField]
        private Button CloseGuideButton = null;

        [Space]
        public UnityEvent onHiddenEvent = new UnityEvent();
        public UnityEvent onInfoHidedEvent = new UnityEvent();

        private RectTransform _rectTrans;
        private float _startY;

        /// <summary>The pending auto-hide, so a second Show replaces it rather than stacking
        /// a second one beside it.</summary>
        private Coroutine _pendingHide;
        private ApplicationDescription _data;

        public void Init()
        {
            _data = Resources.Load("Descriptor") as ApplicationDescription;
            _rectTrans = GetComponent<RectTransform>();
            _startY = _rectTrans.anchoredPosition.y;
            ButtonsPanel.anchoredPosition = new Vector2(ButtonsPanel.sizeDelta.x, 0f);

            ConfigsPanel.Hide();

            Title.text = _data.FullName;
            SubTitle.text = "version " + _data.Version + "\n" + _data.Copyright;

            ShutDownButton.onClick.AddListener(OnShutDown);
            CloseButton.onClick.AddListener(Hide);
            GuideButton.onClick.AddListener(ShowGuide);
            CloseGuideButton.onClick.AddListener(HideGuide);
            if (UsesConfigPanel) SettingsButton.onClick.AddListener(ShowSettings);
            Hide(.8f, Delay);
        }

        private void ShowGuide()
        {
            GuideImage.SetActive(true);
        }

        private void HideGuide()
        {
            GuideImage.SetActive(false);
        }

        private void ShowSettings()
        {
            if (ConfigsPanel.isVisible)
                ConfigsPanel.Hide();
            else
                ConfigsPanel.Show();
        }

        private void OnShutDown()
        {
            Application.Quit();
        }

        public void Show(bool isForced)
        {
            Development.Log("Show" + isForced);
            LeanTween.move(_rectTrans, new Vector3(0f, 0f, 0f), 1.2f).setEase(LeanTweenType.easeOutQuint);
            if (!isForced)
            {
                Development.Log("DelayedClose");
                this.CallLater(Delay, Hide, ref _pendingHide);
            }
            else
            {
                ButtonsPanel.gameObject.SetActive(true);
                LeanTween.move(ButtonsPanel, new Vector2(0f, -70f), 1.2f).setEase(LeanTweenType.easeOutQuint);
            }
        }

        public void Hide()
        {
            Hide(1.2f, 0f);
        }

        public void Hide(float time, float delay)
        {
            LeanTween.move(_rectTrans, new Vector3(0f, _startY, 0f), time).setEase(LeanTweenType.easeOutQuint).setDelay(delay).setOnComplete(OnHided);
            LeanTween.move(ButtonsPanel, new Vector2(ButtonsPanel.sizeDelta.x, -70f), time).setDelay(delay).setEase(LeanTweenType.easeOutQuint);

            if (ConfigsPanel.isVisible)
                ConfigsPanel.Hide();
        }

        private void OnHided()
        {
            onHiddenEvent.Invoke();
            gameObject.SetActive(false);
        }
    }
}

