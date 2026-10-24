using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class MonitorService : IMonitorService
{
    private readonly IGoveeService _govee;
    private readonly IMerossService _meross;
    private readonly IPreferencesService _preferences;
    private readonly IKeepAliveService _keepAlive;
    private readonly TimeProvider _time;

    /// <summary>Margen por defecto sobre el umbral a partir del cual se apaga.</summary>
    public const double DefaultHysteresisMargin = 0.5;
    /// <summary>Minutos sin lecturas válidas tras los que se apaga el enchufe (0 = nunca).</summary>
    public const int DefaultFailsafeMinutes = 30;

    private CancellationTokenSource? _cts;
    // Estado real del enchufe tal como lo conocemos: null = desconocido (al arrancar
    // sin poder consultarlo, o tras una orden que el enchufe no confirmó). Mientras
    // sea desconocido se envía la orden aunque parezca innecesaria.
    private bool? _plugOn;
    private int _consecutiveErrors = 0;
    // A partir de cuántos errores seguidos se avisa. No se detiene: con la app cerrada
    // nadie podría volver a arrancarla. Si los errores duran, actúa el apagado de seguridad.
    private const int ErrorsBeforeWarning = 5;
    // Última lectura válida con el sensor online: lo único que permite controlar.
    private DateTimeOffset _lastValidReading;
    // Apagado de seguridad activo: sin lecturas válidas durante demasiado tiempo.
    private bool _failsafeActive;
    // 1 mientras StartAsync está en curso: la UI y el servicio de Android pueden
    // pedir el arranque a la vez, y sin esto habría dos bucles.
    private int _starting;

    public bool IsRunning { get; private set; }
    public bool? PlugState => _plugOn;
    public SensorReading? LastReading { get; private set; }

    public event Action<SensorReading>? OnReadingUpdated;
    public event Action<bool>? OnPlugStateChanged;
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnError;

    public MonitorService(IGoveeService govee, IMerossService meross, IPreferencesService preferences,
        IKeepAliveService? keepAlive = null, TimeProvider? time = null)
    {
        _govee = govee;
        _meross = meross;
        _preferences = preferences;
        _keepAlive = keepAlive ?? new NoOpKeepAliveService();
        _time = time ?? TimeProvider.System;

        // Redirigir logs internos de Meross a la UI
        _meross.OnLog += msg => OnLogMessage?.Invoke($"[Meross] {msg}");
    }

    public async Task StartAsync()
    {
        if (IsRunning || Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
        {
            OnLogMessage?.Invoke("⚠️ Monitorización ya está en ejecución.");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine("[MonitorService] 🟢 StartAsync() llamado");
            OnLogMessage?.Invoke("🔌 Iniciando monitorización...");

            // Se recuerda que el usuario quiere monitorizar: si Android cierra la app o
            // se reinicia el móvil, el servicio en segundo plano la vuelve a arrancar.
            _preferences.Set(SettingsKeys.MonitoringEnabled, 1);
            try
            {
                _keepAlive.Start();
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"⚠️ No se pudo activar la monitorización en segundo plano: {ex.Message}");
            }

            // Intentamos conectar Meross — si falla continuamos solo con temperatura
            _plugOn = null;
            try
            {
                OnLogMessage?.Invoke("Conectando con Meross...");
                await _meross.InitializeAsync();
                OnLogMessage?.Invoke("✅ Meross conectado.");
                _plugOn = await ReadPlugStateAsync();
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"⚠️ Meross no disponible: {ex.Message}");
                OnLogMessage?.Invoke("   Continuando solo con monitorización de temperatura.");
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            _consecutiveErrors = 0;
            // El plazo del apagado de seguridad cuenta desde el arranque: si nunca llega
            // una lectura (p. ej. sin red tras reiniciar), también se apaga.
            _lastValidReading = _time.GetUtcNow();
            _failsafeActive = false;

            System.Diagnostics.Debug.WriteLine("[MonitorService] ▶️ RunLoopAsync iniciado");
            _ = RunLoopAsync(_cts.Token);
        }
        finally
        {
            Interlocked.Exchange(ref _starting, 0);
        }
    }

    public void Stop()
    {
        // Lo pide el usuario: no se debe restaurar al reabrir la app ni al reiniciar.
        _preferences.Set(SettingsKeys.MonitoringEnabled, 0);
        _keepAlive.Stop();

        if (!IsRunning)
        {
            System.Diagnostics.Debug.WriteLine("[MonitorService] ⚠️ Stop() llamado pero ya estaba detenido");
            return;
        }

        System.Diagnostics.Debug.WriteLine("[MonitorService] ⏹️ Stop() llamado - canceling token");
        _cts?.Cancel();
        IsRunning = false;
        OnLogMessage?.Invoke("⏹️ Monitorización detenida.");
    }

    /// <summary>
    /// Permite encender o apagar el enchufe manualmente desde la UI,
    /// independientemente del umbral de temperatura.
    /// </summary>
    public async Task SetPlugManuallyAsync(bool turnOn)
    {
        var confirmed = await _meross.SetPlugAsync(turnOn);
        // Sincronizamos el estado interno para que el bucle no lo revierta
        _plugOn = confirmed ? turnOn : null;
        if (!confirmed)
            OnLogMessage?.Invoke("⚠️ El enchufe no confirmó la orden manual; puede no haber cambiado.");
        OnPlugStateChanged?.Invoke(turnOn);
    }

    private async Task<bool?> ReadPlugStateAsync()
    {
        try
        {
            var state = await _meross.GetPlugStateAsync();
            OnLogMessage?.Invoke(state == null
                ? "⚠️ No se pudo leer el estado del enchufe; se enviará la orden cuando haga falta."
                : $"🔌 Estado actual del enchufe: {(state.Value ? "ENCENDIDO" : "APAGADO")}");
            if (state != null)
                OnPlugStateChanged?.Invoke(state.Value);
            return state;
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"⚠️ No se pudo leer el estado del enchufe: {ex.Message}");
            return null;
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        System.Diagnostics.Debug.WriteLine("[MonitorService] 📍 RunLoopAsync iniciado");

        var intervalSecs = _preferences.Get(SettingsKeys.Interval, 60);
        var deviceId = _preferences.Get(SettingsKeys.GoveeDeviceId, "");
        var model = _preferences.Get(SettingsKeys.GoveeModel, "");

        // Buscar el sensor, reintentando si falla la red (p. ej. justo tras reiniciar
        // el móvil). Sólo se abandona si Govee responde que no hay ningún sensor.
        while (string.IsNullOrWhiteSpace(deviceId) && !token.IsCancellationRequested)
        {
            OnLogMessage?.Invoke("Buscando sensor Govee...");
            try
            {
                var devices = await _govee.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    StopAfterFatalError(token, "No se encontró ningún sensor Govee. Ve a Ajustes y detecta los dispositivos.");
                    System.Diagnostics.Debug.WriteLine("[MonitorService] ❌ Ningún sensor Govee encontrado");
                    return;
                }
                deviceId = devices[0].Device;
                model = devices[0].Model;
                _preferences.Set(SettingsKeys.GoveeDeviceId, deviceId);
                _preferences.Set(SettingsKeys.GoveeModel, model);
                OnLogMessage?.Invoke($"✅ Sensor detectado: {devices[0].DeviceName} ({model})");
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"❌ Error buscando sensor Govee: {ex.Message}. Reintento en {intervalSecs}s.");
                System.Diagnostics.Debug.WriteLine($"[MonitorService] ❌ Error detectando sensor: {ex.Message}");
                await CheckFailsafeAsync();
                if (!await DelayAsync(intervalSecs, token))
                    break;
            }
        }

        if (!token.IsCancellationRequested)
        {
            OnLogMessage?.Invoke($"✅ Usando sensor: {deviceId} ({model})");
            OnLogMessage?.Invoke("🔄 Iniciando lecturas...");
            System.Diagnostics.Debug.WriteLine("[MonitorService] ✅ Sensor configurado, entrando en bucle principal");
            _consecutiveErrors = 0;
        }

        // Primera lectura inmediata, después una cada intervalo
        while (!token.IsCancellationRequested)
        {
            try
            {
                await LeerYControlarAsync(deviceId, model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonitorService] ⚠️ Error en LeerYControlarAsync: {ex.Message}");
            }

            await CheckFailsafeAsync();

            if (!await DelayAsync(intervalSecs, token))
                break;
        }

        MarkStoppedIfCurrent(token);
        System.Diagnostics.Debug.WriteLine("[MonitorService] 🏁 RunLoopAsync finalizado");
    }

    /// <summary>Espera el intervalo; false si se canceló la monitorización.</summary>
    private static async Task<bool> DelayAsync(int seconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            return true;
        }
        catch (TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[MonitorService] 🛑 RunLoopAsync cancelado");
            return false;
        }
    }

    /// <summary>
    /// Marca el servicio como detenido sólo si <paramref name="token"/> pertenece a la
    /// ejecución actual: un bucle antiguo que termina tarde no debe dar por parado
    /// uno nuevo que ya arrancó, o Stop() dejaría de poder cancelarlo.
    /// </summary>
    private void MarkStoppedIfCurrent(CancellationToken token)
    {
        if (_cts != null && _cts.Token == token)
            IsRunning = false;
    }

    /// <summary>Error de configuración que reintentar no arregla: se para del todo.</summary>
    private void StopAfterFatalError(CancellationToken token, string message)
    {
        if (_cts != null && _cts.Token == token)
        {
            _cts.Cancel();
            IsRunning = false;
            _preferences.Set(SettingsKeys.MonitoringEnabled, 0);
            _keepAlive.Stop();
        }
        OnError?.Invoke(message);
    }

    /// <summary>
    /// Apagado de seguridad: sin lecturas válidas durante el plazo configurado, el
    /// enchufe se apaga en vez de quedarse encendido a ciegas. Se reintenta en cada
    /// intervalo hasta que el enchufe lo confirme; con la primera lectura válida se
    /// vuelve al control normal (ver LeerYControlarAsync).
    /// </summary>
    private async Task CheckFailsafeAsync()
    {
        var minutes = _preferences.Get(SettingsKeys.FailsafeMinutes, DefaultFailsafeMinutes);
        if (minutes <= 0)
            return;

        var elapsed = _time.GetUtcNow() - _lastValidReading;
        if (elapsed < TimeSpan.FromMinutes(minutes))
            return;

        if (!_failsafeActive)
        {
            _failsafeActive = true;
            OnLogMessage?.Invoke($"🛑 Apagado de seguridad: {elapsed.TotalMinutes:F0} min sin lecturas válidas del sensor. Se apaga el enchufe hasta que vuelvan.");
        }

        if (_plugOn != false)
            await ExecutePlugChangeAsync(false);
    }

    private async Task LeerYControlarAsync(string deviceId, string model)
    {
        try
        {
            var reading = await _govee.GetDeviceStateAsync(deviceId, model);
            var threshold = _preferences.Get(SettingsKeys.Threshold, 18.0);
            var margin = Math.Max(0, _preferences.Get(SettingsKeys.HysteresisMargin, DefaultHysteresisMargin));

            // Reset contador de errores en lectura exitosa
            _consecutiveErrors = 0;

            // 1. FILTRO DE LECTURAS BASURA
            // Si el sensor devuelve exactamente 0, valores absurdos o no es válido
            if (reading == null || reading.Temperature < -40 || reading.Temperature > 80 || (reading.Temperature == 0 && reading.Humidity == 0))
            {
                OnLogMessage?.Invoke("⚠️ Lectura ignorada: El sensor devolvió datos sospechosos o nulos.");
                return;
            }

            LastReading = reading;
            OnReadingUpdated?.Invoke(reading);

            if (!reading.Online)
            {
                OnLogMessage?.Invoke("⚠️ Sensor offline.");
                return;
            }

            _lastValidReading = _time.GetUtcNow();
            if (_failsafeActive)
            {
                _failsafeActive = false;
                OnLogMessage?.Invoke("✅ Lecturas recuperadas: se retoma el control por temperatura.");
            }

            OnLogMessage?.Invoke($"📊 Temp: {reading.Temperature:F1}°C  Hum: {reading.Humidity:F1}%  Enciende < {threshold:F1}°C · Apaga ≥ {threshold + margin:F1}°C");

            // 2. LÓGICA CON HISTÉRESIS
            // Entre umbral y umbral + margen no se toca el enchufe: evita el
            // "encendido/apagado" constante cuando la temperatura ronda el umbral.
            // La comparación es con != y no con !: si el estado es desconocido (null)
            // la orden se envía igualmente.

            // ENCENDER: Si la temperatura baja del umbral (ej. < 18.0)
            if (reading.Temperature < threshold && _plugOn != true)
            {
                OnLogMessage?.Invoke($"⚠️ Temp baja ({reading.Temperature:F1} < {threshold:F1}) → Encendiendo...");
                await ExecutePlugChangeAsync(true);
            }
            // APAGAR: Solo si alcanza el umbral + margen (ej. ≥ 19.0)
            else if (reading.Temperature >= threshold + margin && _plugOn != false)
            {
                OnLogMessage?.Invoke($"✅ Temp recuperada ({reading.Temperature:F1} ≥ {threshold + margin:F1}) → Apagando...");
                await ExecutePlugChangeAsync(false);
            }
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            OnLogMessage?.Invoke($"❌ Error leyendo sensor (intento fallido #{_consecutiveErrors}): {ex.Message}");

            if (_consecutiveErrors == ErrorsBeforeWarning)
                OnLogMessage?.Invoke($"⚠️ {ErrorsBeforeWarning} errores seguidos. La monitorización sigue reintentando en cada intervalo.");
        }
    }

    // Método auxiliar para evitar repetir código de error
    private async Task ExecutePlugChangeAsync(bool turnOn)
    {
        try
        {
            var confirmed = await _meross.SetPlugAsync(turnOn);
            OnPlugStateChanged?.Invoke(turnOn);
            if (confirmed)
            {
                _plugOn = turnOn;
            }
            else
            {
                // Sin confirmación no sabemos si cambió: se reenvía en la próxima lectura
                _plugOn = null;
                OnLogMessage?.Invoke("⚠️ El enchufe no confirmó la orden; se reenviará en la próxima lectura.");
            }
        }
        catch (Exception ex)
        {
            _plugOn = null;
            OnLogMessage?.Invoke($"❌ Error controlando enchufe: {ex.Message}");
        }
    }
}
