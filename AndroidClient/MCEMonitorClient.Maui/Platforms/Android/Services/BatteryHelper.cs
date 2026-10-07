using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Net;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    public static class BatteryHelper
    {
        public static void RequestIgnoreBatteryOptimizations(Context context)
        {
            try
            {
                var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch
            {
                try
                {
                    var fallback = new Intent(Settings.ActionIgnoreBatteryOptimizationSettings);
                    fallback.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(fallback);
                }
                catch { }
            }
        }

        public static void OpenStartupManager(Context context)
        {
            try
            {
                var intent = new Intent();
                intent.SetComponent(new ComponentName(
                    "com.huawei.systemmanager",
                    "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity"));
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch
            {
                try
                {
                    var intent2 = new Intent();
                    intent2.SetComponent(new ComponentName(
                        "com.huawei.systemmanager",
                        "com.huawei.systemmanager.appcontrol.activity.StartupAppControlActivity"));
                    intent2.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent2);
                }
                catch
                {
                    try
                    {
                        var appIntent = new Intent(Settings.ActionApplicationDetailsSettings);
                        appIntent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
                        appIntent.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(appIntent);
                    }
                    catch { }
                }
            }
        }

        public static void OpenNotificationSettings(Context context)
        {
            try
            {
                var intent = new Intent(Settings.ActionAppNotificationSettings);
                intent.PutExtra(Settings.ExtraAppPackage, context.PackageName);
                if (context.ApplicationInfo != null)
                    intent.PutExtra("android.intent.extra.UID", context.ApplicationInfo.Uid);
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch
            {
                try
                {
                    var appIntent = new Intent(Settings.ActionApplicationDetailsSettings);
                    appIntent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
                    appIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(appIntent);
                }
                catch { }
            }
        }
        
        public static bool IsIgnoringBatteryOptimizations(Context context)
        {
            try
            {
                var pm = context.GetSystemService(Context.PowerService) as PowerManager;
                return pm?.IsIgnoringBatteryOptimizations(context.PackageName) ?? false;
            }
            catch
            {
                return false;
            }
        }        
    }
}