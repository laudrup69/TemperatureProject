using Android.App;
using Android.Content;
using GoveeMAUI.Models;

namespace GoveeMAUI;

/// <summary>
/// Vuelve a arrancar la monitorización al encender el móvil o al instalar una versión
/// nueva de la app, si el usuario la había dejado activada. Son dos de los pocos casos
/// en que Android permite arrancar un servicio en primer plano sin la app abierta.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null)
            return;

        var preferences = IPlatformApplication.Current?.Services.GetService<IPreferencesService>();
        if (preferences?.Get(SettingsKeys.MonitoringEnabled, 0) != 1)
            return;

        AndroidKeepAliveService.StartMonitorService(context);
    }
}
