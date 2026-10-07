using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;

namespace EnigmaVirtual.Components.UIElements
{
    [AddComponentMenu("UI/Extensions/UI Magnetic Infinite Scroll")]
    [RequireComponent(typeof(ScrollRect))]
    public class UI_MagneticInfiniteScroll : UI_InfiniteScroll, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public event Action<GameObject> OnNewSelect;

        [Tooltip("The pointer to the pivot, the visual element for centering objects.")]
        [SerializeField]
        private RectTransform pivot = null;
        [Tooltip("The maximum speed that allows you to activate the magnet to center on the pivot")]
        [SerializeField]
        private float maxSpeedForMagnetic = 10f;
        [SerializeField]
        [Tooltip("The index of the object which must be initially centered")]
        private int indexStart = 0;
        [SerializeField]
        [Tooltip("The time to decelerate and aim to the pivot")]
        private float timeForDeceleration = 0.05f;

        private float _pastPositionMouseSpeed;
        private float _initMovementDirection = 0;
        private float _pastPosition = 0;
        private float _currentSpeed = 0.0f;
        private float _stopValue = 0.0f;
        private readonly float _waitForContentSet = 0.1f;
        private float _currentTime = 0;
        private int _nearestIndex = 0;
        private bool _useMagnetic = true;
        private bool _isStopping = false;
        private bool _isMovement = false;

        public List<RectTransform> Items { get; }

        [ContextMenu("Reset content position")]
        public void ResetContentPosition()
        {
            StartCoroutine(ResetScrollRectPos());
        }

        // An unused GetSnapToPositionToBringChildIntoView snippet lived here. Nothing called
        // it and there is no such method anywhere else in the repo - an earlier version of this
        // note claimed it duplicated a base-class one, which was invented; this class extends
        // UI_InfiniteScroll, not ScrollSnapBase.

        private IEnumerator ResetScrollRectPos()
        {
            _scrollRect.content.anchoredPosition = Vector2.zero;
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content.GetComponent<RectTransform>());

            List<Transform> tl = new List<Transform> { };
            //for (int i= items.Count -1 ; i >= 0; i--)
            //{
            //    Transform go = Instantiate(items[i].transform);
            //    tl.Add(go);
            //}

            for (int i = _scrollRect.content.childCount - 1; i >= 0; i--)
            {
                Transform go = Instantiate(_scrollRect.content.GetChild(i));
                go.GetComponent<RectTransform>().position = Vector2.zero;
                tl.Add(go);
            }

            yield return new WaitForEndOfFrame();
            SetNewItems(ref tl);

            //yield return new WaitForSeconds(0.5f);
            //SetInitContent();
        }

        public void SnapTo(RectTransform target)
        {
            Canvas.ForceUpdateCanvases();

            _scrollRect.content.anchoredPosition =
                (Vector2)_scrollRect.transform.InverseTransformPoint(_scrollRect.content.position)
                - (Vector2)_scrollRect.transform.InverseTransformPoint(target.position);
        }

        protected override void Awake()
        {
            base.Awake();
            if (!InitByUser)
                SetInitContent();
        }

        public void SetInitContent()
        {
            StartCoroutine(SetInitContent_IE());
        }

        private void Update()
        {
            if (_scrollRect == null || !_scrollRect.content || !pivot || !_useMagnetic || !_isMovement || items == null)
                return;

            float currentPosition = GetRightAxis(_scrollRect.content.anchoredPosition);
            _currentSpeed = Mathf.Abs(currentPosition - _pastPosition);
            _pastPosition = currentPosition;
            if (Mathf.Abs(_currentSpeed) > maxSpeedForMagnetic)
                return;

            if (_isStopping)
            {
                Vector2 anchoredPosition = _scrollRect.content.anchoredPosition;
                _currentTime += Time.deltaTime;
                float valueLerp = _currentTime / timeForDeceleration;

                float newPosition = Mathf.Lerp(GetRightAxis(anchoredPosition), _stopValue, valueLerp);

                _scrollRect.content.anchoredPosition = _isVertical ? new Vector2(anchoredPosition.x, newPosition) :
                                new Vector2(newPosition, anchoredPosition.y);

                if (newPosition == GetRightAxis(anchoredPosition) && _nearestIndex > 0 && _nearestIndex < items.Count)
                {
                    _isStopping = false;
                    _isMovement = false;
                    var item = items[_nearestIndex];
                    if (item != null && OnNewSelect != null)
                        OnNewSelect.Invoke(item.gameObject);
                }
            }
            else
            {
                float distance = Mathf.Infinity * (-_initMovementDirection);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null)
                        continue;

                    var aux = GetRightAxis(item.position) - GetRightAxis(pivot.position);

                    if ((_initMovementDirection <= 0 && aux < distance && aux > 0) ||
                        (_initMovementDirection > 0 && aux > distance && aux < 0))
                    {
                        distance = aux;
                        _nearestIndex = i;
                    }
                }

                _isStopping = true;
                _stopValue = GetAnchoredPositionForPivot(_nearestIndex);
                _scrollRect.StopMovement();
            }
        }

        public override void SetNewItems(ref List<Transform> newItems)
        {
            foreach (var element in newItems)
            {
                RectTransform rectTransform = element.GetComponent<RectTransform>();
                if (rectTransform && pivot)
                    rectTransform.sizeDelta = pivot.sizeDelta;
            }
            base.SetNewItems(ref newItems);
        }

        public void SetContentInPivot(int index)
        {
            float newPos = GetAnchoredPositionForPivot(index);
            Vector2 anchoredPosition = _scrollRect.content.anchoredPosition;

            if (_scrollRect.content)
            {
                _scrollRect.content.anchoredPosition = _isVertical ? new Vector2(anchoredPosition.x, newPos) :
                                            new Vector2(newPos, anchoredPosition.y);
                _pastPosition = GetRightAxis(_scrollRect.content.anchoredPosition);
            }
        }

        private IEnumerator SetInitContent_IE()
        {
            yield return new WaitForSeconds(_waitForContentSet);
            SetContentInPivot(indexStart);
        }

        private float GetAnchoredPositionForPivot(int index)
        {
            if (!pivot || items == null || items.Count < 0)
                return 0f;

            index = Mathf.Clamp(index, 0, items.Count - 1);

            float posItem = GetRightAxis(items[index].anchoredPosition);
            float posPivot = GetRightAxis(pivot.anchoredPosition);
            return posPivot - posItem;
        }

        private void FinishPrepareMovement()
        {
            _isMovement = true;
            _useMagnetic = true;
            _isStopping = false;
            _currentTime = 0;
        }

        private float GetRightAxis(Vector2 vector)
        {
            return _isVertical ? vector.y : vector.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float currentPosition = GetRightAxis(UIExtensionsInputManager.MousePosition);

            _initMovementDirection = Mathf.Sign(currentPosition - _pastPositionMouseSpeed);
            _pastPositionMouseSpeed = currentPosition;
            _useMagnetic = false;
            _isStopping = false;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            FinishPrepareMovement();
        }

        public void OnScroll(PointerEventData eventData)
        {
            _initMovementDirection = -UIExtensionsInputManager.MouseScrollDelta.y;
            FinishPrepareMovement();
        }
    }
}
