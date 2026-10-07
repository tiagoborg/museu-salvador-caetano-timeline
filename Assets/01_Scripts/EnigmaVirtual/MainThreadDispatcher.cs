using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        public static MainThreadDispatcher Instance;
        readonly Queue<Action> actions = new();

        /// <summary>
        /// Reused between frames so the drain does not allocate on a per-frame path. Only ever touched
        /// on the main thread, and only while no lock is held, so it needs no synchronisation of its own.
        /// </summary>
        private readonly List<Action> draining = new();

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                //DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                // Destroys the whole GameObject, which is inherited behaviour and deliberately left
                // alone here - but it takes every other component on that object with it. See the
                // register for why that is filed rather than changed.
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Set once this dispatcher stops draining, so work arriving afterwards is refused out loud
        /// instead of being queued to nothing.
        /// </summary>
        /// <remarks>
        /// <para><b>Every access is under <c>lock (actions)</c>, and it is deliberately NOT
        /// <c>volatile</c>.</b> The first version of this fix read it before taking the lock, which
        /// only narrowed the window it existed to close: a worker could pass the check, be pre-empted,
        /// and enqueue after <c>OnDisable</c> had run - landing on a queue nothing drains, silently,
        /// which is the defect. <c>volatile</c> gives visibility, not atomicity across the
        /// check-then-enqueue pair, and that pair is the whole problem; <c>Interlocked</c> would make a
        /// write atomic that already was. Mutual exclusion is what is needed and the right lock object
        /// was already to hand. <c>volatile</c> is dropped because Monitor acquire/release is a full
        /// fence, and a <c>volatile</c> field read only under a lock invites the next person to read it
        /// outside one.</para>
        ///
        /// <para>An instance field, not a static, and that is a correctness point rather than taste:
        /// <c>Awake</c> calls <c>Destroy(gameObject)</c> on a duplicate, <c>Destroy</c> is deferred, so
        /// the duplicate still receives <c>OnDisable</c> - a static would be latched true by a dying
        /// duplicate and permanently refuse work on the surviving instance.</para>
        ///
        /// <para>Set from <c>OnDisable</c>, not <c>OnDestroy</c>: <c>OnDisable</c> runs for both, and
        /// deactivation is the half that leaves the object alive-but-not-draining, which is exactly the
        /// window this exists for.</para>
        /// </remarks>
        private bool isShuttingDown;

        void OnEnable()
        {
            // Under the lock like every other access, so a worker cannot observe a half-published
            // change. Re-enabling recovers rather than refusing forever.
            lock (actions)
            {
                isShuttingDown = false;
            }
        }

        /// <summary>
        /// Stops accepting work, and says what was left holding.
        /// </summary>
        /// <remarks>
        /// Anything enqueued after the last <c>Update</c> was previously discarded with no trace, which
        /// is the routine loss at quit rather than an exotic one. It is reported and cleared, <b>not</b>
        /// invoked: running stored callbacks during teardown is how you get a
        /// <c>MissingReferenceException</c> per delegate whose target is already destroyed, and
        /// carrying them across a re-enable would run stale work whose world has moved on.
        ///
        /// The log is raised outside the lock, for the reason <c>Update</c> already argues - nothing
        /// arbitrary happens while the lock is held.
        /// </remarks>
        void OnDisable()
        {
            int stranded;

            lock (actions)
            {
                isShuttingDown = true;
                stranded = actions.Count;
                actions.Clear();
            }

            if (stranded > 0)
                Development.LogError($"MainThreadDispatcher stopped draining with {stranded} queued "
                    + "action(s) still pending; they were DROPPED and will not run.");
        }

        /// <summary>
        /// Clears the static, so the reference cannot outlive the object.
        ///
        /// In *this* project that is defensive rather than urgent: the dispatcher is a child of
        /// MainController under Core, which is DontDestroyOnLoad, so this realistically only runs at
        /// quit - a scene unload does not strand it here. It matters for a derived project that
        /// places one per scene.
        /// </summary>
        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void RunOnMainThread(Action action)
        {
            // Checked before the dispatcher lookup below, because a null action is a caller bug that
            // needs no dispatcher to diagnose - and reporting it through the "no dispatcher" path would
            // have blamed a missing component for someone else's mistake. A null would otherwise
            // surface as a NullReferenceException during some later frame's drain, by which point
            // nothing identifies who enqueued it.
            if (action == null)
            {
                Development.LogError("RunOnMainThread was given a null action; nothing was queued.");
                return;
            }

            // Snapshotted into a local, and that is load-bearing. Instance is a static that OnDestroy
            // above now clears on the main thread, so reading it three times - guard, lock, enqueue -
            // leaves a window where a worker passes the guard and then dereferences null on the lock.
            // The old code read it repeatedly too, but was benign precisely because nothing ever
            // cleared it; adding OnDestroy is what opened the window.
            MainThreadDispatcher dispatcher = Instance;

            // ReferenceEquals, not ==. Unity overloads == on Object so a destroyed object reports as
            // null, and that override reads native engine state, which is not safe from a worker
            // thread - and a worker thread is the only kind that calls this method.
            if (ReferenceEquals(dispatcher, null))
            {
                // Logged, not thrown. Today's only caller (SocketConnection.ConnectToTcpServer) does
                // wrap this in a try/catch, so the throw was not actually escaping into the void - but
                // it was being swallowed into an errorMessage nobody reads, and the next caller from a
                // raw thread would have lost the thread outright. An Error line survives either way.
                Development.LogError("MainThreadDispatcher is not present in the scene, so work "
                    + "marshalled from a background thread was DROPPED and will not run.");
                return;
            }

            // The case the message above was written for barely happens; THIS is the one that drops
            // work. Between the dispatcher's last Update and its OnDestroy the reference is still
            // non-null and `actions` is a plain managed field, so the lock and the enqueue both succeed
            // and nothing ever drains them.
            //
            // The check shares the enqueue's critical section rather than sitting in front of it. An
            // earlier version tested the flag before taking the lock and left the window open: pass the
            // check, get pre-empted, OnDisable runs, enqueue lands on a dead queue. One uncontended
            // lock is not a reason to only half-close it.
            bool refused;

            lock (dispatcher.actions)
            {
                refused = dispatcher.isShuttingDown;

                if (!refused)
                    dispatcher.actions.Enqueue(action);
            }

            // Outside the lock: nothing arbitrary runs while it is held.
            if (refused)
                Development.LogError("MainThreadDispatcher is shutting down, so work marshalled from a "
                    + "background thread was DROPPED and will not run. A worker thread outlived the "
                    + "dispatcher - stop the thread in OnDisable rather than relying on the queue.");
        }

        void Update()
        {
            // Locked unconditionally, with no Count check in front of it. The old
            // `while (actions.Count > 0)` read Count while worker threads were enqueuing under the lock,
            // and Queue<T> is not thread-safe: a concurrent Enqueue can resize the backing array
            // mid-read, so that was undefined behaviour rather than a merely stale value. An uncontended
            // lock costs tens of nanoseconds - the check it replaces was not sound at any price.
            lock (actions)
            {
                if (actions.Count == 0)
                    return;

                // Snapshot, so the invokes below happen OUTSIDE the lock and the work is bounded to
                // whatever was queued when this frame began. The old loop re-read Count after every
                // invoke, so an action that enqueued another kept the drain alive and froze the frame.
                // Anything enqueued during the drain now runs on the next one, which is what a caller
                // would expect in any case.
                //
                // Invoking outside the lock matters for a second reason: an action that calls
                // RunOnMainThread re-enters this lock. `lock` is reentrant on one thread so it would not
                // deadlock today, but holding a lock across an arbitrary caller-supplied callback is how
                // a deadlock gets built later.
                while (actions.Count > 0)
                    draining.Add(actions.Dequeue());
            }

            // try/finally, because Clear() has to happen even if something escapes the loop. Without
            // it, an exception the catch cannot handle - a ThreadAbort, which Mono re-raises at the end
            // of a catch, or a throw from building the message itself - would leave every
            // already-invoked action in the buffer, and the next frame with queued work would append to
            // it and run them all a second time. Silent duplicate side effects are worse than the crash
            // that caused them.
            try
            {
                for (int i = 0; i < draining.Count; i++)
                {
                    try
                    {
                        draining[i].Invoke();
                    }
                    catch (Exception e)
                    {
                        // Per action, so one bad callback cannot strand the ones behind it. Before, the
                        // throw escaped Update entirely: Unity logged it with no indication of *which*
                        // queued action failed, and everything still queued waited for the next frame.
                        //
                        // Method/Target named explicitly rather than relying on the exception's own
                        // stack trace, which IL2CPP strips - identifying the culprit was the point.
                        Action failed = draining[i];

                        Development.LogError("the queued action "
                            + $"[{failed.Target?.GetType().Name ?? "static"}.{failed.Method?.Name}] threw "
                            + $"and was skipped. The work it represented did NOT happen: {e}");
                    }
                }
            }
            finally
            {
                draining.Clear();
            }
        }
    }
}
