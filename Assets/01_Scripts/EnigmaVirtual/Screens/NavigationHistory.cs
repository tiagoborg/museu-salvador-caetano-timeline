using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual.Screens.Navigation
{
    public class NavigationHistory : MonoBehaviour
    {
        public event Action OnItemPushedEvent;
        public event Action OnItemPoppedEvent;

        // Exposed for editor inspection only — do not modify at runtime
        [NonReorderable]
        [SerializeField] private List<NavigationItem> _stack = new List<NavigationItem>();

        [SerializeField] private int _maxHistorySize = 10;

        private int _idCount = 0;

        public int Count => _stack.Count;
        public NavigationItem Current => _stack.Count == 0 ? null : _stack[_stack.Count - 1];

        // -------------------------------------------------------------------------
        // Push
        // -------------------------------------------------------------------------

        public void Push(ScreenType screenType, object item)
        {
            Push(new NavigationItem(screenType, item));
        }

        public void Push(ScreenType screenType)
        {
            Push(new NavigationItem(screenType));
        }

        public void Push(NavigationItem item)
        {
            item.id = _idCount++;

            _stack.Add(item);

            if (_stack.Count > _maxHistorySize)
                _stack.RemoveAt(0);

            OnItemPushedEvent?.Invoke();
        }

        // -------------------------------------------------------------------------
        // Pop
        // -------------------------------------------------------------------------

        /// <summary>
        /// Removes and returns the top item. Returns null if the stack is empty.
        /// </summary>
        public NavigationItem Pop()
        {
            if (_stack.Count == 0)
            {
                Development.Log("NavigationHistory: Pop called on empty stack.");
                return null;
            }

            NavigationItem item = Current;
            _stack.RemoveAt(_stack.Count - 1);
            OnItemPoppedEvent?.Invoke();
            return item;
        }

        /// <summary>
        /// Removes and returns the top two items as (current, previous).
        /// Either value may be null if the stack doesn't have enough items.
        /// Fires OnItemPoppedEvent once per item removed.
        /// </summary>
        public (NavigationItem current, NavigationItem previous) PopTwo()
        {
            NavigationItem current = Pop();
            NavigationItem previous = Pop();
            return (current, previous);
        }

        /// <summary>
        /// Pops all items down to and including the item with the given id.
        /// Returns the matched item, or null if the id is not found (stack is not modified in that case).
        /// </summary>
        public NavigationItem PopToId(uint id)
        {
            int targetIndex = _stack.FindLastIndex(item => item.id == id);

            if (targetIndex < 0)
            {
                Development.LogError($"NavigationHistory: PopToId could not find id [{id}]. Stack unchanged.");
                return null;
            }

            int removeCount = _stack.Count - targetIndex - 1;
            for (int i = 0; i < removeCount; i++)
            {
                _stack.RemoveAt(_stack.Count - 1);
                OnItemPoppedEvent?.Invoke();
            }

            return _stack[targetIndex];
        }

        // -------------------------------------------------------------------------
        // Peek
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns the item at the given index without removing it. Returns null if out of range.
        /// </summary>
        public NavigationItem PeekAt(int index)
        {
            if (index < 0 || index >= _stack.Count)
            {
                Development.LogError($"NavigationHistory: PeekAt index [{index}] is out of range [0..{_stack.Count - 1}].");
                return null;
            }

            return _stack[index];
        }

        // -------------------------------------------------------------------------
        // Utility
        // -------------------------------------------------------------------------

        public void Clear()
        {
            _stack.Clear();
            _idCount = 0;
        }
    }
}