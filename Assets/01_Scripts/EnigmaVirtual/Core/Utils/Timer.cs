/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Timer can not be copied and/or distributed without
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{
    /// <summary>
    /// Counts up to <see cref="interval"/> seconds, raising <see cref="onTimerTickEvent"/> every
    /// <see cref="tickInterval"/> and <see cref="onTimerCompleteEvent"/> at the end.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Reset"/> is also a Unity message.</b> The editor calls it when the component is
    /// added and when *Reset* is chosen in the Inspector context menu, so this override runs in edit
    /// mode as well as when your code calls it. Harmless - it only zeroes the running state - but a
    /// subclass overriding it inherits that, so do not put runtime-only work in there. Renaming it
    /// would break every branched project's callers, so it is documented rather than moved.
    /// </remarks>
    public class Timer : MonoBehaviour
    {
        /// <remarks>
        /// Raised <b>after</b> the timer has already been reset and, when looping, restarted - so a
        /// listener reading <see cref="currentTime"/> or <see cref="GetProgress"/> sees 0, not the
        /// interval that just elapsed. Pre-existing, and worth writing down now that
        /// <see cref="GetProgress"/> is reachable from <c>App/</c> code for the first time.
        /// </remarks>
        public UnityEvent onTimerCompleteEvent = new UnityEvent();
        public UnityEvent onTimerTickEvent = new UnityEvent();

        protected bool loop = false;

        protected float _currTime;
        protected float _secsCount;
        protected bool _running;

        [Tooltip("Seconds between onTimerTickEvent. Zero or less means one tick per frame, which is " +
                 "almost never wanted - StartTimer refuses it and falls back to 1.")]
        public float tickInterval = 1f;

        /// <summary>
        /// Seconds until <see cref="onTimerCompleteEvent"/>.
        /// </summary>
        /// <remarks>
        /// <b>A field, not an auto-property, and that is the fix.</b> It was
        /// <c>public float interval { get; set; }</c> - invisible to Unity's serializer, so it could
        /// not be authored in the Inspector beside <c>tickInterval</c>, and it defaulted to <b>0</b>
        /// rather than to anything sensible. A <c>Timer</c> given <see cref="StartTimer()"/> without an
        /// interval therefore satisfied <c>_currTime &gt;= interval</c> on its very first frame and
        /// completed immediately - every frame, if <c>loop</c> was on. The same serializer trap: an
        /// auto-property in a type Unity serializes is not merely unread, it does not exist.
        /// <para>
        /// Dormant in this repo - both call sites set an interval before starting - and live in any
        /// branched project that calls a bare <see cref="StartTimer()"/> or authors a <c>Timer</c> in a
        /// scene expecting the Inspector value to mean something.
        /// </para>
        /// </remarks>
        [Tooltip("Seconds until onTimerCompleteEvent. Overwritten by SetTimer/StartTimer(interval).")]
        public float interval = 1f;

        /// <remarks>
        /// Read-only. It had a public setter that did exactly what <see cref="StartTimer()"/> and
        /// <see cref="StopTimer"/> do, by another name and without their guards or their
        /// <c>virtual</c> overrides - so a subclass could be bypassed by assigning a bool. One way
        /// in.
        /// </remarks>
        public bool IsRunning => _running;

        public float currentTime { get { return _currTime; } }

        /// <summary>Alias of <see cref="SetTimer"/>, which it duplicated verbatim.</summary>
        /// <remarks>
        /// Deprecated rather than deleted: unlike an identity <c>GetTranslation</c>, this does real
        /// work, so removing it takes function away from a branched project that calls it. A warning
        /// breaks no build and says which of the two names to write.
        /// </remarks>
        [System.Obsolete("Use SetTimer instead - SetDuration is the same method under a second name.")]
        public void SetDuration(float interval = 1f, bool loop = false) => SetTimer(interval, loop);

        public void SetTimer(float interval = 1f, bool loop = false)
        {
            // No guard here: a non-positive interval is stored as given and refused at StartTimer,
            // where "0 means no timeout" can be honoured. Rejecting it at the setter would make
            // SetTimer(0) mean something different from an Inspector-authored 0.
            this.loop = loop;
            this.interval = interval;
            _currTime = _secsCount = 0f;
            _running = false;
        }

        /// <remarks>
        /// <c>protected virtual</c>, not <c>public</c> and not plain <c>private</c>. Public made a
        /// Unity message part of the API - anyone could advance the timer a second time in the same
        /// frame, and the Inspector listed it as a <c>UnityEvent</c> target. But private would set a
        /// worse trap for the six <c>virtual</c> methods below it: a subclass declaring its own
        /// <c>Update()</c> gets **no** CS0108 warning, because private members are not inherited, and
        /// Unity dispatches only the most-derived declaration - so the base tick would stop, silently,
        /// in the one method a subclass is most likely to write.
        /// </remarks>
        protected virtual void Update()
        {
            if (!_running)
                return;

            _currTime += Time.deltaTime;
            _secsCount += Time.deltaTime;

            if (_secsCount >= tickInterval)
            {
                onTimerTickEvent.Invoke();

                // -= rather than = 0, so the overshoot past tickInterval carries into the next tick
                // instead of being discarded: at 60fps with a 1s tick the old form lost up to 16ms
                // every tick and drifted late over a long countdown.
                //
                // Capped at one interval of backlog, deliberately. After a hitch longer than
                // tickInterval the missed ticks are dropped rather than replayed - a tick handler
                // usually animates or beeps, and a burst is worse than a gap. That is what the old
                // code did by accident; this does it on purpose and stays exact for normal frames.
                _secsCount = Mathf.Min(_secsCount - tickInterval, tickInterval);
            }

            if (_currTime >= interval)
            {
                // Same carry, for the same reason. Reset() zeroes _currTime, so without this a
                // looping timer threw away the completion overshoot too and every cycle ran up to a
                // frame long - the drift the tick accumulator above had just been fixed for, still
                // accumulating one level up.
                float overshoot = _currTime - interval;

                Reset();

                if (loop)
                {
                    StartTimer();

                    // Carry only a partial overshoot. A hitch longer than the whole interval would
                    // otherwise leave _currTime at interval and complete again on the very next frame,
                    // turning a stall into a burst of completions - the same trap as the tick backlog
                    // above, one level up.
                    _currTime = overshoot < interval ? overshoot : 0f;
                }

                onTimerCompleteEvent.Invoke();
            }
        }

        virtual public void Reset()
        {
            _currTime = _secsCount = 0f;
            _running = false;
        }

        virtual public void RestartTimer()
        {
            Reset();
            StartTimer();
        }

        virtual public void StopTimer()
        {
            _running = false;
        }

        virtual public void StartTimer(float interval, bool loop = false)
        {
            SetTimer(interval, loop);
            StartTimer();
        }

        virtual public void StartTimer()
        {
            if (interval <= 0f)
            {
                // Zero already means "no timeout" elsewhere in this codebase - DebugSettings ships
                // errorTimeout: 0 and NotificationPrefab tests > 0 before starting - so a non-positive
                // interval is an operator's choice, not a fault. Honour it by not running, rather than
                // completing on the first frame, which is what the old code did and what nobody ever
                // meant. A warning, not an error: a failed validation is not an exception.
                Development.LogWarning($"Timer on [{name}] has an interval of {interval}s, which means "
                    + "no timeout. Not starting - onTimerCompleteEvent will not fire.");
                return;
            }

            if (tickInterval <= 0f)
            {
                // No "never tick" semantic to honour here - a non-positive tickInterval just raises
                // the event every frame, which is never the intent.
                Development.LogWarning($"Timer on [{name}] has a tickInterval of {tickInterval}s, which "
                    + "would raise onTimerTickEvent every frame. Using 1s.");
                tickInterval = 1f;
            }

            _running = true;
        }

        /// <remarks>
        /// Routed through <see cref="StopTimer"/> / <see cref="StartTimer()"/> rather than assigning
        /// <c>_running</c>. Setting the flag directly was the second way in that the removed
        /// <c>IsRunning</c> setter left behind: it skipped the interval guard, so a never-configured
        /// timer resumed straight into completing on its first frame, and it skipped a subclass's
        /// overrides.
        /// </remarks>
        virtual public void PauseTimer(bool val)
        {
            if (val)
                StopTimer();
            else
                StartTimer();
        }

        /// <summary>
        /// How far through the interval, 0 to 1. Returns 0 rather than <c>NaN</c> when there is no
        /// interval to be a fraction of.
        /// </summary>
        /// <remarks>
        /// <c>public</c>, not <c>internal</c>: this is a **restoration**, not a widening. Before the
        /// v0.24.0 assembly split everything shared one assembly, so `internal` was decorative and any
        /// project could call this; the split turned it into a latent CS0122 for every branched project
        /// that did. The divide was also unguarded - <c>_currTime / 0</c> is <c>NaN</c>, and a
        /// <c>NaN</c> assigned to <c>Image.fillAmount</c> propagates into the layout rather than
        /// throwing, so it fails as a wrong picture with a clean console.
        /// </remarks>
        public float GetProgress()
        {
            if (interval <= 0f)
                return 0f;

            return Mathf.Clamp01(_currTime / interval);
        }
    }
}

