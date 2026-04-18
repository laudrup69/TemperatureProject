using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly GoveeService _govee;

    [ObservableProperty] private string _goveeApiKey     = "";
    [ObservableProperty] private string _goveeDeviceId   = "";
    [ObservableProperty] private string _goveeModel      = "";
    [ObservableProperty] private string _merossEmail     = "";
    [ObservableProperty] private string _merossPassword  = "";
    [ObservableProperty] private string _merossDevice    = "";
    [ObservableProperty] private double _threshold       = 18.0;
    [ObservableProperty] private int    _intervalSeconds = 60;
    [ObservableProperty] private string _statusMessage   = "";
    [ObservableProperty] private string _deviceList      = "";

    public SettingsViewModel(GoveeService govee)
    {
        _govee = govee;
        LoadSettings();
    }

    private void LoadSettings()
    {
        GoveeApiKey     = Preferences.Get(SettingsKeys.GoveeApiKey,    "");
        GoveeDeviceId   = Preferences.Get(SettingsKeys.GoveeDeviceId,  "");
        GoveeModel      = Preferences.Get(SettingsKeys.GoveeModel,     "");
        MerossEmail     = Preferences.Get(SettingsKeys.MerossEmail,    "");
        MerossPassword  = Preferences.Get(SettingsKeys.MerossPassword, "");
        MerossDevice    = Preferences.Get(SettingsKeys.MerossDevice,   "");
        Threshold       = Preferences.Get(SettingsKeys.Threshold,      18.0);
        IntervalSeconds = Preferences.Get(SettingsKeys.Interval,       60);
    }

    [RelayCommand]
    private void Save()
    {
        Preferences.Set(SettingsKeys.GoveeApiKey,    GoveeApiKey);
        Preferences.Set(SettingsKeys.GoveeDeviceId,  GoveeDeviceId);
        Preferences.Set(SettingsKeys.GoveeModel,     GoveeModel);
        Preferences.Set(SettingsKeys.MerossEmail,    MerossEmail);
        Preferences.Set(SettingsKeys.MerossPassword, MerossPassword);
        Preferences.Set(SettingsKeys.MerossDevice,   MerossDevice);
        Preferences.Set(SettingsKeys.Threshold,      Threshold);
        Preferences.Set(SettingsKeys.Interval,       IntervalSeconds);
        StatusMessage = "Ajustes guardados correctamente.";
    }

    [RelayCommand]
    private async Task DetectGoveeDevicesAsync()
    {
        try
        {
            Preferences.Set(SettingsKeys.GoveeApiKey, GoveeApiKey);
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
