using System;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class FadeRawImage : MonoBehaviour
    {

        private RawImage _image;
        private bool _delayOut;

        void Awake()
        {
            _image = GetComponent<RawImage>();
        }

        void Start()
        {
        }

        private IEnumerator Out(float time, float delay, bool destroy)
        {
            if (_delayOut && delay > 0)
            {
                _delayOut = false;
                yield return new WaitForSeconds(delay);
            }

            while (_image.color.a > 0)
            {
                float alpha = _image.color.a;
                alpha -= Time.deltaTime / time;
                _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, alpha);
                yield return null;
            }

            Destroy(gameObject);
        }

        public void FadeOut(float time)
        {
            _delayOut = false;
            StartCoroutine(Out(time, 0f, false));
        }

        public void FadeOut(float time, float delay, bool destroy)
        {
            _delayOut = delay > 0f;
            StartCoroutine(Out(time, delay, destroy));
        }

        private IEnumerator In(float time)
        {
            while (_image.color.a < 1f)
            {
                float alpha = _image.color.a;
                alpha += Time.deltaTime / time;
                _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, alpha);
                yield return null;
            }
        }

        public void FadeIn(float time)
        {
            StartCoroutine(In(time));
        }

    }
}
