using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace GoveeMAUI;

/// <summary>
/// Servicio en primer plano que mantiene viva la app mientras se monitoriza.
/// No ejecuta lógica propia: el bucle de MonitorService corre en el mismo proceso,
/// y este servicio sólo evita que Android lo congele o lo cierre con la pantalla
/// apagada (notificación persistente + wake lock parcial para que la CPU no duerma).
/// specialUse y no dataSync: en Android 15 dataSync está limitado a 6 h al día.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class MonitorForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "monitor";

    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification());

        if (_wakeLock == null)
        {
            var powerManager = (PowerManager?)GetSystemService(PowerService);
            _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "GoveeMAUI:monitor");
            _wakeLock?.Acquire();
        }

        // Si Android mata el proceso, la monitorización (que vive en memoria) se pierde
        // con él: no tiene sentido que el sistema recree el servicio solo.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
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
