/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/
using UnityEngine;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(RectTransform))]
    public class Preloader : MonoBehaviour
    {
        [SerializeField]
        private float rotationSpeed = 2f;

        private RectTransform _rect;
        private float _currentRotation;
        private bool _canRotate = false;

        private void Awake()
        {
            Reset();

            _currentRotation = _rect.rotation.eulerAngles.z;
        }

        private void Reset()
        {
            _rect = GetComponent<RectTransform>();
            if (_rect == null)
            {
                Debug.LogError("RectTransform component is missing on " + gameObject.name);
                return;
            }
        }

        private void Update()
        {
            if (!_canRotate || _rect == null)
                return;

            _currentRotation -= rotationSpeed * Time.deltaTime;
            _rect.rotation = Quaternion.Euler(0, 0, _currentRotation);
        }

        public void FadeOut(float fadeDuration = 0.6f)
        {
            if (!_canRotate)
                return;

            if (_rect == null)
                Reset();

            gameObject.SetActive(false);
            LeanTween.alpha(_rect, 0f, fadeDuration).setOnComplete(OnFaded);
        }

        private void OnFaded()
        {
            _canRotate = false;
            gameObject.SetActive(false);
        }

        public void Stop()
        {
            _canRotate = false;
        }

        public void Play()
        {
            gameObject.SetActive(true);
            _canRotate = true;
        }
    }
}

