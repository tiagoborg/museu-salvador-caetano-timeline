using System;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace EnigmaVirtual
{
	public enum TimerDirection
	{
		Increase,
		Decrease
	}


	public class TimeController : MonoBehaviour
	{
		public TimerDirection Direction;
		public bool AutoStart = false;
		public int StartEndTime = 60;
		public TimeControllerDisplay Display;
		[Header("Events")]
		public UnityEvent OnTimerEnded = new UnityEvent();

		
		private float _elapsedTime = 0;
		[SerializeField]
		private bool _isActive = false;
		private int _lastSecond = 0;


		// Use this for initialization
		private void Start()
		{
			Development.Log( "Timer Controller initialized" );
			UpdateDisplay();
			if (AutoStart) StartTimer();
			_lastSecond = Direction == TimerDirection.Decrease ? -1 : 0;
		}

		// Update is called once per frame
		private void Update()
		{
			if (_isActive)
			{
				_elapsedTime += Time.deltaTime;
				int elapsedSec = Mathf.FloorToInt( _elapsedTime );
				if (elapsedSec > _lastSecond)
				{
					_lastSecond = elapsedSec;
					UpdateDisplay();
					CheckEnd();
				}
			}
		}

		void CheckEnd()
		{
			if (_elapsedTime >= StartEndTime)
			{
				_elapsedTime = StartEndTime;
				UpdateDisplay();
				StopTimer();
				OnTimerEnded.Invoke();
			}
		}

		protected void UpdateDisplay()
		{
			if (!Display) return;
			if (Direction == TimerDirection.Increase)
			{
				Display.UpdateDisplay( _elapsedTime );
			}
			else
			{
				Display.UpdateDisplay( StartEndTime - _elapsedTime );
			}

		}

		[ContextMenu( "Start Timer" )]
		public void StartTimer()
		{
			_isActive = true;
		}

		[ContextMenu( "Stop Timer" )]
		public void StopTimer()
		{
			_isActive = false;
		}

		[ContextMenu( "Reset" )]
		public void Reset()
		{
			_isActive = false;
			_elapsedTime = 0;
			_lastSecond = Direction == TimerDirection.Decrease ? -1 : 0;
			UpdateDisplay();
		}

		public int GetElapsedTime()
		{
			return _lastSecond;
		}
	}
}