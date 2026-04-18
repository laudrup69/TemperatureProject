using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MonitorService _monitor;

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
            IsRunning = false;
            StatusText = "Detenido";
            StatusColor = Colors.Gray;
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
        });
    }

    // Este método se ejecutará automáticamente CADA VEZ que PlugOn cambie de valor, 
    // ya sea por el botón manual o por el monitor de Govee.
    partial void OnPlugOnChanged(bool value)
    {
        ManualPlugButtonText = value ? "⭕ Apagar enchufe manualmente" : "🔌 Encender enchufe manualmente";
        ManualPlugButtonColor = value ? "#F44336" : "#5A4FD6";
    }
}