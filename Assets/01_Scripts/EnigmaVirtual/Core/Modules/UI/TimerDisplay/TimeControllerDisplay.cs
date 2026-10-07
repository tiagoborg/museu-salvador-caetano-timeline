using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace EnigmaVirtual
{
    [RequireComponent( typeof( Text ) )]
    public class TimeControllerDisplay : MonoBehaviour, ITimerDisplay
    {

        private Text _label;
        // Use this for initialization
        void Awake()
        {
            _label = GetComponent<Text>();
        }

        public void UpdateDisplay( float time )
        {
            _label.text = StringUtil.TimeFormat( time );
        }
    }
}