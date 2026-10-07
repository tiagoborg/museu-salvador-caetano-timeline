//#define DO_LOGS // use this on classes to print logs.
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EnigmaVirtual
{
    public static class Development
    {
        static private string logFormatTag = "{0} :: {{{1}}} {2}";
        static private string logFormatTagLine = "{0} :: {{{1}}} {2}\n({4}:{3})";
        static private string logFormat = "{0} :: {1}";
        static private string logFormatLine = "{0} :: {1}\n({3}:{2})";

        // Caller info is compile-time now. It used to be four `new StackFrame(2, true)`
        // constructions - one per property, three per LogError - walked at runtime on a path that
        // ships on every platform, because the error half is unconditional. 531 call sites across
        // the repo, of which roughly half are the always-compiled LogError. Worse than the cost:
        // StackFrame carries no file or line under IL2CPP, so every one of those logs printed
        // "Unknown:0" on the targets that have no console to read instead.
        //
        // THE PARAMETER ORDER IS LOAD-BEARING: [CallerLineNumber] int comes *before*
        // [CallerFilePath] string. With the natural (string file, int line) order,
        // `Log("tag", message)` becomes ambiguous - the compiler can read it either as
        // Log(string tag, object message) or as Log(object message, string file), and neither
        // candidate wins, so ~400 call sites turn into CS0121. That collision is what deferred this
        // fix in the first place. An `int` in the second slot cannot accept a string argument, so
        // `Log("tag", "message")` has exactly one applicable candidate again.
        //
        // HOW MANY SITES THAT ACTUALLY SAVES HERE: none. The register carried "~400 call sites" as
        // the deferral's reason, and it was later repeated as ~574; both were guesses. Measured,
        // the collision needs a two-argument call whose second argument converts to
        // `string`, and only ten non-LogIf calls in this repo are two-argument at all: three in
        // SimpleSideMenu and four in the attribute drawers pass a UnityEngine.Object, and three in
        // MonoBehaviourExtensions forward a caller-info line number. None passes a string, so none
        // was ever ambiguous under either order. (Count them with a parser, not a grep - a grep
        // counts the commas inside string.Concat(...) and has produced a wrong answer here twice.) The order is still the right one: `Log(tag, "message")` is ordinary in project code
        // even though the framework does not write it, and a branched project would have met the
        // full set at once. But the justification is "a hazard avoided for branched projects", not
        // "a fire put out here".
        //
        // THE MIRROR OF IT IS STILL OPEN, and this is the sentence to read before changing the order
        // back. `Log("tag", someInt)` is now CS0121: the tag overload wants (string, object) and the
        // message overload wants (object, int), each is better in one argument, and both substitute
        // defaults - so neither wins. It bound to the tag overload before this change. The collision
        // set is small and integral: int, char, short, byte, sbyte, ushort. long, float, double and
        // decimal do not convert to int, so they are unaffected.
        //
        // No call site in this repo does it, but `Development.Log("Downloaded", list.Count)` is an
        // ordinary line for a branched project, so the workaround belongs here rather than in a
        // commit message: pass a string. `Log($"Downloaded {list.Count}")`, or `list.Count.ToString()`,
        // or cast - `(object)list.Count`. Adding (string, int) overloads would fix the common half and
        // leave char and short failing the same way, which is worse than one rule that always holds.
        // The failure is a compile error in the branched project, not a wrong log at runtime.
        //
        // TWO MORE THINGS THE ORDER COSTS, both found by compiling the shapes rather than reasoning
        // about them:
        //
        //   * `Action<object> f = Development.LogError;` is now CS0123. Optional parameters are not
        //     substituted in a method-group conversion, so no Development method with caller info can
        //     be assigned to a delegate any more. Nothing here does; a branched project wiring the
        //     logger into a callback would. Wrap it - `o => Development.LogError(o)`.
        //
        //   * `Log(message, someInt)` used to be CS1503 and now COMPILES, binding the int to `line`
        //     and dropping it from the message. That is the one failure in this area that is silent,
        //     so it is the one worth remembering: the second argument to a single-message overload is
        //     not a second message.
        //
        // Both are why DevelopmentOverloadGuard.cs exists under Assets/Editor: it compiles the
        // canonical shapes, so reordering these parameters fails there rather than in a client's tree.
        //
        // The same collision is why `LogIf`'s and `CheckPoint`'s LogType parameters no longer carry
        // a default - and why the two [Obsolete] `Assert` forwarders must not carry one
        // either. Restoring a default on any of the four is CS0121 in every branched project.
        // DevelopmentOverloadGuard compiles all four shapes, so that fails there first. `Assert(cond, msg)` used to resolve to the two-parameter overload purely
        // because it was the only candidate needing no default substituted; once *both* candidates
        // substitute defaults, that tie-break stops applying and the call is ambiguous. Requiring
        // the LogType argument keeps the two overloads apart by arity. No call site passed it
        // implicitly - a two-argument LogIf always reached the two-parameter overload, which logs
        // at Log level.
        private const string UnknownFrame = "Unknown";

        /// <summary>Null-safe message rendering. LogError/LogException are always compiled, so
        /// they must not be able to throw on a null argument.</summary>
        /// <remarks>
        /// Used by the conditional half too. It was not, and
        /// <c>message.ToString()</c> on a null argument threw a NullReferenceException out of the
        /// logger - on every Standalone and Android build, since those are exactly the targets where
        /// <c>DO_LOGS</c> is defined and the call therefore survives. A logger must
        /// never be able to throw, and it does not matter which half of the file it lives in.
        /// </remarks>
        private static string Describe(object message)
        {
            return message == null ? "null" : message.ToString();
        }

        /// <summary>
        /// The class name for the log prefix, taken from the file name rather than from a type.
        /// </summary>
        /// <remarks>
        /// This is a behaviour change, and a deliberate one - but not a free one, and the first
        /// version of this remark claimed it was. The old <c>CurrentClass</c> walked to the declaring
        /// type and preferred its outer type; the file name agrees with that for most of this
        /// codebase and gives a real answer under IL2CPP, where the type walk returned
        /// <c>Unknown</c> for everything.
        ///
        /// <para>Where it disagrees, measured rather than assumed - 15 call sites across 6 files, all
        /// of them files declaring more than one type or a type whose name differs from the file's.
        /// The one worth knowing about is <c>Api.cs</c>, which declares <c>Api</c>,
        /// <c>APIParameters</c> and <c>APIResponseModel</c>: six response-parsing errors that used to
        /// read <c>APIResponseModel ::</c> now read <c>Api ::</c>, which attributes a body-parsing
        /// failure to the HTTP layer. The file and line in the same message still point at the right
        /// place. The others are three attribute/drawer pairs, <c>RawKey.cs</c> (<c>RawKeyExtensions</c>)
        /// and <c>AssetList.cs</c> declaring <c>AssetsList</c>.</para>
        /// </remarks>
        private static string ClassOf(string callerFilePath)
        {
            string name = FileOf(callerFilePath);

            if (name == UnknownFrame)
                return UnknownFrame;

            int dot = name.LastIndexOf('.');

            return dot > 0 ? name.Substring(0, dot) : name;
        }

        /// <summary>
        /// The file name for the log's location suffix - the name, not the path.
        /// </summary>
        /// <remarks>
        /// <c>[CallerFilePath]</c> is the **build machine's** absolute path, baked into every one of
        /// ~566 call sites as a literal, and <c>LogError</c> is always compiled - so the full path
        /// would ship inside every player and print into <c>Player.log</c> on the kiosk. The old
        /// <c>StackFrame</c> route disclosed nothing there, because a release player has no PDBs and
        /// returned <c>Unknown</c>. The leak is Low severity - it reaches disk on the
        /// machine and no further, and anyone who can read that log can already read the plaintext
        /// password in the shipped settings.json - but the folder is redundant beside
        /// <see cref="ClassOf"/>, so it costs nothing to drop.
        /// </remarks>
        private static string FileOf(string callerFilePath)
        {
            if (string.IsNullOrEmpty(callerFilePath))
                return UnknownFrame;

            // NOT Path.GetFileName. `Path` splits on the separator of whatever
            // platform is *running*; `[CallerFilePath]` carries the separator of whatever platform
            // *compiled*. On Android those disagree - Mono's DirectorySeparatorChar is '/', the baked
            // literal is `C:\Work\...\Core.cs`, no '/' is found, and the whole build-machine path is
            // returned as the "file name". Every log line on the device read
            // `C:\Work\claude\unity_default_6\Assets\...\Core :: Load: settings.json`, which is
            // precisely the disclosure the Path.GetFileName call was added to prevent (v0.41.0).
            //
            // Measured on an SM-T510, Android 11, IL2CPP - not reasoned about. It is the
            // hardcoded-separator problem from the other direction: there a literal in a path, here a
            // path helper trusting the runtime's separator for a compile-time string. Split on both
            // and the question does not arise.
            int cut = callerFilePath.LastIndexOfAny(PathSeparators);
            string name = cut >= 0 ? callerFilePath.Substring(cut + 1) : callerFilePath;

            return string.IsNullOrEmpty(name) ? UnknownFrame : name;
        }

        /// <summary>Both separators, because the build machine's and the device's need not agree.</summary>
        /// <remarks>
        /// <c>StringUtil.GetFileNameFromUrl</c> is the same four lines over the same array, and this is
        /// still not calling it: <c>StringUtil</c> calls <c>Development</c> in five places, so the
        /// logger taking a dependency on it puts a class that logs underneath the thing it logs into.
        /// A duplicated <c>LastIndexOfAny</c> is the cheaper of the two problems. The duplication is
        /// deliberate, for the reason above - not an oversight.
        /// </remarks>
        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// Logs a message o type LogType to the Unity Console.
        /// </summary>
        /// <param name="logType">Type of log to be sisplayed.</param>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(LogType logType, Type callertype, object message)
        {
            string text = string.Format(logFormat, callertype?.ToString() ?? UnknownFrame, Describe(message));

            if (logType == LogType.Assert || logType == LogType.Error)
                UnityEngine.Debug.LogError(text);
            else if (logType == LogType.Log)
                UnityEngine.Debug.Log(text);
            else if (logType == LogType.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.LogWarning(string.Format(logFormat, "Development", string.Format("Undefined logType \"{0}\"", logType.ToString())));
        }

        /// <summary>
        /// Logs a message of type LogType to the Unity Console.
        /// </summary>
        /// <param name="logType">Type of log to be sisplayed.</param>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(LogType logType, string tag, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            string text = string.Format(logFormatTagLine, ClassOf(file), tag, Describe(message), line, FileOf(file));

            if (logType == LogType.Assert || logType == LogType.Error)
                UnityEngine.Debug.LogError(text);
            else if (logType == LogType.Log)
                UnityEngine.Debug.Log(text);
            else if (logType == LogType.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.LogWarning(string.Format(logFormat, "Development", string.Format("Undefined logType \"{0}\"", logType.ToString())));
        }

        /// <summary>
        /// Logs a message of type LogType to the Unity Console.
        /// </summary>
        /// <param name="logType">Type of log to be sisplayed.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(LogType logType, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            string text = string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file));

            if (logType == LogType.Assert || logType == LogType.Error)
                UnityEngine.Debug.LogError(text);
            else if (logType == LogType.Log)
                UnityEngine.Debug.Log(text);
            else if (logType == LogType.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.LogWarning(string.Format(logFormat, "Development", string.Format("Undefined logType \"{0}\"", logType.ToString())));
        }

        /// <summary>
        /// Logs a message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(Type callertype, object message)
        {
            UnityEngine.Debug.Log(string.Format(logFormat, callertype?.ToString() ?? UnknownFrame, Describe(message)));
        }

        /// <summary>
        /// Logs a message to the Unity Console.
        /// </summary>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(string tag, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.Log(string.Format(logFormatTagLine, ClassOf(file), tag, Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(Type callertype, string tag, object message)
        {
            UnityEngine.Debug.Log(string.Format(logFormatTag, callertype?.ToString() ?? UnknownFrame, tag, Describe(message)));
        }

        /// <summary>
        /// Logs a message to the Unity Console.
        /// </summary>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Log(object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.Log(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a warning message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void LogWarning(Type callertype, object message)
        {
            UnityEngine.Debug.LogWarning(string.Format(logFormat, callertype?.ToString() ?? UnknownFrame, Describe(message)));
        }

        /// <summary>
        /// Logs a warning message to the Unity Console.
        /// </summary>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void LogWarning(string tag, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.LogWarning(string.Format(logFormatTagLine, ClassOf(file), tag, Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a warning message to the Unity Console.
        /// </summary>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void LogWarning(object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.LogWarning(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a warning message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void LogWarning(Type callertype, string tag, object message)
        {
            UnityEngine.Debug.LogWarning(string.Format(logFormatTag, callertype?.ToString() ?? UnknownFrame, tag, Describe(message)));
        }

        // ---------------------------------------------------------------------------------
        // ALWAYS COMPILED - do not add [Conditional("DO_LOGS")] to anything below this line.
        //
        // DO_LOGS is only defined for Android and Standalone, so a conditional error logger
        // means iOS, WebGL, macOS, Linux and UWP builds report nothing at all - the call site
        // is removed, not just silenced. Verbose logging above stays conditional.
        //
        // LogIf() must STAY conditional: its arguments are
        // stripped with the call, and all 70 call sites pass Core.settings.<Module>.doLogs, which
        // is null whenever settings parsing failed. Making it unconditional would spread that
        // NullReferenceException to every platform. Verified: none of those 70 sites passes
        // LogType.Error, so keeping it conditional costs no error reporting whatsoever.
        //
        // TRAP: Log(LogType, ...) and LogIf(cond, msg, LogType) above route LogType.Error to
        // Debug.LogError yet stay conditional, so an error written through them is invisible on
        // any platform without DO_LOGS. Nothing does that today. If you need a guaranteed error,
        // call LogError directly - never pass LogType.Error to a conditional overload.
        // ---------------------------------------------------------------------------------
        /// <summary>
        /// Logs a error message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        public static void LogError(Type callertype, object message)
        {
            UnityEngine.Debug.LogError(string.Format(logFormat, callertype?.ToString() ?? UnknownFrame, Describe(message)));
        }

        /// <summary>
        /// Logs a error message to the Unity Console.
        /// </summary>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        public static void LogError(string tag, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.LogError(string.Format(logFormatTagLine, ClassOf(file), tag, Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a error message to the Unity Console.
        /// </summary>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        public static void LogError(object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.LogError(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a error message to the Unity Console.
        /// </summary>
        /// <param name="callertype">Caller class type object.</param>
        /// <param name="tag">Message tag for extra readability.</param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        public static void LogError(Type callertype, string tag, object message)
        {
            UnityEngine.Debug.LogError(string.Format(logFormatTag, callertype?.ToString() ?? UnknownFrame, tag, Describe(message)));
        }

        /// <summary>
        /// Logs a exception message to the Unity Console.
        /// </summary>
        /// <param name="exception">String or object to be converted to string representation for display.</param>
        public static void LogException(Exception exception)
        {
            if (exception == null)
            {
                UnityEngine.Debug.LogError("LogException called with a null exception.");
                return;
            }

            UnityEngine.Debug.LogException(exception);
        }

        /// <summary>
        /// Logs <paramref name="message"/> at <c>Log</c> level when <paramref name="condition"/> is
        /// <b>true</b>.
        /// </summary>
        /// <remarks>
        /// <para><b>This was called <c>Assert</c>, and that name described none of it.</b> An
        /// assert fires when a condition is <i>false</i> and reports a failure; this fires when the
        /// condition is <b>true</b> and reports at <c>Log</c> level. Every one of the 70 call sites in
        /// this repo passes <c>Core.settings.&lt;Module&gt;.doLogs</c>, so what they all mean is "log
        /// this if logging is on for my module" - which is exactly a <c>LogIf</c> and never an
        /// assertion. The old name sent people looking for a failed invariant that had not
        /// happened.</para>
        ///
        /// <para><b>Still <c>[Conditional("DO_LOGS")]</c>, and that is load-bearing.</b> The arguments
        /// are stripped with the call, and nearly every site passes
        /// <c>Core.settings.&lt;Module&gt;.doLogs</c> - which throws when settings parsing failed.
        /// Making it unconditional would spread that NullReferenceException to every platform.
        /// So never put one of these on a path that can run before the boot chain completes.</para>
        /// </remarks>
        [Conditional("DO_LOGS")]
        public static void LogIf(bool condition, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!condition)
                return;

            UnityEngine.Debug.Log(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
        }

        /// <summary>
        /// Logs <paramref name="message"/> at <paramref name="logType"/> when
        /// <paramref name="condition"/> is <b>true</b>. See <see cref="LogIf(bool, object, int, string)"/>
        /// for why this is not called Assert.
        /// </summary>
        /// <param name="logType">Required, and not defaulted - see the note on parameter order at the
        /// top of this file. Note the TRAP below: an Error routed through here is still stripped
        /// without DO_LOGS.</param>
        [Conditional("DO_LOGS")]
        public static void LogIf(bool condition, object message, LogType logType,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!condition)
                return;

            string text = string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file));

            if (logType == LogType.Error)
                UnityEngine.Debug.LogError(text);
            else if (logType == LogType.Log)
                UnityEngine.Debug.Log(text);
            else if (logType == LogType.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.LogWarning(string.Format(logFormat, "Development", string.Format("Undefined logType \"{0}\"", logType.ToString())));
        }

        /// <summary>Obsolete. Renamed to <see cref="LogIf(bool, object, int, string)"/>.</summary>
        /// <remarks>
        /// Kept as a forwarder rather than deleted, following <c>StringUtil.ToClock</c> and
        /// <c>Timer.SetDuration</c>: every branched project calls this, and an <c>[Obsolete]</c>
        /// warning at each of their call sites is the migration notice. The caller-info arguments are
        /// passed through explicitly - without that, the forwarder's own line would be reported
        /// instead of the caller's.
        /// </remarks>
        [Obsolete("Renamed to LogIf - it logs when the condition is TRUE, at Log level, and was never "
            + "an assert (M9). Same arguments, same behaviour. This forwarder is removed in v0.50.")]
        [Conditional("DO_LOGS")]
        public static void Assert(bool condition, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            LogIf(condition, message, line, file);
        }

        /// <summary>Obsolete. Renamed to <see cref="LogIf(bool, object, LogType, int, string)"/>.</summary>
        [Obsolete("Renamed to LogIf - it logs when the condition is TRUE and was never an assert "
            + "(M9). Same arguments, same behaviour. This forwarder is removed in v0.50.")]
        [Conditional("DO_LOGS")]
        public static void Assert(bool condition, object message, LogType logType,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            LogIf(condition, message, logType, line, file);
        }

        /// <summary>
        /// Executes and measures (time) of a method execution over N iterations.
        /// Usage:
        /// Console.WriteLine(TimeAction(() => DoStuff(), 1000));
        /// </summary>
        /// <param name="action">Method to test.</param>
        /// <param name="iterations">Number of iterations to execute.</param>
        /// <returns></returns>
        public static long TimeAction(Action action, int iterations)
        {
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
                action();

            sw.Stop();

            return sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// Executes and measures (time) of a method execution over N iterations directly to output log.
        /// Usage:
        /// TimeActionToLog(() => DoStuff(), 1000);
        /// </summary>
        /// <param name="action">Method to test.</param>
        /// <param name="iterations">Number of iterations to execute.</param>
        [Conditional("DO_LOGS")]
        public static void TimeActionToLog(System.Action action, int iterations,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            // logFormat takes two arguments, so the third used to be silently discarded and the
            // measurement never carried a line number. It reports one now.
            UnityEngine.Debug.Log(string.Format(logFormatLine, ClassOf(file), TimeAction(action, iterations), line, FileOf(file)));
        }

        /// <summary>
        /// Pauses the Editor.
        /// </summary>
        [Conditional("DO_LOGS")]
        public static void Break()
        {
            UnityEngine.Debug.Break();
        }

        /// <summary>
        /// Pauses the Editor throwing a error message.
        /// </summary>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Break(object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.LogError(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
            UnityEngine.Debug.Break();
        }

        /// <summary>
        /// Pauses the Editor throwing a error message if condition is meet.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("DO_LOGS")]
        public static void Break(bool condition, object message,
            [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!condition)
                return;

            UnityEngine.Debug.LogError(string.Format(logFormatLine, ClassOf(file), Describe(message), line, FileOf(file)));
            UnityEngine.Debug.Break();
        }

        /// <summary>
        /// Logs a message showing current class, method and code line to the Unity Console.
        /// </summary>
        /// <remarks>
        /// Note the new accidental surface: <c>CheckPoint("some label")</c> compiles, and silently
        /// supplies <c>member</c> instead of the caller's name. Nothing does that here. If you want a
        /// label, <c>Log</c> is the method for it.
        /// <para><b>CheckPoint declares its caller parameters in a different order from the Log
        /// family</b> - <c>[CallerMemberName] string</c> first, rather than <c>[CallerLineNumber]
        /// int</c>. That is safe only because CheckPoint's siblings take <c>bool</c> and
        /// <c>LogType</c>, neither of which a <c>string</c> argument can reach, so there is no
        /// collision to dodge. Do not copy this order onto anything in the Log family, and do not
        /// "normalise" the Log family onto it - see the header comment for what that costs.</para>
        /// <para>Unmentioned bonus of <c>[CallerMemberName]</c>: inside a coroutine this now prints the
        /// iterator method's real name. <c>StackFrame.GetMethod().Name</c> printed <c>MoveNext</c>, which
        /// is what every <c>CheckPoint</c> in <c>VideoPlayerControllerBase</c> and
        /// <c>DownloadableImage</c> reported.</para>
        /// </remarks>
        [Conditional("DO_LOGS")]
        public static void CheckPoint(
            [CallerMemberName] string member = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            UnityEngine.Debug.Log(string.Format(logFormatLine, ClassOf(file), MemberOf(member), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a message showing current class, method and code line to the Unity Console.
        /// </summary>
        /// <param name="condition"></param>
        [Conditional("DO_LOGS")]
        public static void CheckPoint(bool condition,
            [CallerMemberName] string member = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!condition)
                return;

            UnityEngine.Debug.Log(string.Format(logFormatLine, ClassOf(file), MemberOf(member), line, FileOf(file)));
        }

        /// <summary>
        /// Logs a message showing current class, method and code line to the Unity Console.
        /// </summary>
        /// <param name="logType">Required, and no longer defaulted - see the note on parameter order
        /// at the top of this file.</param>
        [Conditional("DO_LOGS")]
        public static void CheckPoint(LogType logType,
            [CallerMemberName] string member = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            string text = string.Format(logFormatLine, ClassOf(file), MemberOf(member), line, FileOf(file));

            if (logType == LogType.Error)
                UnityEngine.Debug.LogError(text);
            else if (logType == LogType.Log)
                UnityEngine.Debug.Log(text);
            else if (logType == LogType.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.LogWarning(string.Format(logFormat, "Development", string.Format("Undefined logType \"{0}\"", logType.ToString())));
        }

        private static string MemberOf(string callerMemberName)
        {
            return string.IsNullOrEmpty(callerMemberName) ? UnknownFrame : callerMemberName;
        }
    }
}
