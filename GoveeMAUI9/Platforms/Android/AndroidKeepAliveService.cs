using Android.Content;
using Android.OS;
using GoveeMAUI.Models;

namespace GoveeMAUI;

/// <summary>
/// Arranca y para <see cref="MonitorForegroundService"/>, y pide la exclusión de la
/// optimización de batería.
/// </summary>
public class AndroidKeepAliveService : IKeepAliveService
{
    public void Start()
    {
        // Ya en marcha (p. ej. es el propio servicio quien restaura la monitorización):
        // no se vuelve a pedir, Android 12+ no deja arrancarlo desde segundo plano.
        if (MonitorForegroundService.IsRunning)
            return;

        StartMonitorService(Android.App.Application.Context);
    }

    public void Stop()
    {
        var context = Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(MonitorForegroundService)));
    }

    public void RequestBackgroundExemption()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            return;

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var powerManager = (PowerManager?)context.GetSystemService(Context.PowerService);
        if (powerManager == null || powerManager.IsIgnoringBatteryOptimizations(context.PackageName))
            return;

        // Abre el diálogo del sistema "¿Permitir que la app se ejecute siempre en
        // segundo plano?". Si el usuario lo rechaza, se vuelve a pedir la próxima vez
        // que inicie la monitorización.
        var intent = new Intent(
            Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
            Android.Net.Uri.Parse("package:" + context.PackageName));
        if (context is not Android.App.Activity)
            intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    internal static void StartMonitorService(Context context)
    {
        var intent = new Intent(context, typeof(MonitorForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }
}
