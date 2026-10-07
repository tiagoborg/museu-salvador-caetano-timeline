/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * DetectLongClick can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace EnigmaVirtual
{
    public class DetectLongClick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public UnityEvent onLongClickEvent = new UnityEvent();

        [SerializeField]
        private float pressTime = 3f;

        private bool isClicked;
        private float elapsedTime;

        void Update()
        {
            if (isClicked)
            {
                elapsedTime += Time.deltaTime;

                if (elapsedTime > pressTime)
                {
                    // Disarm BEFORE raising, not after. A listener that throws would otherwise
                    // skip the reset and leave isClicked true, so this fires again on the very next
                    // frame - and every frame after that. Mark the
                    // work done first, then announce it.
                    OnPointerUp(null);
                    onLongClickEvent.Invoke();
                }
            }
        }

        /// <summary>
        /// So a pointer event before the boot chain finishes is refused deliberately rather than by
        /// throwing.
        /// </summary>
        /// <remarks>
        /// <c>Core.settings</c> is null until <c>CoreSetupComplete()</c>, so
        /// <c>Core.settings.EditorSettings</c> threw a <c>NullReferenceException</c> on any press
        /// that landed first. The panel was protected only because the EventSystem catches whatever
        /// a handler throws and carries on - the guard was an accident, and it stopped the *rest* of
        /// this method rather than expressing an intent.
        ///
        /// It matters most in exactly the situation it looks harmless in: when boot has stalled,
        /// the preloader is all anyone sees, and holding the corner for three seconds to reach
        /// the settings panel is the first thing a technician tries. That produced a swallowed NRE
        /// and no explanation.
        /// </remarks>
        private bool CanAcceptPointer()
        {
            if (Core.IsSet)
                return true;

            if (!reportedPreBoot)
            {
                reportedPreBoot = true;

                // Ungated Development.Log, not LogIf: LogIf's arguments are stripped with the call
                // and every site passes Core.settings.<Module>.doLogs, which is exactly the null
                // being guarded against here. Once, because OnPointerDown fires on every
                // press. It is DO_LOGS-gated like all of Development.Log, which is acceptable here:
                // the targets that can strand someone on a preloader in the field are Standalone
                // and Android, and both define DO_LOGS.
                Development.Log("Long-press ignored: the boot chain has not completed, so there are "
                    + "no settings to open yet. If this is a stalled boot, the reason is above.");
            }

            return false;
        }

        private bool reportedPreBoot;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanAcceptPointer())
                return;

            if (!Core.settings.EditorSettings.isActive)
                return;

            isClicked = true;
            elapsedTime = 0f;
        }

        /// <summary>
        /// Releasing always clears the press, whatever the settings say.
        /// </summary>
        /// <remarks>
        /// This used to return early when <c>EditorSettings.isActive</c> was false, <b>before</b>
        /// clearing <c>isClicked</c>. The settings panel writes <c>Core.settings</c> at runtime, so
        /// turning EditorSettings off during a press left <c>isClicked</c> true forever - and
        /// <c>Update</c> then invoked <c>onLongClickEvent</c> on <i>every frame</i> from there on,
        /// because its only reset path is this method. An arm/disarm pair must never be gated
        /// asymmetrically: whatever can set the flag, releasing has to clear it.
        /// </remarks>
        public void OnPointerUp(PointerEventData eventData)
        {
            isClicked = false;
            elapsedTime = 0f;
        }
    }
}
