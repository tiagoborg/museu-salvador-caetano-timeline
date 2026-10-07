/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ConfigsPanel can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace EnigmaVirtual
{

    public class DesignScaler : MonoBehaviour
    {
        [SerializeField]
        private float _designWidth;
        [SerializeField]
        private float _designHeight;
        private CanvasScaler _scaler;
		private ApplicationDescription _data;

		// Use this for initialization
		void Start()
        {
			_data = Resources.Load( "Descriptor" ) as ApplicationDescription;
			Setup( _data.DesignWidth, _data.DesignHeight );
        }

        public void Setup(float width, float height)
        {
			_designWidth = width;
			_designHeight = height;
			_scaler = GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.matchWidthOrHeight = 1f;
            _scaler.referenceResolution = new Vector2( _designWidth, _designHeight );
  
        }
    }
}
