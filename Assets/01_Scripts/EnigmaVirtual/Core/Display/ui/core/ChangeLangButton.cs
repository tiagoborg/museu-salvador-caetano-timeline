/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ChangeLangButton cannot be copied and/or distributed without 
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(Image), typeof(Button))] public class ChangeLangButton : MonoBehaviour
    {
        public Langs Language;
        private Button _button;

        void Start()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnButtonClick);
        }

        private void OnButtonClick()
        {
            Core.ChangeCurrentLang(Language);
        }
    }
}