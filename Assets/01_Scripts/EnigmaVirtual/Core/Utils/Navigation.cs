/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * Navigation cannot be copied and/or distributed without 
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EnigmaVirtual
{

    [RequireComponent(typeof(Button))]
	public class Navigation : MonoBehaviour
    {
        public string SceneName;
        private Button _button;

		void Start ()
		{
		    _button = GetComponent<Button>();
		    _button.onClick.AddListener(Goto);
		}

        void OnDestroy()
        {
           if (_button!=null) _button.onClick.RemoveAllListeners();
        }

	    public void Goto()
	    {
            //Development.Log("click");
            SceneManager.LoadScene(SceneName);
	    }
	}
}

