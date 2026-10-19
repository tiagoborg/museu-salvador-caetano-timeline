using System;
using System.Collections.Generic;
using EnigmaVirtual;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Input for a uGUI input module that exposes at most one touch: the first finger to touch down, for as long
/// as it stays down. Every other finger is ignored, including one that was already down when the first lifts,
/// so a second finger never takes over a gesture. While any finger is down the mouse reads as absent, so the
/// legacy touch-to-mouse emulation cannot feed another finger in through the mouse path.
/// </summary>
/// <remarks>
/// Installed as the module's <see cref="BaseInputModule.inputOverride"/> by <see cref="Install"/>, and
/// removed by <see cref="Uninstall"/>, which restores whatever override was there before. Both take effect
/// in <c>LateUpdate</c>, after the event system has run: they are called from click handlers, i.e. from
/// inside the module's own touch loop, and swapping its input there would mix two touch sources in one
/// pass. Fingers already down when the swap happens are reported once as cancelled, so the module releases
/// whatever they were pressing or dragging, and are then ignored.
/// </remarks>
public class SingleTouchInput : BaseInput
{
    private const int NoFinger = -1;

    private BaseInputModule _module;
    private BaseInput _previous;
    private bool _wanted;
    // Fingers that were down at install, exposed once as cancelled on the next frame.
    private readonly List<Touch> _release = new List<Touch>();
    private readonly List<Touch> _exposed = new List<Touch>();
    private int _finger = NoFinger;
    private int _frame = -1;
    private readonly List<Touch> _raw = new List<Touch>();

    public override int touchCount
    {
        get
        {
            Refresh();
            return _exposed.Count;
        }
    }

    public override Touch GetTouch(int index)
    {
        Refresh();
        return index >= 0 && index < _exposed.Count ? _exposed[index] : default(Touch);
    }

    public override bool mousePresent
    {
        get { return base.mousePresent && Input.touchCount == 0; }
    }

    /// <summary>
    /// Makes single touch the input of the current event system's module from the end of this frame. Returns
    /// the component, or null - with an error logged - when there is no event system or module. Safe to call
    /// again while installed.
    /// </summary>
    public static SingleTouchInput Install()
    {
        EventSystem eventSystem = EventSystem.current;
        BaseInputModule module = eventSystem == null ? null
            : eventSystem.currentInputModule != null ? eventSystem.currentInputModule
            : eventSystem.GetComponent<BaseInputModule>();
        if (module == null)
        {
            Development.LogError("SingleTouchInput: no event system or input module found, so simultaneous "
                + "touches are NOT limited to one.");
            return null;
        }

        SingleTouchInput input = module.GetComponent<SingleTouchInput>();
        if (input == null)
            input = module.gameObject.AddComponent<SingleTouchInput>();

        input._module = module;
        input._wanted = true;
        return input;
    }

    /// <summary>Restores the module's previous input at the end of this frame, if this is still its override. Idempotent.</summary>
    public void Uninstall()
    {
        _wanted = false;
    }

    private void LateUpdate()
    {
        if (_module == null)
            return;

        if (_wanted && _module.inputOverride != this)
        {
            _previous = _module.inputOverride;
            _module.inputOverride = this;
            _finger = NoFinger;
            _frame = -1;

            _release.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch held = Input.GetTouch(i);
                if (held.phase == TouchPhase.Ended || held.phase == TouchPhase.Canceled)
                    continue;
                held.phase = TouchPhase.Canceled;
                _release.Add(held);
            }
        }
        else if (!_wanted && _module.inputOverride == this)
        {
            _module.inputOverride = _previous;
            _previous = null;
            _finger = NoFinger;
            _release.Clear();
        }
    }

    private void Refresh()
    {
        if (_frame == Time.frameCount)
            return;
        _frame = Time.frameCount;

        _exposed.Clear();
        _exposed.AddRange(_release);
        _release.Clear();

        _raw.Clear();
        for (int i = 0; i < Input.touchCount; i++)
            _raw.Add(Input.GetTouch(i));
        if (Select(_raw, ref _finger, out Touch chosen))
            _exposed.Add(chosen);
    }

    /// <summary>
    /// Picks the one touch to expose this frame and tracks which finger owns it. Only a touch that begins while
    /// no finger is locked can take the lock; it is exposed until it ends or is cancelled (that frame included,
    /// so the module sees the release), or until it disappears.
    /// </summary>
    public static bool Select(IList<Touch> touches, ref int lockedFinger, out Touch chosen)
    {
        chosen = default(Touch);
        int count = touches != null ? touches.Count : 0;

        if (lockedFinger != NoFinger)
        {
            for (int i = 0; i < count; i++)
            {
                if (touches[i].fingerId != lockedFinger)
                    continue;

                chosen = touches[i];
                if (chosen.phase == TouchPhase.Ended || chosen.phase == TouchPhase.Canceled)
                    lockedFinger = NoFinger;
                return true;
            }

            // The locked finger vanished without an Ended phase: free the lock, expose nothing this frame.
            lockedFinger = NoFinger;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (touches[i].phase != TouchPhase.Began)
                continue;

            chosen = touches[i];
            lockedFinger = chosen.fingerId;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    /// <summary>Selection check, runnable from eval as <c>SingleTouchInput.SelfCheck()</c>. Pure; touches no scene.</summary>
    public static string SelfCheck()
    {
        int checks = 0;
        Action<bool, string> check = (condition, what) =>
        {
            checks++;
            if (!condition)
                throw new Exception("SingleTouchInput self-check FAILED: " + what);
        };

        Func<int, TouchPhase, Touch> t = (finger, phase) => new Touch { fingerId = finger, phase = phase };
        int locked = NoFinger;
        Touch chosen;

        check(!Select(new List<Touch>(), ref locked, out chosen) && locked == NoFinger, "no touches: nothing");

        // First finger down takes the lock.
        check(Select(new[] { t(3, TouchPhase.Began) }, ref locked, out chosen) && chosen.fingerId == 3 && locked == 3,
            "first touch is exposed and locked");

        // A second finger, beginning while the first is held, is ignored.
        check(Select(new[] { t(3, TouchPhase.Moved), t(5, TouchPhase.Began) }, ref locked, out chosen)
            && chosen.fingerId == 3 && chosen.phase == TouchPhase.Moved && locked == 3, "second finger ignored");

        // Order in the raw list does not matter: the locked finger is found by id.
        check(Select(new[] { t(5, TouchPhase.Stationary), t(3, TouchPhase.Moved) }, ref locked, out chosen)
            && chosen.fingerId == 3, "locked finger found whatever its index");

        // The first lifts: its Ended frame is still exposed, so the module releases it, then the lock frees.
        check(Select(new[] { t(3, TouchPhase.Ended), t(5, TouchPhase.Moved) }, ref locked, out chosen)
            && chosen.fingerId == 3 && chosen.phase == TouchPhase.Ended && locked == NoFinger, "release exposed once");

        // The finger that was already down never takes over.
        check(!Select(new[] { t(5, TouchPhase.Moved) }, ref locked, out chosen) && locked == NoFinger,
            "a finger held from before is not adopted");

        // Counter-test: a new touch after everything is up is the first touch again.
        check(!Select(new List<Touch>(), ref locked, out chosen), "all lifted");
        check(Select(new[] { t(7, TouchPhase.Began) }, ref locked, out chosen) && chosen.fingerId == 7 && locked == 7,
            "a later single touch works");

        // Cancel releases like Ended.
        check(Select(new[] { t(7, TouchPhase.Canceled) }, ref locked, out chosen) && chosen.phase == TouchPhase.Canceled
            && locked == NoFinger, "cancel releases");

        // Two fingers beginning in the same frame: only the first listed is taken.
        check(Select(new[] { t(1, TouchPhase.Began), t(2, TouchPhase.Began) }, ref locked, out chosen)
            && chosen.fingerId == 1 && locked == 1, "simultaneous begin: one taken");

        // A locked finger that vanishes without Ended frees the lock instead of sticking.
        check(!Select(new[] { t(2, TouchPhase.Moved) }, ref locked, out chosen) && locked == NoFinger,
            "vanished finger frees the lock, other finger still ignored");

        return "SingleTouchInput self-check PASS (" + checks + " checks)";
    }
#endif
}
