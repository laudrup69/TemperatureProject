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
        if (_monitor.IsRunning)
        {
            _monitor.Stop();
            _wasRunningWhenError = false;
            _autoRetryTokenSource?.Cancel();
            IsRunning = false;
            StatusText = "Detenido";
            StatusColor = Colors.Gray;
            AddLog("⏹️ Monitorización detenida por el usuario.");
        }
        else
        {
            try
            {
                var apiKey = Preferences.Get(SettingsKeys.GoveeApiKey, "");
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    AddLog("❌ Falta la API Key de Govee. Ve a ⚙️ Ajustes primero.");
                    return;
                }

                Preferences.Set(SettingsKeys.Threshold, Threshold);

                StatusText = "Conectando...";
                StatusColor = Color.FromArgb("#F7C26A");
                IsRunning = true;
                _wasRunningWhenError = false;
                _autoRetryCount = 0;
                _autoRetryTokenSource?.Cancel(); // Cancelar reintentos automáticos pendientes

                await _monitor.StartAsync();

                StatusText = "Monitorizando";
                StatusColor = Color.FromArgb("#4CAF50");
            }
            catch (Exception ex)
            {
                AddLog($"❌ {ex.Message}");
                IsRunning = false;
                StatusText = "Error — revisa los Ajustes";
                StatusColor = Colors.Red;
            }
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
            var newState = !PlugOn;
            AddLog($"👆 Control manual → {(newState ? "ENCENDER" : "APAGAR")} enchufe...");
            await _monitor.SetPlugManuallyAsync(newState);
            PlugOn = newState;
            //ManualPlugButtonText = PlugOn ? "⭕ Apagar enchufe manualmente" : "🔌 Encender enchufe manualmente";
            //ManualPlugButtonColor = PlugOn ? "#F44336" : "#5A4FD6";
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