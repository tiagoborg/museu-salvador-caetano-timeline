using UnityEngine;
using System.Collections;

namespace EnigmaVirtual
{
    public class TransitionIn : TransitionBase
    {
        public bool AutoStart = true;
        public bool StartPositioned = true;

        // Use this for initialization
        public override void Awake()
        {
            _autoStart = AutoStart;
            _startPositioned = StartPositioned;
            base.Awake();
        }


    }
}
