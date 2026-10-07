using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// The type-safe replacement for <c>Invoke("MethodName", delay)</c>.
    /// </summary>
    /// <remarks>
    /// <para><c>Invoke</c> takes a method <i>name</i>, so a rename compiles clean and fails at
    /// runtime, the target cannot be <c>private</c> without becoming a stripping candidate under
    /// IL2CPP, and nothing tells you the method is called at all. A method group has none of those
    /// problems.</para>
    ///
    /// <para><b>The timer does not live on the caller, and that is the whole design.</b> The obvious
    /// implementation - <c>caller.StartCoroutine(...)</c> - is <i>not</i> a drop-in for
    /// <c>Invoke</c>, and the difference breaks the boot chain. Measured, in play mode: a pending
    /// <c>Invoke</c> still fires after its GameObject is deactivated, while <c>StartCoroutine</c>
    /// cannot even be called on an inactive object and a running coroutine is killed for good by
    /// <c>SetActive(false)</c>, without its <c>finally</c> running. <c>AppInfo</c> depends on
    /// exactly that: it deactivates itself, and the delayed <c>Hide</c> it schedules afterwards is
    /// the only forward link from the splash screen to <c>LoadScene</c>. A caller-hosted coroutine
    /// left the app on the preloader with <c>BOOT STALLED</c>. So the coroutine runs on a private
    /// persistent host instead, and only the caller being <b>destroyed</b> cancels it - which is
    /// what <c>Invoke</c> does too.</para>
    ///
    /// <para><b>The <c>ref</c> overload is the one to reach for.</b> <c>Invoke</c> without a matching
    /// <c>CancelInvoke</c> <i>stacks</i>: call it twice and two callbacks are pending, so the first
    /// timer fires against the second call's state. That was live in two of the six call sites this
    /// replaced - most visibly <c>NotifyController</c>, where a 5s notification followed by a 10s one
    /// was hidden after 5s by the first notification's timer. Passing the handle makes each call
    /// replace the previous one.</para>
    ///
    /// <para><b>Do not add a second delegate-type overload.</b> Taking a <c>UnityAction</c> or an
    /// <c>Action&lt;T&gt;</c> beside this would make every method-group call site <c>CS0121</c> at
    /// once, because a method group converts to both. <c>Action</c> only. (The two overloads here
    /// are safe from that and from the default-argument trap: they differ in arity, <c>ref</c>
    /// must be spelled at the call site, and a <c>ref</c> parameter cannot carry a default.)</para>
    ///
    /// <para><b>Not covered:</b> <c>CancelInvoke</c> and <c>IsInvoking</c> do not see these. Nothing
    /// in this repo calls either, but a branched project that cancels a timer that way will now
    /// compile clean and cancel nothing. Stop the returned handle instead.</para>
    /// </remarks>
    public static class MonoBehaviourExtensions
    {
        /// <summary>
        /// Calls <paramref name="action"/> after <paramref name="delay"/> seconds of scaled time -
        /// the same clock <c>Invoke</c> used.
        /// </summary>
        /// <remarks>
        /// Survives the caller being deactivated; cancelled by the caller being destroyed. Always
        /// defers at least one frame, as <c>Invoke</c> did, so a zero delay does not run the action
        /// inside this call.
        /// </remarks>
        /// <returns>
        /// A handle that can be passed to <see cref="StopCallLater"/> - or <c>null</c> if the call
        /// could not be scheduled, which is always reported. Never assume it is non-null.
        /// </returns>
        public static Coroutine CallLater(this MonoBehaviour behaviour, float delay, Action action,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            // Caller info is forwarded to every report below. Without it Development's [CallerFilePath]
            // resolves to this file, so all six call sites produced the same untraceable line.
            if (behaviour == null)
            {
                Development.LogError("CallLater on a null or destroyed MonoBehaviour - the delayed "
                    + "call is dropped and will not happen.", line, file);
                return null;
            }

            if (action == null)
            {
                Development.LogError("CallLater with a null action on '" + behaviour.name
                    + "' - the delayed call is dropped.", line, file);
                return null;
            }

            if (!Application.isPlaying)
            {
                Development.LogError("CallLater from '" + behaviour.name + "' outside play mode - "
                    + "coroutines do not run in edit mode, so the delayed call is dropped.", line, file);
                return null;
            }

            return Host().StartCoroutine(CallLater_IE(behaviour, delay, action));
        }

        /// <summary>
        /// As <see cref="CallLater(MonoBehaviour, float, Action, int, string)"/>, but replaces the
        /// call already pending in <paramref name="pending"/> instead of stacking a second one
        /// beside it.
        /// </summary>
        /// <param name="pending">
        /// <b>A stop-token, not a pending flag.</b> It is never cleared when the call completes and
        /// cannot be - C# forbids a <c>ref</c> parameter inside an iterator or a closure - so a
        /// non-null handle means "something was started once", never "something is still due".
        /// Testing it to decide whether a callback is outstanding will be wrong. Keep a separate
        /// bool if the caller needs to know that.
        /// </param>
        public static void CallLater(this MonoBehaviour behaviour, float delay, Action action,
            ref Coroutine pending,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            // Started before the old one is stopped, so a scheduling failure cannot turn "replace"
            // into "cancel both" - the previous call keeps running if the new one never starts.
            Coroutine started = behaviour.CallLater(delay, action, line, file);

            if (started != null && pending != null)
                StopCallLater(pending);

            if (started != null)
                pending = started;
        }

        /// <summary>
        /// Cancels a call scheduled by <see cref="CallLater(MonoBehaviour, float, Action, int, string)"/>.
        /// </summary>
        /// <remarks>
        /// Must go through here rather than the caller's own <c>StopCoroutine</c>: the coroutine is
        /// running on the persistent host, not on the caller, and Unity only stops a coroutine from
        /// the MonoBehaviour that started it. Safe to call with <c>null</c> or with a handle whose
        /// call has already run.
        /// </remarks>
        public static void StopCallLater(Coroutine pending)
        {
            if (pending != null && _host != null)
                _host.StopCoroutine(pending);
        }

        /// <summary>
        /// The persistent coroutine host, created on first use.
        /// </summary>
        /// <remarks>
        /// <c>DontDestroyOnLoad</c> and <b>no</b> <c>HideFlags</c>, deliberately. The first version
        /// used <c>HideAndDontSave</c>, which in the editor survives leaving play mode - four hosts
        /// had accumulated after four runs, and each one kept its coroutines. It is only ever created
        /// while playing (see the <c>Application.isPlaying</c> guard), so nothing can serialise it
        /// into a scene, and leaving it visible under DontDestroyOnLoad means a stuck delayed call
        /// can be found in the hierarchy instead of being invisible.
        /// </remarks>
        private static CallLaterHost _host;

        private static CallLaterHost Host()
        {
            // Unity's fake-null, so a host destroyed by leaving play mode is replaced rather than
            // returned as a dead reference.
            if (_host != null)
                return _host;

            GameObject go = new GameObject("~CallLater");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<CallLaterHost>();
            return _host;
        }

        private static IEnumerator CallLater_IE(MonoBehaviour behaviour, float delay, Action action)
        {
            // At least one frame even at zero, matching Invoke. WaitForSeconds(0) already yields a
            // frame, but a negative serialized delay would not.
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            else
                yield return null;

            // Unity's fake-null: true once the caller has been destroyed. Invoke is cancelled by
            // destruction too, so stopping here matches it - and without this the action would run
            // against a destroyed target and throw MissingReferenceException on its first member
            // access, which is not something the caller can catch.
            if (behaviour == null)
                yield break;

            action();
        }
    }
}
