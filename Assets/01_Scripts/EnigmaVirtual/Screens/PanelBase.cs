using System;
using System.Collections;
using UnityEngine;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class PanelBase : MonoBehaviour
    {
        public enum PanelType { None, Home, MainMenu }

        [Space, Header("TRANSITION CONFIG")]
        [SerializeField]
        protected CanvasGroup canvasGroup;
        [SerializeField]
        protected float transitionTime = 0.125f;

        public PanelType panelType = PanelType.None;

        private Coroutine animCoroutine;

        public bool isVisible { get { return canvasGroup.alpha > 0f; } }

        protected virtual void Awake()
        {
            Reset();
            if (canvasGroup == null)
            {
                Development.LogError("CanvasGroup component is missing on " + gameObject.name);
                return;
            }

            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        protected virtual void Reset()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
        }

        public virtual void Show(bool animate = true, Action callback = null)
        {
            if (animCoroutine != null)
            {
                StopCoroutine(animCoroutine);
                animCoroutine = null;
            }

            if (animate)
                animCoroutine = StartCoroutine(CrossFadeAlpha(0, 1, transitionTime, callback));
            else
                SetCanvasGroupState(1, true);
        }

        public virtual void Hide(bool animate = true, Action callback = null)
        {
            if (animCoroutine != null)
            {
                StopCoroutine(animCoroutine);
                animCoroutine = null;
            }

            if (animate)
                animCoroutine = StartCoroutine(CrossFadeAlpha(1, 0, transitionTime, callback));
            else
                SetCanvasGroupState(0, false);
        }

        protected IEnumerator CrossFadeAlpha(float startAlpha, float finalAlpha, float duration, Action callback = null)
        {
            if (canvasGroup == null)
            {
                Debug.LogError("CanvasGroup component is missing on " + gameObject.name, this);
                yield break;
            }

            if (canvasGroup.alpha == finalAlpha)
            {
                callback?.Invoke();
                yield break;
            }

            canvasGroup.gameObject.SetActive(true);
            canvasGroup.alpha = startAlpha;
            float counter = 0;

            while (counter < duration)
            {
                counter += Time.deltaTime;
                canvasGroup.alpha = startAlpha < finalAlpha ? counter / duration : 1 - counter / duration;
                yield return null;
            }

            SetCanvasGroupState(canvasGroup.alpha, startAlpha < finalAlpha);
            callback?.Invoke();
        }

        protected void SetCanvasGroupState(float alpha, bool interactable)
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = alpha;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }
    }
}