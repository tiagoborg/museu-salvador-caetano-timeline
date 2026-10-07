using System;
using System.Collections.Generic;

namespace EnigmaVirtual
{
    /// <summary>
    /// A value that notifies when it changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Two behaviour changes in v0.49.0</b>, both breaking for a branched project that
    /// used the old shape. Nothing in this repo does - <c>Observable&lt;T&gt;</c> has zero callers
    /// here - so this is entirely for the projects downstream.</para>
    ///
    /// <para><b>1. <c>OnChanged</c> is an <c>event</c>, not a public field.</b> As a field, any
    /// caller could do <c>obs.OnChanged = null</c> and silently unsubscribe everyone else, or
    /// <c>obs.OnChanged = mine</c> and replace them, or invoke it from outside as though the value
    /// had changed. An <c>event</c> allows only <c>+=</c> and <c>-=</c> from outside. Code that
    /// ASSIGNED it needs one character changed; code that cleared everyone was relying on a bug.</para>
    ///
    /// <para><b>2. Setting the same value no longer notifies.</b> It fired unconditionally before,
    /// so a poll that assigns every frame produced a change event every frame. Comparison is
    /// <c>EqualityComparer&lt;T&gt;.Default</c>, which honours <c>IEquatable&lt;T&gt;</c> and a
    /// <c>struct</c>'s own <c>Equals</c> without boxing - and is null-safe on both sides, so a
    /// reference type going null-to-null is correctly silent while null-to-value is not.</para>
    ///
    /// <para><b>This is the SILENT half of the break, and the one to look for.</b> The
    /// <c>event</c> change is a compile error; this one is not. Two idioms stop working with no
    /// diagnostic at all: <c>obs.Value = obs.Value</c> to re-push the current value to a late
    /// subscriber, and mutating a reference-typed value in place and then <c>Set</c>ting the same
    /// reference to announce it. Both are now no-ops. There is deliberately no <c>ForceNotify()</c>
    /// - adding API for zero known callers is speculation; raise it if a branch needs it.</para>
    ///
    /// <para><b>Subclassing:</b> an <c>event</c> cannot be raised from a derived class (CS0070), so
    /// a subclass notifies by calling <c>Set</c> rather than by touching <c>OnChanged</c>. That is
    /// the only route, and it is enough - the class is left unsealed for it.</para>
    ///
    /// <para><b>Subscribers are invoked one at a time.</b> A multicast delegate stops at the first
    /// one that throws, taking every later subscriber with it - and in Unity a delegate targeting a
    /// destroyed MonoBehaviour is still a live delegate whose first member access throws
    /// MissingReferenceException. One destroyed listener would otherwise strand every subscriber
    /// after it.</para>
    ///
    /// <para>That is a <b>third</b> contract change beyond the two above: a subscriber exception
    /// is now logged and swallowed rather than propagating to whoever assigned the value. Called
    /// out here because a branched project relying on that propagation gets no compile error
    /// either.</para>
    /// </remarks>
    public class Observable<T>
    {
        private T value;

        public event Action<T> OnChanged;

        public T Value
        {
            get { return value; }
            set { Set(value); }
        }

        public Observable(T value)
        {
            this.value = value;
        }

        /// <summary>
        /// Assigns <paramref name="value"/>, notifying only if it differs from the current one.
        /// </summary>
        public void Set(T value)
        {
            if (EqualityComparer<T>.Default.Equals(this.value, value))
                return;

            this.value = value;

            Action<T> handlers = OnChanged;
            if (handlers == null)
                return;

            // GetInvocationList, not a bare invoke: see the class remarks.
            Delegate[] list = handlers.GetInvocationList();

            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    // this.value, NOT the parameter. A subscriber that assigns during notification
                    // re-enters Set and notifies everyone with the newer value; if this loop then
                    // resumed with its captured parameter, every LATER subscriber would be handed
                    // the superseded one - out of order, and disagreeing with this.value. Reading
                    // the field each time makes the last write win everywhere. An earlier version
                    // of this comment claimed the capture made it safe; it did the opposite.
                    ((Action<T>)list[i])(this.value);
                }
                catch (Exception e)
                {
                    // LogException first - it is what carries the stack in a form Unity classifies
                    // as an exception - then the context line saying who was skipped.
                    Development.LogException(e);

                    Delegate d = list[i];
                    string who = d.Method == null
                        ? "<unknown>"
                        : (d.Method.DeclaringType == null ? "" : d.Method.DeclaringType.Name + ".")
                          + d.Method.Name;

                    // Names the failing SUBSCRIBER. typeof(T).Name named the value type, which
                    // identifies nothing when three MonoBehaviours are subscribed to one Observable.
                    Development.LogError($"Observable subscriber [{who}] threw and was skipped; the "
                        + $"remaining {list.Length - i - 1} will still be called.");
                }
            }
        }
    }
}
