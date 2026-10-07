
using System;

namespace EnigmaVirtual
{

    public static class ActionExtensions
    {
        public static void SafeInvoke<T1, T2>(this Action<T1, T2> action, T1 args1, T2 args2)
        {
            if (action != null)
                action.Invoke(args1, args2);
        }

        public static void SafeInvoke<T1, T2, T3>(this Action<T1, T2, T3> action, T1 args1, T2 args2, T3 args3)
        {
            if (action != null)
                action.Invoke(args1, args2, args3);
        }

        public static void SafeInvoke<T1, T2, T3, T4>(this Action<T1, T2, T3, T4> action, T1 args1, T2 args2, T3 args3, T4 args4)
        {
            if (action != null)
                action.Invoke(args1, args2, args3, args4);
        }

        public static void SafeInvoke<T>(this Action<T> action, T args)
        {
            if (action != null)
                action.Invoke(args);
        }

        public static void SafeInvoke(this Action action)
        {
            if (action != null)
                action.Invoke();
        }

        // InvokeLater(this Action, int) and InvokeLater<T>(this Action<T>, int, T) were deleted in
        // v0.9.0. Both ran the callback on a System.Timers.Timer thread-pool thread, so any
        // Unity API inside them threw - and neither had a cancellation path, so a pending one fired
        // into destroyed objects. They had zero callers, which is why this is a deletion rather
        // than a fix: it is the smaller change, and it stops the next project reaching for a helper
        // whose name gives no hint that it cannot work. Use a coroutine.

    }
}
