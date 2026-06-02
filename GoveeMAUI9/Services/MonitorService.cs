using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class MonitorService : IMonitorService
{
    private readonly IGoveeService _govee;
    private readonly IMerossService _meross;
    private readonly IPreferencesService _preferences;

    private CancellationTokenSource? _cts;
    private bool _plugOn = false;
    private int _consecutiveErrors = 0;
    private const int MaxConsecutiveErrors = 5;

    public bool IsRunning { get; private set; }

    public event Action<SensorReading>? OnReadingUpdated;
    public event Action<bool>? OnPlugStateChanged;
    public event Action<string>? OnLogMessage;
    public event Action<string>? OnError;

    public MonitorService(IGoveeService govee, IMerossService meross, IPreferencesService preferences)
    {
        _govee = govee;
        _meross = meross;
        _preferences = preferences;

        // Redirigir logs internos de Meross a la UI
        _meross.OnLog += msg => OnLogMessage?.Invoke($"[Meross] {msg}");
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        // Intentamos conectar Meross — si falla continuamos solo con temperatura
        try
        {
            OnLogMessage?.Invoke("Conectando con Meross...");
            await _meross.InitializeAsync();
            OnLogMessage?.Invoke("✅ Meross conectado.");
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"⚠️ Meross no disponible: {ex.Message}");
            OnLogMessage?.Invoke("   Continuando solo con monitorización de temperatura.");
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _consecutiveErrors = 0;

        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
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
        await _meross.SetPlugAsync(turnOn);
        // Sincronizamos el estado interno para que el bucle no lo revierta
        _plugOn = turnOn;
        OnPlugStateChanged?.Invoke(turnOn);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var deviceId = _preferences.Get(SettingsKeys.GoveeDeviceId, "");
        var model = _preferences.Get(SettingsKeys.GoveeModel, "");

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            OnLogMessage?.Invoke("Buscando sensor Govee...");
            try
            {
                var devices = await _govee.GetDevicesAsync();
                if (devices.Count == 0)
                {
                    OnError?.Invoke("No se encontró ningún sensor Govee. Ve a Ajustes y detecta los dispositivos.");
                    IsRunning = false;
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
                OnError?.Invoke($"Error buscando sensor Govee: {ex.Message}");
                IsRunning = false;
                // Aquí NO retornamos inmediatamente; MainViewModel puede reintentar automáticamente
                return;
            }
        }
        else
        {
            OnLogMessage?.Invoke($"✅ Usando sensor configurado: {deviceId} ({model})");
        }

        OnLogMessage?.Invoke("🔄 Iniciando lecturas...");
        _consecutiveErrors = 0;

        var intervalSecs = _preferences.Get(SettingsKeys.Interval, 60);

        // Primera lectura inmediata sin esperar el intervalo
        await LeerYControlarAsync(deviceId, model);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSecs), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (!token.IsCancellationRequested)
                await LeerYControlarAsync(deviceId, model);
        }

        IsRunning = false;
    }

    private async Task LeerYControlarAsync(string deviceId, string model)
    {
        try
        {
            var reading = await _govee.GetDeviceStateAsync(deviceId, model);
            var threshold = _preferences.Get(SettingsKeys.Threshold, 18.0);

            // Reset contador de errores en lectura exitosa
            _consecutiveErrors = 0;

            // 1. FILTRO DE LECTURAS BASURA
            // Si el sensor devuelve exactamente 0, valores absurdos o no es válido
            if (reading == null || reading.Temperature < -40 || reading.Temperature > 80 || (reading.Temperature == 0 && reading.Humidity == 0))
            {
                OnLogMessage?.Invoke("⚠️ Lectura ignorada: El sensor devolvió datos sospechosos o nulos.");
                return;
            }

            OnReadingUpdated?.Invoke(reading);

            if (!reading.Online)
            {
                OnLogMessage?.Invoke("⚠️ Sensor offline.");
                return;
            }

            OnLogMessage?.Invoke($"📊 Temp: {reading.Temperature:F1}°C  Hum: {reading.Humidity:F1}%  Umbral: {threshold}°C");

            // 2. LÓGICA CON HISTÉRESIS (MARGEN DE 1 GRADO)
            // Definimos un margen para evitar el "encendido/apagado" constante
            double margin = 1.0;

            // ENCENDER: Si la temperatura baja del umbral (ej. < 18.0)
            if (reading.Temperature < threshold && !_plugOn)
            {
                OnLogMessage?.Invoke($"⚠️ Temp baja ({reading.Temperature:F1} < {threshold}) → Encendiendo...");
                await ExecutePlugChangeAsync(true);
            }
            // APAGAR: Solo si supera el umbral + margen (ej. > 19.0)
            else if (reading.Temperature >= (threshold + margin) && _plugOn)
            {
                OnLogMessage?.Invoke($"✅ Temp recuperada ({reading.Temperature:F1} >= {threshold + margin}) → Apagando...");
                await ExecutePlugChangeAsync(false);
            }
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            OnLogMessage?.Invoke($"❌ Error leyendo sensor (intento fallido #{_consecutiveErrors}): {ex.Message}");
            
            if (_consecutiveErrors >= MaxConsecutiveErrors)
            {
                OnError?.Invoke($"Demasiados errores consecutivos ({_consecutiveErrors}). Deteniendo monitorización.");
                IsRunning = false;
            }
        }
    }

    // Método auxiliar para evitar repetir código de error
    private async Task ExecutePlugChangeAsync(bool turnOn)
    {
        try
        {
            await _meross.SetPlugAsync(turnOn);
            _plugOn = turnOn;
            OnPlugStateChanged?.Invoke(turnOn);
        }
        catch (Exception ex)
        {
            OnLogMessage?.Invoke($"❌ Error controlando enchufe: {ex.Message}");
        }
    }
}