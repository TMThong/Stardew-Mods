using System;
using System.Reflection;
using StardewModdingAPI;

namespace StardewConnect.Utilities
{
    /// <summary>
    /// Copies text to the OS clipboard through Stardew Valley's own <c>DesktopClipboard</c>.
    ///
    /// The call is made by reflection on purpose: the helper lives in the game assembly and its
    /// signature has moved between releases, and a missing clipboard must never take the mod
    /// (or the menu the player is looking at) down with it.
    /// </summary>
    internal static class ClipboardHelper
    {
        private static IMonitor monitor;
        private static MethodInfo setTextMethod;
        private static bool resolved;

        public static void Initialise(IMonitor logger)
        {
            monitor = logger;
        }

        /// <summary>True when a clipboard implementation was found.</summary>
        public static bool IsAvailable => Resolve() != null;

        /// <summary>Copies <paramref name="text"/> to the clipboard. Returns false when unavailable.</summary>
        public static bool TrySetText(string text)
        {
            if (text == null)
                text = string.Empty;

            MethodInfo method = Resolve();
            if (method == null)
                return false;

            try
            {
                method.Invoke(null, new object[] { text });
                return true;
            }
            catch (Exception error)
            {
                monitor?.Log($"Could not copy to the clipboard: {error.InnerException?.Message ?? error.Message}", LogLevel.Warn);
                return false;
            }
        }

        private static MethodInfo Resolve()
        {
            if (resolved)
                return setTextMethod;

            resolved = true;
            try
            {
                Type clipboardType = Type.GetType("StardewValley.DesktopClipboard, Stardew Valley", throwOnError: false)
                    ?? FindTypeInLoadedAssemblies("StardewValley.DesktopClipboard");

                setTextMethod = clipboardType?.GetMethod(
                    "SetText",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null);

                if (setTextMethod == null)
                    monitor?.Log("No clipboard implementation found; copy buttons will be disabled.", LogLevel.Debug);
            }
            catch (Exception error)
            {
                monitor?.Log($"Could not resolve the clipboard helper: {error.Message}", LogLevel.Debug);
                setTextMethod = null;
            }

            return setTextMethod;
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(fullName, throwOnError: false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
