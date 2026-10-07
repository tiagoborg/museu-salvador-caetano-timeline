/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;

namespace EnigmaVirtual.Screens
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class ScreenViewBase : MonoBehaviour, IScreenView
    {
        [SerializeField]
        protected ScreenType screenType;
        [SerializeField]
        protected float transitionSpeed = 0.125f;

        private CanvasGroup _group;
        private bool _isInitialized = false;

        protected ScreenControllerBase Controller;

        public ScreenType GetScreenType()
        {
            return screenType;
        }

        public void SetScreenType(ScreenType screenType)
        {
            this.screenType = screenType;
        }

        public void Init(ScreenControllerBase controller)
        {
            if (_isInitialized) return;
            _group = GetComponent<CanvasGroup>();
            Controller = controller;
            _isInitialized = true;
        }

        [ContextMenu("Show")]
        public virtual void Show()
        {
            if (IsShown || !_isInitialized) return;
            gameObject.SetActive(true);
            LeanTween.alphaCanvas(Group, 1f, transitionSpeed).setOnComplete(EnableGroup);
        }

        public virtual void SetData<T>(T data) where T : class { }

        public void EnableGroup()
        {
            _group.blocksRaycasts = true;
            _group.interactable = true;
        }

        public void DisableGroup()
        {
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        [ContextMenu("Hide")]
        public virtual void Hide()
        {
            //if (!IsShown || !_isInitialized) return;
            //gameObject.SetActive(false);
            LeanTween.alphaCanvas(Group, 0f, transitionSpeed);
            DisableGroup();
        }

        public bool IsShown { get { return _group.alpha > 0; } }
        public CanvasGroup Group { get { return _group; } }
    }
}