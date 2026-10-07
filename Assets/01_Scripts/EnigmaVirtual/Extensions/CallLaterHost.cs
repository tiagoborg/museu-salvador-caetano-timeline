using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// The persistent coroutine host behind
    /// <see cref="MonoBehaviourExtensions.CallLater(MonoBehaviour, float, System.Action, int, string)"/>.
    /// </summary>
    /// <remarks>
    /// <para>Its own file, and empty on purpose. A delayed call must outlive its caller being
    /// deactivated - <c>Invoke</c> did, and <c>AppInfo</c>'s boot-critical <c>Hide</c> depends on it
    /// - so the coroutine cannot run on the caller. This object is created on first use under
    /// <c>DontDestroyOnLoad</c> and never deactivated, which is the only state a coroutine needs to
    /// keep ticking.</para>
    ///
    /// <para>Nothing should add this component by hand.</para>
    /// </remarks>
    [AddComponentMenu("")]
    internal sealed class CallLaterHost : MonoBehaviour
    {
    }
}
