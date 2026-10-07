/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual.Screens.Navigation;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnigmaVirtual.Screens
{
    [RequireComponent(typeof(NavigationHistory))]
    public abstract class ScreenControllerBase : MonoBehaviour
    {
        [SerializeField]
        protected NavigationHistory _navigationHistory;
        // ScreenViewBase, not the App-layer ScreenView: a base class must not depend on its own
        // derived type, and that reference was one of the cycles blocking the .asmdef split.
        // Not List<IScreenView> - Unity cannot serialize an interface, and this field is authored
        // in the Inspector. Widening the type keeps existing scene/prefab references valid.
        [SerializeField]
        protected List<ScreenViewBase> screens = new List<ScreenViewBase>();
        [SerializeField]
        protected ScreenType startScreen = ScreenType.Call;

        [Space(20)]

        protected ScreenViewBase _currentScreen;

        public NavigationHistory navigationHistory => _navigationHistory;
        public ScreenType currentScreenType => _currentScreen != null ? _currentScreen.GetScreenType() : ScreenType.None;

        protected virtual void Reset()
        {
            _navigationHistory = GetComponent<NavigationHistory>();
        }

        protected virtual void Awake()
        {
            if (!Core.IsSet)
                return;

            // Add only what isn't already listed. A plain AddRange duplicated any entry authored in
            // the Inspector that is also a child, and a plain Clear() would have discarded authored
            // entries that are NOT children (a screen on a sibling canvas) - which the original
            // append supported.
            foreach (ScreenViewBase screen in gameObject.GetComponentsInChildren<ScreenViewBase>(true))
            {
                if (!screens.Contains(screen))
                    screens.Add(screen);
            }

            InitializeAllScreens();
            DisableAllScreens();
            NavigateToStartScreen();

            if (!Core.IsSet)
                return;

            if (Core.settings.Activity.isActive)
                Core.main.ActivityMonitor.onLongInactivityEvent.AddListener(OnLongInactivity);
        }

        protected virtual void OnDestroy()
        {
            if (!Core.IsSet)
                return;

            if (Core.settings.Activity.isActive)
                Core.main.ActivityMonitor.onLongInactivityEvent.RemoveListener(OnLongInactivity);
        }

        protected virtual void OnLongInactivity()
        {
            Development.CheckPoint();
            NavigateToStartScreen();
        }

        public void NavigateToStartScreen()
        {
            if (currentScreenType == startScreen)
                return;

            NavigateTo(new NavigationItem(startScreen));
        }

        protected virtual void InitializeAllScreens()
        {
            foreach (IScreenView screen in screens)
                screen.Init(this);
        }

        protected void DisableAllScreens()
        {
            foreach (IScreenView screen in screens)
            {
                screen.Group.alpha = 0;
                screen.Group.blocksRaycasts = false;
                screen.Group.interactable = false;
            }
        }

        public virtual void NavigateTo(NavigationItem navItem)
        {
            if (_currentScreen != null)
            {
                _currentScreen.Hide();
                _currentScreen = null;
            }

            _currentScreen = screens.FirstOrDefault(screen => screen.GetScreenType() == navItem.screenType);

            if (_currentScreen != null)
                _currentScreen.Show();
            else
            {
                // Was a double-gated Assert (conditional on DO_LOGS *and* on doLogs), so a
                // navigation target that does not exist silently bounced the user to the start
                // screen with nothing logged anywhere. That reads as "the button does nothing".
                // Also drops a Core.settings deref that is null when settings failed.
                Development.LogError($"ScreenView [{navItem.screenType}] not found in screens - "
                    + "navigating to the start screen instead. Check that a ScreenView with this "
                    + "type is present and registered on the controller.");

                if (navItem.screenType != startScreen)
                    NavigateToStartScreen();
            }
        }

        public virtual void NavigateTo(ScreenType screenType)
        {
            if (currentScreenType == screenType)
                return;

            if (_currentScreen != null)
            {
                _currentScreen.Hide();
                _currentScreen = null;
            }

            _currentScreen = screens.FirstOrDefault(screen => screen.GetScreenType() == screenType);

            if (_currentScreen != null)
                _currentScreen.Show();
            else
            {
                // Same as NavigateTo(NavigationItemBase) above - see the note there.
                Development.LogError($"ScreenView [{screenType}] not found in screens - navigating "
                    + "to the start screen instead. Check that a ScreenView with this type is "
                    + "present and registered on the controller.");

                if (screenType != startScreen)
                    NavigateToStartScreen();
            }
        }
    }
}

