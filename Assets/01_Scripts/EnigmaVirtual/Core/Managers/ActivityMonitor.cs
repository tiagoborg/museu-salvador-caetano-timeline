using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{
    public class ActivityMonitor : MonoBehaviour
    {
        public UnityEvent onActivityEvent = new UnityEvent();
        public UnityEvent onLongInactivityEvent = new UnityEvent();

        private bool _isActive = false;
        private ActivitySettings _settings;
        private float _time = 0;

        void Start()
        {
            if (!Core.IsSet)
            {
                // enabled = false, not a bare return: returning leaves _settings null while Update
                // keeps running and dereferences it on its very first statement - a
                // NullReferenceException every frame, starting precisely when the app is already
                // failing and drowning the log that would explain the real cause.
                //
                // Defensive in *this* project rather than a live bug: MainController creates this
                // component from Init(), which runs after Core.IsSet is true, and no scene or
                // prefab places one - so the branch is currently unreachable here. It is live for
                // any derived project that drops an ActivityMonitor into a scene, which is exactly
                // what a base project has to assume someone will do.
                //
                // And it says so. A component that silently stops doing its job is how a feature
                // goes missing with nobody able to say since when.
                Development.LogError("ActivityMonitor disabled: Core is not set, so there are no "
                    + "settings to monitor activity against. The inactivity timeout will not run "
                    + "this session.");
                enabled = false;
                return;
            }

            _settings = Core.settings.Activity;
        }

        void Update()
        {
            if (!_settings.isActive)
                return;

            if (_settings.useMouse)
            {
                if (Input.GetMouseButtonDown(0))
                    SetActive();

                //if (Input.GetMouseButtonDown(1))
                //    Development.Log("Pressed right click.");

                //if (Input.GetMouseButtonDown(2))
                //    Development.Log("Pressed middle click.");
            }
            if (_settings.useKeyBoard)
            {
                if (Input.anyKeyDown)
                    SetActive();
            }
            if (_isActive)
            {
                _time += Time.deltaTime;
                if (_time >= _settings.timeOut)
                {
                    _isActive = false;
                    onLongInactivityEvent?.Invoke();
                }
            }
        }

        public void SetActive(bool value = true)
        {
            if (value)
                if (!_isActive)
                    onActivityEvent.Invoke();
            _isActive = value;
            _time = 0;
        }
    }
}
