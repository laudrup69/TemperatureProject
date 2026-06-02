using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IGoveeService _govee;
    private readonly IPreferencesService _preferences;

    [ObservableProperty] private string _goveeApiKey     = "";
    [ObservableProperty] private string _goveeDeviceId   = "";
    [ObservableProperty] private string _goveeModel      = "";
    [ObservableProperty] private string _merossEmail     = "";
    [ObservableProperty] private string _merossPassword  = "";
    [ObservableProperty] private string _merossSecret    = "";
    [ObservableProperty] private string _merossDevice    = "";
    [ObservableProperty] private double _threshold       = 18.0;
    [ObservableProperty] private int    _intervalSeconds = 60;
    [ObservableProperty] private string _statusMessage   = "";
    [ObservableProperty] private string _deviceList      = "";

    public SettingsViewModel(IGoveeService govee, IPreferencesService preferences)
    {
        _govee = govee;
        _preferences = preferences;
        LoadSettings();
    }

    private void LoadSettings()
    {
        GoveeApiKey     = _preferences.Get(SettingsKeys.GoveeApiKey,    "");
        GoveeDeviceId   = _preferences.Get(SettingsKeys.GoveeDeviceId,  "");
        GoveeModel      = _preferences.Get(SettingsKeys.GoveeModel,     "");
        MerossEmail     = _preferences.Get(SettingsKeys.MerossEmail,    "");
        MerossPassword  = _preferences.Get(SettingsKeys.MerossPassword, "");
        MerossSecret    = _preferences.Get(SettingsKeys.MerossSecret,   "");
        MerossDevice    = _preferences.Get(SettingsKeys.MerossDevice,   "");
        Threshold       = _preferences.Get(SettingsKeys.Threshold,      18.0);
        IntervalSeconds = _preferences.Get(SettingsKeys.Interval,       60);
    }

    [RelayCommand]
    private void Save()
    {
        _preferences.Set(SettingsKeys.GoveeApiKey,    GoveeApiKey);
        _preferences.Set(SettingsKeys.GoveeDeviceId,  GoveeDeviceId);
        _preferences.Set(SettingsKeys.GoveeModel,     GoveeModel);
        _preferences.Set(SettingsKeys.MerossEmail,    MerossEmail);
        _preferences.Set(SettingsKeys.MerossSecret,   MerossSecret);
        _preferences.Set(SettingsKeys.MerossPassword, MerossPassword);
        _preferences.Set(SettingsKeys.MerossDevice,   MerossDevice);
        _preferences.Set(SettingsKeys.Threshold,      Threshold);
        _preferences.Set(SettingsKeys.Interval,       IntervalSeconds);
        StatusMessage = "Ajustes guardados correctamente.";
    }

    [RelayCommand]
    private async Task DetectGoveeDevicesAsync()
    {
        try
        {
            _preferences.Set(SettingsKeys.GoveeApiKey, GoveeApiKey);
            StatusMessage = "Buscando sensores Govee...";
            DeviceList    = "";

            var devices = await _govee.GetDevicesAsync();
            if (devices.Count == 0)
            {
                StatusMessage = "No se encontraron dispositivos. Comprueba la API Key.";
                return;
            }

            DeviceList    = string.Join("\n", devices.Select(d =>
                $"Nombre: {d.DeviceName}  |  ID: {d.Device}  |  Modelo: {d.Model}"));

            GoveeDeviceId = devices[0].Device;
            GoveeModel    = devices[0].Model;
            StatusMessage = $"Se encontraron {devices.Count} dispositivo(s). Seleccionado el primero automaticamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
