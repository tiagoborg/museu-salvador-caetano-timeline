using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class KeyData : MonoBehaviour
    {
        public int data_id;
        public Button button { get; set; }

        void Awake()
        {
            button = GetComponent<Button>();
        }
    }
}
