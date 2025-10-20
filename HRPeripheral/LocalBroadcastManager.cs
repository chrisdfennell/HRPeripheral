using Android.Content;

namespace HRPeripheral;

/// <summary>
/// A minimal re-implementation of AndroidX.LocalBroadcastManager,
/// simplified for internal app events inside the HRPeripheral app.
///
/// ⚙️ Purpose:
/// Allows components (like Activities or Services) to register lightweight
/// in-process broadcast receivers without exposing them system-wide.
/// This avoids unnecessary permissions and global broadcasts.
///
/// Typical usage:
/// ```csharp
/// LocalBroadcastManager.Register(this, AppSignals.ActionForceClose, intent => Finish());
/// ...
/// LocalBroadcastManager.Unregister(this, AppSignals.ActionForceClose, handler);
/// ```
///
/// Notes:
///  • Only supports one receiver at a time (static field `_receiver`).
///    This is fine for simple apps, but could be extended to handle
///    multiple actions/handlers using a dictionary.
///  • Broadcasts are delivered immediately on the main thread.
///  • Uses app-local actions (strings defined in <see cref="AppSignals"/>).
/// </summary>
public static class LocalBroadcastManager
{
    // ========================================================================
    // SINGLETON RECEIVER STORAGE
    // ========================================================================
    // The currently active receiver instance (if any).
    // We only store one for simplicity. If you want multiple handlers per action,
    // this could become a Dictionary<string, List<BroadcastReceiver>>.
    static BroadcastReceiver? _receiver;

    // ========================================================================
    // REGISTER
    // ========================================================================
    /// <summary>
    /// Registers a new inline broadcast receiver for the given action string.
    /// The supplied <paramref name="handler"/> delegate will be invoked whenever
    /// an Intent with that action is broadcasted via Context.SendBroadcast().
    /// </summary>
    /// <param name="ctx">The current Android context (Activity or Service).</param>
    /// <param name="action">The Intent action string to listen for.</param>
    /// <param name="handler">Delegate to execute when the broadcast is received.</param>
    public static void Register(Context ctx, string action, System.Action<Intent> handler)
    {
        // Create an inline receiver that wraps the handler delegate.
        var receiver = new InlineReceiver(handler);

        // Register it with the Android system to listen for our given action.
        ctx.RegisterReceiver(receiver, new IntentFilter(action));

        // Store reference so we can cleanly unregister later.
        _receiver = receiver;
    }

    // ========================================================================
    // UNREGISTER
    // ========================================================================
    /// <summary>
    /// Unregisters the previously registered broadcast receiver, if any.
    /// </summary>
    /// <param name="ctx">The current Android context.</param>
    /// <param name="action">The action string (not used in this simplified version).</param>
    /// <param name="handler">The handler delegate originally registered.</param>
    public static void Unregister(Context ctx, string action, System.Action<Intent> handler)
    {
        if (_receiver != null)
        {
            // Unregister the receiver from the Android framework.
            ctx.UnregisterReceiver(_receiver);
            _receiver = null; // Clear stored reference to avoid memory leaks.
        }
    }

    // ========================================================================
    // INLINE RECEIVER
    // ========================================================================
    /// <summary>
    /// Inline wrapper that turns an Action&lt;Intent&gt; delegate into a proper
    /// BroadcastReceiver subclass so it can be registered with the system.
    /// </summary>
    class InlineReceiver : BroadcastReceiver
    {
        readonly System.Action<Intent> _h; // handler delegate

        /// <summary>
        /// Constructs an inline receiver with a given delegate.
        /// </summary>
        public InlineReceiver(System.Action<Intent> h)
        {
            _h = h;
        }

        /// <summary>
        /// Invoked by Android when a broadcast matching our IntentFilter arrives.
        /// Simply delegates to the stored handler.
        /// </summary>
        public override void OnReceive(Context context, Intent intent)
        {
            _h(intent);
        }
    }
}