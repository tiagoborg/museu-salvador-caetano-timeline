using System;
using UnityEngine;

namespace EnigmaVirtual.UI
{
    [Serializable]
    public class KeyboardLayout : MonoBehaviour
    {
        public KeyboardScript.LayoutLanguage language;
        public GameObject layout;

        private void Reset()
        {
            if (layout == null)
                layout = GetComponent<GameObject>();
        }

        public void EnableKeyboard()
        {
            layout.gameObject.SetActive(true);
        }

        public void DisablableKeyboard()
        {
            layout.gameObject.SetActive(false);
        }
    }
}