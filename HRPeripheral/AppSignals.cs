// Central place to keep "well-known" app-wide signal/action names.
// Keeping these in one file avoids typos in string literals sprinkled across the codebase.
// Typical usage: Intents and BroadcastReceivers that need to agree on an action string.

namespace HRPeripheral
{
    /// <summary>
    /// Static container for app-wide intent action names (a.k.a. "signals").
    /// Use these constants instead of hard-coding strings in multiple places.
    /// </summary>
    public static class AppSignals
    {
        /// <summary>
        /// Broadcast/intent action that instructs the app to immediately close.
        /// Expected flow:
        ///  - A component (e.g., a notification action or service) sends an Intent with this action.
        ///  - A BroadcastReceiver or Activity listens for it and performs a graceful shutdown
        ///    (e.g., stop services, finish activities, remove task).
        ///
        /// Notes:
        ///  - Using a unique, app-scoped name ("hrperipheral.action.FORCE_CLOSE") avoids collisions
        ///    with system or other apps’ actions.
        ///  - Keep the value stable; changing it will break any existing filters referencing it.
        /// </summary>
        public const string ActionForceClose = "hrperipheral.action.FORCE_CLOSE";
    }
}