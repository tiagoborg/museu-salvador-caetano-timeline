#if UNITY_EDITOR
using UnityEngine;

namespace EnigmaVirtual.EditorTools
{
    /// <summary>
    /// Compiles the canonical <see cref="Development"/> call shapes so that a change to the logger's
    /// parameter order fails here, in one file, instead of in a client project's tree.
    /// </summary>
    /// <remarks>
    /// <para>Nothing calls this. It exists to be <b>compiled</b>, which is the whole test: since
    /// v0.41.0 the caller-info parameters make overload resolution in <c>Development</c>
    /// order-dependent, and the constraint lives in a comment that a future edit can silently
    /// violate. A comment can be broken silently; a compile error cannot, and this file is the
    /// difference.</para>
    ///
    /// <para>The string-second-argument lines are the ones that do the work, and the first version
    /// of this file had only one of them - so a reorder confined to <c>LogWarning</c> or
    /// <c>LogError</c> would have compiled clean here and broken elsewhere. That hole was caught
    /// late; a guard with a hole in it is worse than no guard, because it is trusted.</para>
    ///
    /// <para><b>The rule being enforced:</b> no overload in the <c>Log</c> family may place a
    /// parameter reachable by a <c>string</c> <i>or</i> an <c>int</c> argument in the slot
    /// immediately after <c>message</c>. <c>[CallerLineNumber] int</c> first satisfies it;
    /// <c>[CallerFilePath] string</c> first does not, and turns <c>Log("tag", "message")</c> into
    /// CS0121. No call site in this repo writes that shape - measured, after two rounds of docs
    /// asserting otherwise - so the lines below are the only place the constraint is exercised here,
    /// which is exactly why they exist.</para>
    ///
    /// <para><b>What this cannot catch:</b> the shapes that are already broken by design and have to
    /// stay that way - <c>Log("tag", someInt)</c> is CS0121, and <c>Log(message, someInt)</c> compiles
    /// while quietly binding the int to <c>line</c>. Both are listed in <c>Development.cs</c>'s header.
    /// A test that asserted them would be asserting a defect, so they are documented instead.</para>
    ///
    /// <para>Editor-only on purpose: it must never reach a player, and <c>Assets/Editor/</c> is where
    /// this project puts code that must not (see the conventions in <c>CLAUDE.md</c>).</para>
    /// </remarks>
    internal static class DevelopmentOverloadGuard
    {
        private static void CanonicalShapes()
        {
            object message = null;
            const string tag = "tag";
            bool condition = false;

            Development.Log(message);
            Development.Log(tag, message);
            Development.Log(tag, "a string message");
            Development.Log(typeof(DevelopmentOverloadGuard), message);
            Development.Log(typeof(DevelopmentOverloadGuard), tag, message);
            Development.Log(LogType.Log, message);
            Development.Log(LogType.Log, tag, message);
            Development.Log(LogType.Log, tag, "a string message");
            Development.Log(LogType.Log, typeof(DevelopmentOverloadGuard), message);

            Development.LogWarning(message);
            Development.LogWarning(tag, message);
            Development.LogWarning(tag, "a string message");
            Development.LogWarning(typeof(DevelopmentOverloadGuard), message);
            Development.LogWarning(typeof(DevelopmentOverloadGuard), tag, message);

            Development.LogError(message);
            Development.LogError(tag, message);
            Development.LogError(tag, "a string message");
            Development.LogError(typeof(DevelopmentOverloadGuard), message);
            Development.LogError(typeof(DevelopmentOverloadGuard), tag, message);
            Development.LogException(new System.Exception("shape check"));

            // Two arguments must reach the Log-level overload, three the LogType one. That split is
            // load-bearing: all 70 call sites here pass two arguments and expect Log level, not Error.
            Development.LogIf(condition, message);
            Development.LogIf(condition, message, LogType.Warning);

            // The [Obsolete] Assert forwarders must keep compiling with the same shapes, because a
            // branched project is still calling them until it migrates. The pragma is the point
            // of this block, not an oversight: the warning is the migration notice everywhere else.
#pragma warning disable 618
            Development.Assert(condition, message);
            Development.Assert(condition, message, LogType.Warning);
#pragma warning restore 618

            Development.CheckPoint();
            Development.CheckPoint(condition);
            Development.CheckPoint(LogType.Warning);

            Development.Break();
            Development.Break(message);
            Development.Break(condition, message);

            Development.TimeActionToLog(() => { }, 1);
        }
    }
}
#endif
