/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * Countdown can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace EnigmaVirtual
{
	public class Countdown : MonoBehaviour {

		public int Seconds = 5;
		public bool AutoStart = false;
		public GameObject Display;
		public Text TimerDisplay;


		private AudioSource _audio;
		private float _elapsedTime;
		private bool _isCounting;
		private int _lastSecond;
	
		void Awake () {
			_audio = GetComponent<AudioSource>();
			Reset();
		}

		void Start () {
			if (AutoStart) StartCountdown();
		}
		
		void Update () {
			if (!_isCounting || TimerDisplay == null) return;

			_elapsedTime += Time.deltaTime;
			if ((int)_elapsedTime != _lastSecond)
			{
				_lastSecond = ( int )_elapsedTime;
				TimerDisplay.text = Seconds - _lastSecond + "";
				_audio.Play();
			}
			if ((int)_elapsedTime >=Seconds)
			{				
				TimerDisplay.text = "0";
				Reset();
			}
		}

		void Reset()
		{			
			_isCounting = false;
			_elapsedTime = 0f;
			if (Display != null)
				Display.SetActive( false );
		}

		public void StartCountdown()
		{
			if (Display != null) Display.SetActive( true );
			_isCounting = true;
			_audio.Play();
		}
	}
}
