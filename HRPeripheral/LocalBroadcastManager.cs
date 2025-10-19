using Android.Content;

namespace HRPeripheral;

public static class LocalBroadcastManager
{
    public static void Register(Context ctx, string action, System.Action<Intent> handler)
    {
        var receiver = new InlineReceiver(handler);
        ctx.RegisterReceiver(receiver, new IntentFilter(action));
        _receiver = receiver;
    }
    public static void Unregister(Context ctx, string action, System.Action<Intent> handler)
    {
        if (_receiver != null)
        {
            ctx.UnregisterReceiver(_receiver);
            _receiver = null;
        }
    }

    class InlineReceiver : BroadcastReceiver
    {
        readonly System.Action<Intent> _h;
        public InlineReceiver(System.Action<Intent> h) { _h = h; }
        public override void OnReceive(Context context, Intent intent) => _h(intent);
    }

    static BroadcastReceiver _receiver;
}