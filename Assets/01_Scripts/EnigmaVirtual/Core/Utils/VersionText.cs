using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(Text))]
    public class VersionText : MonoBehaviour
    {
        void Awake()
        {
            GetComponent<Text>().text = "v."+Application.version;
        }
    }
}
