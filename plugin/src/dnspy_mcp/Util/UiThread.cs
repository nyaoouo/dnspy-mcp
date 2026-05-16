#if DNSPY
using System;

namespace DnspyMcp.Util;

internal static class UiThread
{
    public static void Invoke(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) { action(); return; }
        dispatcher.Invoke(action);
    }

    public static T Invoke<T>(Func<T> func)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) return func();
        return dispatcher.Invoke(func);
    }
}
#endif
