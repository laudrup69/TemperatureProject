using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class MonitorService : IMonitorService
{
    private readonly IGoveeService _govee;
    private readonly IMerossService _meross;
    private readonly IPreferencesService _preferences;

    /// <summary>Margen por defecto sobre el umbral a partir del cual se apaga.</summary>
    public const double DefaultHysteresisMargin = 1.0;

    private CancellationTokenSource? _cts;
    // Estado real del enchufe tal como lo conocemos: null = desconocido (al arrancar
    // sin poder consultarlo, o tras una orden que el enchufe no confirmó). Mientras
    // sea desconocido se envía la orden aunque parezca innecesaria.
    private bool? _plugOn;
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
        if (IsRunning) 
        {
            OnLogMessage?.Invoke("⚠️ Monitorización ya está en ejecución.");
            return;
        }

        System.Diagnostics.Debug.WriteLine("[MonitorService] 🟢 StartAsync() llamado");
        OnLogMessage?.Invoke("🔌 Iniciando monitorización...");

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

        System.Diagnostics.Debug.WriteLine("[MonitorService] ▶️ RunLoopAsync iniciado");
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
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
                ? "⚠️ No se pudo leer el estado del enchufe; se enviará la orden en la primera lectura."
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
                    MarkStoppedIfCurrent(token);
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
                OnError?.Invoke($"Error buscando sensor Govee: {ex.Message}");
                MarkStoppedIfCurrent(token);
                System.Diagnostics.Debug.WriteLine($"[MonitorService] ❌ Error detectando sensor: {ex.Message}");
                return;
            }
        }
        else
        {
            OnLogMessage?.Invoke($"✅ Usando sensor configurado: {deviceId} ({model})");
        }

        OnLogMessage?.Invoke("🔄 Iniciando lecturas...");
        System.Diagnostics.Debug.WriteLine("[MonitorService] ✅ Sensor configurado, entrando en bucle principal");
        _consecutiveErrors = 0;

        var intervalSecs = _preferences.Get(SettingsKeys.Interval, 60);

        // Primera lectura inmediata sin esperar el intervalo
        try
        {
            await LeerYControlarAsync(deviceId, model, token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MonitorService] ⚠️ Error en primera lectura: {ex.Message}");
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSecs), token);
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[MonitorService] 🛑 RunLoopAsync cancelado (pausa de app)");
                break;
            }

            if (!token.IsCancellationRequested)
            {
                try
                {
                    await LeerYControlarAsync(deviceId, model, token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MonitorService] ⚠️ Error en LeerYControlarAsync: {ex.Message}");
                }
            }
        }

        MarkStoppedIfCurrent(token);
        System.Diagnostics.Debug.WriteLine("[MonitorService] 🏁 RunLoopAsync finalizado");
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

    private async Task LeerYControlarAsync(string deviceId, string model, CancellationToken token)
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

            OnReadingUpdated?.Invoke(reading);

            if (!reading.Online)
            {
                OnLogMessage?.Invoke("⚠️ Sensor offline.");
                return;
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
            
            if (_consecutiveErrors >= MaxConsecutiveErrors)
            {
                // Cancelar de verdad el bucle: si sólo se marca como parado, sigue
                // corriendo y el reintento automático arranca otro en paralelo.
                if (_cts != null && _cts.Token == token)
                {
                    _cts.Cancel();
                    IsRunning = false;
                }
                OnError?.Invoke($"Demasiados errores consecutivos ({_consecutiveErrors}). Deteniendo monitorización.");
            }
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