using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using GoveeMAUI.Models;

namespace GoveeMAUI;

/// <summary>
/// Servicio en primer plano que mantiene la monitorización con la app minimizada,
/// cerrada desde recientes o con la pantalla apagada (notificación persistente +
/// wake lock parcial para que la CPU no duerma).
///
/// El bucle de control es MonitorService, que vive en el mismo proceso. Si Android
/// mata el proceso, START_STICKY hace que el sistema recree el servicio, y éste
/// vuelve a arrancar la monitorización si el usuario la había dejado activada. Lo
/// mismo hace BootReceiver al reiniciar el móvil.
///
/// specialUse y no dataSync: en Android 15 dataSync está limitado a 6 h al día y
/// no puede arrancarse desde BOOT_COMPLETED.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class MonitorForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "monitor";

    private PowerManager.WakeLock? _wakeLock;

    /// <summary>Si el servicio está en marcha en este proceso.</summary>
    public static bool IsRunning { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Android exige llamar a StartForeground enseguida tras StartForegroundService,
        // también cuando a continuación el servicio decide pararse.
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification());
        IsRunning = true;

        var services = IPlatformApplication.Current?.Services;
        var preferences = services?.GetService<IPreferencesService>();
        var monitor = services?.GetService<IMonitorService>();

        if (preferences == null || monitor == null
            || preferences.Get(SettingsKeys.MonitoringEnabled, 0) != 1)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (_wakeLock == null)
        {
            var powerManager = (PowerManager?)GetSystemService(PowerService);
            _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "GoveeMAUI:monitor");
            _wakeLock?.Acquire();
        }

        // Restaurar tras un cierre del proceso o un reinicio. Si la ha arrancado la
        // UI, ya está en marcha (o arrancando) y StartAsync no hace nada.
        if (!monitor.IsRunning)
            _ = monitor.StartAsync();

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        if (_wakeLock?.IsHeld == true)
            _wakeLock.Release();
        _wakeLock = null;
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(ChannelId, "Monitorización", NotificationImportance.Low)
        {
            Description = "Aviso mientras la app controla la temperatura"
        };
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var flags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
            : PendingIntentFlags.UpdateCurrent;
        var pendingIntent = PendingIntent.GetActivity(this, 0, launchIntent, flags);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Govee Temp Control")
            .SetContentText("Monitorizando la temperatura")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .SetContentIntent(pendingIntent)
            .Build()!;
    }
}
