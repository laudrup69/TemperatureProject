using Android.Content;
using GoveeMAUI.Models;

namespace GoveeMAUI;

/// <summary>
/// Arranca y para <see cref="MonitorForegroundService"/>. Idempotente: el reintento
/// automático vuelve a llamar a Start() con el servicio ya en marcha, a veces con la
/// app en segundo plano, donde Android 12+ no deja arrancar uno nuevo.
/// </summary>
public class AndroidKeepAliveService : IKeepAliveService
{
    private bool _running;

    public void Start()
    {
        if (_running)
            return;

        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(MonitorForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
        _running = true;
    }

    public void Stop()
    {
        if (!_running)
            return;

        var context = Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(MonitorForegroundService)));
        _running = false;
    }
}
