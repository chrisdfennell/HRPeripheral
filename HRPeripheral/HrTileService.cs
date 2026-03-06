using Android.App;
using Android.Content;
using AndroidX.Concurrent.Futures;
using Google.Common.Util.Concurrent;
using SysDebug = System.Diagnostics.Debug;
using SysException = System.Exception;

// Use the Tiles namespace for all builder types (deprecated but functional in C# bindings)
#pragma warning disable CS0618 // Suppress obsolete warnings for Tiles builder APIs
using AndroidX.Wear.Tiles;
#pragma warning restore CS0618

namespace HRPeripheral;

/// <summary>
/// Wear OS tile that shows current heart rate, zone, and calories at a glance.
/// Reads cached data from SharedPreferences (written by HeartRateService).
/// </summary>
[Service(
    Exported = true,
    Permission = "com.google.android.wearable.permission.BIND_TILE_PROVIDER")]
[IntentFilter(new[] { "androidx.wear.tiles.action.BIND_TILE_PROVIDER" })]
#pragma warning disable CS0618
public class HrTileService : TileService
{
    private const string TAG = "HRP/HrTile";
    private const string RESOURCES_VERSION = "1";

    protected override IListenableFuture OnTileRequest(RequestBuilders.TileRequest requestParams)
    {
        return CallbackToFutureAdapter.GetFuture(new TileResolver(this))!;
    }

    protected override IListenableFuture OnTileResourcesRequest(RequestBuilders.ResourcesRequest requestParams)
    {
        return CallbackToFutureAdapter.GetFuture(new ResourceResolver())!;
    }

    private static LayoutElementBuilders.ILayoutElement BuildText(
        string text, float sizeSp, int argbColor)
    {
        return new LayoutElementBuilders.Text.Builder()
            .SetText(text)!
            .SetFontStyle(new LayoutElementBuilders.FontStyle.Builder()
                .SetSize(new DimensionBuilders.SpProp.Builder().SetValue(sizeSp)!.Build()!)!
                .SetColor(new ColorBuilders.ColorProp.Builder()
                    .SetArgb(argbColor)!.Build()!)!
                .Build()!)!
            .Build()!;
    }

    private class TileResolver : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        private readonly HrTileService _svc;
        public TileResolver(HrTileService svc) => _svc = svc;

        public Java.Lang.Object? AttachCompleter(CallbackToFutureAdapter.Completer completer)
        {
            try
            {
                var sp = _svc.GetSharedPreferences(HrpPrefs.PREFS_NAME, FileCreationMode.Private)!;
                int hr = sp.GetInt("tile_hr", 0);
                float kcal = sp.GetFloat("tile_kcal", 0f);
                int zone = sp.GetInt("tile_zone", 0);
                long timestamp = sp.GetLong("tile_timestamp", 0);

                long ageMs = Java.Lang.JavaSystem.CurrentTimeMillis() - timestamp;
                bool stale = timestamp == 0 || ageMs > 30_000;

                string hrText = stale ? "--" : hr.ToString();
                string kcalText = stale ? "-- kcal" : $"{kcal:F0} kcal";
                string zoneText = (zone >= 1 && zone <= 5 && !stale)
                    ? $"Z{zone} {HeartRateZone.Zones[zone - 1].Name}"
                    : "";

                // Build column content
                // HorizontalAlignment: 1=Start, 2=Center, 3=End
                var columnBuilder = new LayoutElementBuilders.Column.Builder()
                    .SetHorizontalAlignment(2)
                    .AddContent(BuildText(hrText, 48, unchecked((int)0xFFFFFFFF)))!
                    .AddContent(BuildText("bpm", 14, unchecked((int)0xFFAAAAAA)))!;

                if (!string.IsNullOrEmpty(zoneText))
                {
                    var (r, g, b) = HeartRateZone.GetZoneColor(zone);
                    int argb = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b));
                    columnBuilder.AddContent(BuildText(zoneText, 14, argb));
                }

                columnBuilder.AddContent(BuildText(kcalText, 12, unchecked((int)0xFFCCCCCC)));

                // Clickable to open app
                var launchAction = new ActionBuilders.LaunchAction.Builder()
                    .SetAndroidActivity(new ActionBuilders.AndroidActivity.Builder()
                        .SetPackageName(_svc.PackageName!)!
                        .SetClassName($"{_svc.PackageName}.MainActivity")!
                        .Build()!)!
                    .Build()!;

                var clickable = new ModifiersBuilders.Clickable.Builder()
                    .SetId("open_app")!
                    .SetOnClick(launchAction)!
                    .Build()!;

                var root = new LayoutElementBuilders.Box.Builder()
                    .SetModifiers(new ModifiersBuilders.Modifiers.Builder()
                        .SetClickable(clickable)!
                        .SetBackground(new ModifiersBuilders.Background.Builder()
                            .SetColor(new ColorBuilders.ColorProp.Builder()
                                .SetArgb(unchecked((int)0xFF000000))!.Build()!)!
                            .Build()!)!
                        .Build()!)!
                    .AddContent(columnBuilder.Build()!)!
                    .Build()!;

                var layout = new LayoutElementBuilders.Layout.Builder()
                    .SetRoot(root)!
                    .Build()!;

                var entry = new TimelineBuilders.TimelineEntry.Builder()
                    .SetLayout(layout)!
                    .Build()!;

                var timeline = new TimelineBuilders.Timeline.Builder()
                    .AddTimelineEntry(entry)!
                    .Build()!;

                var tile = new TileBuilders.Tile.Builder()
                    .SetResourcesVersion(RESOURCES_VERSION)!
                    .SetTimeline(timeline)!
                    .SetFreshnessIntervalMillis(5000)!
                    .Build()!;

                completer.Set(tile);
            }
            catch (SysException ex)
            {
                SysDebug.WriteLine($"{TAG}: TileResolver error: {ex}");
                var fallback = new TileBuilders.Tile.Builder()
                    .SetResourcesVersion(RESOURCES_VERSION)!
                    .Build()!;
                completer.Set(fallback);
            }

            return null;
        }
    }

    private class ResourceResolver : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        public Java.Lang.Object? AttachCompleter(CallbackToFutureAdapter.Completer completer)
        {
            var resources = new ResourceBuilders.Resources.Builder()
                .SetVersion(RESOURCES_VERSION)!
                .Build()!;
            completer.Set(resources);
            return null;
        }
    }
}
#pragma warning restore CS0618
