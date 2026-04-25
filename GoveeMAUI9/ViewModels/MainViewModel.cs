using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MonitorService _monitor;
    private CancellationTokenSource? _autoRetryTokenSource;
    private bool _wasRunningWhenError = false;
    private int _autoRetryCount = 0;
    private const int MaxAutoRetries = 3;
    private const int RetryDelaySeconds = 10;

    [ObservableProperty] private string _temperature = "--.-";
    [ObservableProperty] private string _humidity = "--.-";
    [ObservableProperty] private bool _plugOn = false;
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty] private string _statusText = "Detenido";
    [ObservableProperty] private Color _statusColor = Colors.Gray;
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private string _lastUpdate = "Sin lecturas aún";
    [ObservableProperty] private double _threshold;
    [ObservableProperty] private string _manualPlugButtonText = "🔌 Encender enchufe manualmente";
    [ObservableProperty] private string _manualPlugButtonColor = "#5A4FD6";
    [ObservableProperty] private OperationMode _currentMode = OperationMode.Manual;
    [ObservableProperty] private bool _isManualPlugButtonEnabled = true;

    public MainViewModel(MonitorService monitor)
    {
        _monitor = monitor;
        _threshold = Preferences.Get(SettingsKeys.Threshold, 18.0);

        _monitor.OnReadingUpdated += OnReading;
        _monitor.OnPlugStateChanged += OnPlugChanged;
        _monitor.OnLogMessage += AddLog;
        _monitor.OnError += OnErrorReceived;
    }

    [RelayCommand]
    private async Task ToggleMonitorAsync()
    {
        try
        {
            if (CurrentMode == OperationMode.Monitoring)
            {
                // TRANSICIÓN: Monitoring → Manual
                AddLog("🔄 Deteniendo monitorización...");
                _monitor.Stop();
                _wasRunningWhenError = false;
                _autoRetryTokenSource?.Cancel();
                IsRunning = false;
                CurrentMode = OperationMode.Manual;
                IsManualPlugButtonEnabled = true;
                
                // Apagar el enchufe al detener monitoreo
                try
                {
                    await _monitor.SetPlugManuallyAsync(false);
                    PlugOn = false;
                }
                catch (Exception ex)
                {
                    AddLog($"⚠️ No se pudo apagar el enchufe: {ex.Message}");
                }
                
                StatusText = "Modo manual";
                StatusColor = Colors.Gray;
                AddLog("✅ Monitorización detenida. Modo manual activo.");
            }
            else
            {
                // TRANSICIÓN: Manual → Monitoring
                AddLog("🔄 Iniciando monitorización...");
                
                var apiKey = Preferences.Get(SettingsKeys.GoveeApiKey, "");
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    AddLog("❌ Falta la API Key de Govee. Ve a ⚙️ Ajustes primero.");
                    return;
                }

                Preferences.Set(SettingsKeys.Threshold, Threshold);

                // Si el enchufe estaba encendido manualmente, apágalo antes de monitorear
                if (PlugOn)
                {
                    try
                    {
                        AddLog("⚠️ Apagando enchufe antes de activar monitorización...");
                        await _monitor.SetPlugManuallyAsync(false);
                        PlugOn = false;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"⚠️ No se pudo apagar el enchufe: {ex.Message}");
                    }
                }

                StatusText = "Conectando...";
                StatusColor = Color.FromArgb("#F7C26A");
                IsRunning = true;
                CurrentMode = OperationMode.Monitoring;
                IsManualPlugButtonEnabled = false;
                _wasRunningWhenError = false;
                _autoRetryCount = 0;
                _autoRetryTokenSource?.Cancel();

                await _monitor.StartAsync();

                StatusText = "Monitorizando";
                StatusColor = Color.FromArgb("#4CAF50");
                AddLog("✅ Monitorización iniciada. Controles manuales deshabilitados.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ {ex.Message}");
            IsRunning = false;
            CurrentMode = OperationMode.Manual;
            IsManualPlugButtonEnabled = true;
            StatusText = "Error — revisa los Ajustes";
            StatusColor = Colors.Red;
        }
    }

    [RelayCommand]
    private void SaveThreshold()
    {
        Preferences.Set(SettingsKeys.Threshold, Threshold);
        AddLog($"💾 Umbral guardado: {Threshold:F1}°C");
    }

    [RelayCommand]
    private async Task TogglePlugManuallyAsync()
    {
        try
        {
            // Si estamos monitoreando, detenemos la monitorización primero
            if (CurrentMode == OperationMode.Monitoring)
            {
                AddLog("⚠️ Deteniendo monitorización para cambiar a control manual...");
                _monitor.Stop();
                _wasRunningWhenError = false;
                _autoRetryTokenSource?.Cancel();
                IsRunning = false;
                CurrentMode = OperationMode.Manual;
                IsManualPlugButtonEnabled = true;
                StatusText = "Modo manual";
                StatusColor = Colors.Gray;
                AddLog("✅ Monitorización detenida. Control manual activado.");
            }

            // Ahora estamos en modo Manual, cambiar estado del enchufe
            var newState = !PlugOn;
            AddLog($"👆 Control manual → {(newState ? "ENCENDER" : "APAGAR")} enchufe...");
            await _monitor.SetPlugManuallyAsync(newState);
            PlugOn = newState;
            AddLog($"✅ Enchufe {(PlugOn ? "ENCENDIDO" : "APAGADO")} manualmente.");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error control manual: {ex.Message}");
        }
    }

    private void OnReading(SensorReading r)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Temperature = $"{r.Temperature:F1}";
            Humidity = $"{r.Humidity:F1}";
            LastUpdate = $"Última lectura: {DateTime.Now:HH:mm:ss}";
        });
    }

    private void OnPlugChanged(bool isOn)
        => MainThread.BeginInvokeOnMainThread(() => PlugOn = isOn);

    private void AddLog(string msg)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}]  {msg}";
            Log = Log.Length > 3000
                ? line + "\n" + Log[..2000]
                : line + "\n" + Log;
        });
    }

    private void OnErrorReceived(string err)
    {
        AddLog($"❌ {err}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusText = "Error";
            StatusColor = Colors.Red;
            IsRunning = false;
            CurrentMode = OperationMode.Manual;
            IsManualPlugButtonEnabled = true;
            _wasRunningWhenError = true;
            _autoRetryCount = 0;
            
            // Iniciar reintento automático
            StartAutoRetry();
        });
    }

    private void StartAutoRetry()
    {
        // Cancelar reintento anterior si existe
        _autoRetryTokenSource?.Cancel();
        _autoRetryTokenSource = new CancellationTokenSource();

        _ = AutoRetryLoopAsync(_autoRetryTokenSource.Token);
    }

    private async Task AutoRetryLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _autoRetryCount < MaxAutoRetries && _wasRunningWhenError)
        {
            _autoRetryCount++;
            AddLog($"⏳ Reintentando automáticamente en {RetryDelaySeconds}s... (intento {_autoRetryCount}/{MaxAutoRetries})");
            
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), token);
                
                if (token.IsCancellationRequested || !_wasRunningWhenError)
                    break;
                
                AddLog($"🔄 Reintentando monitorización (intento {_autoRetryCount}/{MaxAutoRetries})...");
                await ToggleMonitorAsync();
                
                // Si ToggleMonitorAsync inicia el monitor exitosamente, salir del loop
                if (_monitor.IsRunning)
                {
                    AddLog("✅ Monitorización reiniciada automáticamente.");
                    _wasRunningWhenError = false;
                    break;
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error en reintento automático: {ex.Message}");
            }
        }

        if (_autoRetryCount >= MaxAutoRetries && _wasRunningWhenError)
        {
            AddLog($"❌ Reintentos automáticos agotados ({MaxAutoRetries}). Requiere intervención manual.");
            _wasRunningWhenError = false;
        }
    }

    // Este método se ejecutará automáticamente CADA VEZ que PlugOn cambie de valor, 
    // ya sea por el botón manual o por el monitor de Govee.
    partial void OnPlugOnChanged(bool value)
    {
        ManualPlugButtonText = value ? "⭕ Apagar enchufe manualmente" : "🔌 Encender enchufe manualmente";
        ManualPlugButtonColor = value ? "#F44336" : "#5A4FD6";
    }
}