/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * NavigateTo cannot be copied and/or distributed without
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(Button))] public class ShowScreen : MonoBehaviour
    {
        public ScreenType ScreenToShow;
        private Button _button;
        private ScreenControllerBase _controller;

        void Start()
        {
            _controller = GetComponentInParent<ScreenControllerBase>() ?? FindFirstObjectByType<ScreenControllerBase>();
        }

        void OnEnable()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
        }

        void OnDisable()
        {
            _button.onClick.RemoveListener(OnClick);
        }


        private void OnClick()
        {
            _controller.NavigateTo(ScreenToShow);
        }
    }
}