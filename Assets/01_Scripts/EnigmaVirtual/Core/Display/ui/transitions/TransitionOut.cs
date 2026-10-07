using UnityEngine;
using System.Collections;

namespace EnigmaVirtual
{
    public class TransitionOut : TransitionBase
    {

        public bool AutoStart = true;

        public override void Awake()
        {
            _autoStart = AutoStart;
            base.Awake();
        }
    }
}
