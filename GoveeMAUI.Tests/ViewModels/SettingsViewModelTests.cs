using GoveeMAUI.Models;
using GoveeMAUI.Services;
using GoveeMAUI.ViewModels;

namespace GoveeMAUI.Tests.ViewModels;

[Collection("MAUI Tests Collection")]
public class SettingsViewModelTests
{    static SettingsViewModelTests() => TestMauiInitializer.Initialize();
    private readonly Mock<IGoveeService> _goveeMock;
    private readonly TestPreferencesService _preferences;
    private readonly SettingsViewModel _sut;

    public SettingsViewModelTests()
    {
        _goveeMock = new Mock<IGoveeService>();
        _preferences = new TestPreferencesService();
        _sut = new SettingsViewModel(_goveeMock.Object, _preferences);
    }

    [Fact]
    public void Constructor_ShouldLoadSettingsFromPreferences()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");
        _preferences.Set(SettingsKeys.Threshold, 20.5);
        _preferences.Set(SettingsKeys.Interval, 45);

        // Act
        var vm = new SettingsViewModel(_goveeMock.Object, _preferences);

        // Assert
        vm.GoveeApiKey.Should().Be("test-api-key");
        vm.Threshold.Should().Be(20.5);
        vm.IntervalSeconds.Should().Be(45);
    }

    [Fact]
    public void Constructor_ShouldUseDefaultValuesIfNotSet()
    {
        // Arrange - Clear all preferences
        _preferences.Remove(SettingsKeys.GoveeApiKey);
        _preferences.Remove(SettingsKeys.Threshold);
        _preferences.Remove(SettingsKeys.Interval);

        // Act
        var vm = new SettingsViewModel(_goveeMock.Object, _preferences);

        // Assert
        vm.GoveeApiKey.Should().BeEmpty();
        vm.Threshold.Should().Be(18.0); // Default
        vm.IntervalSeconds.Should().Be(60); // Default
    }

    [Fact]
    public void SaveCommand_ShouldPersistAllSettings()
    {
        // Arrange
        _sut.GoveeApiKey = "new-api-key";
        _sut.MerossEmail = "test@example.com";
        _sut.Threshold = 22.5;
        _sut.IntervalSeconds = 30;

        // Act
        _sut.SaveCommand.Execute(null);

        // Assert
        _preferences.Get(SettingsKeys.GoveeApiKey, "").Should().Be("new-api-key");
        _preferences.Get(SettingsKeys.MerossEmail, "").Should().Be("test@example.com");
        _preferences.Get(SettingsKeys.Threshold, 0.0).Should().Be(22.5);
        _preferences.Get(SettingsKeys.Interval, 0).Should().Be(30);
    }

    [Fact]
    public void SaveCommand_ShouldSetStatusMessage()
    {
        // Act
        _sut.SaveCommand.Execute(null);

        // Assert
        _sut.StatusMessage.Should().Be("Ajustes guardados correctamente.");
    }

    [Fact]
    public async Task DetectGoveeDevicesAsync_WithValidApiKey_ShouldListDevices()
    {
        // Arrange
        _sut.GoveeApiKey = "valid-api-key";

        var devices = new List<GoveeDevice>
        {
            new GoveeDevice { Device = "dev1", Model = "H5184", DeviceName = "Living Room" },
            new GoveeDevice { Device = "dev2", Model = "H5109", DeviceName = "Bedroom" }
        };

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(devices);

        // Act
        await _sut.DetectGoveeDevicesCommand.ExecuteAsync(null);

        // Assert
        _sut.GoveeDeviceId.Should().Be("dev1");
        _sut.GoveeModel.Should().Be("H5184");
        _sut.DeviceList.Should().Contain("Living Room");
        _sut.DeviceList.Should().Contain("Bedroom");
        _sut.StatusMessage.Should().Contain("Se encontraron 2 dispositivo(s)");
    }

    [Fact]
    public async Task DetectGoveeDevicesAsync_WithNoDevices_ShouldShowMessage()
    {
        // Arrange
        _sut.GoveeApiKey = "valid-api-key";

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(new List<GoveeDevice>());

        // Act
        await _sut.DetectGoveeDevicesCommand.ExecuteAsync(null);

        // Assert
        _sut.StatusMessage.Should().Contain("No se encontraron dispositivos");
    }

    [Fact]
    public async Task DetectGoveeDevicesAsync_WithException_ShouldShowError()
    {
        // Arrange
        _sut.GoveeApiKey = "invalid-api-key";

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ThrowsAsync(new InvalidOperationException("API Key inválida"));

        // Act
        await _sut.DetectGoveeDevicesCommand.ExecuteAsync(null);

        // Assert
        _sut.StatusMessage.Should().Contain("Error");
        _sut.StatusMessage.Should().Contain("API Key inválida");
    }

    [Fact]
    public void Properties_ShouldBeObservable()
    {
        // Arrange
        var propertyChangedRaised = false;

        _sut.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Threshold))
                propertyChangedRaised = true;
        };

        // Act
        _sut.Threshold = 21.0;

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Theory]
    [InlineData("api-key-1")]
    [InlineData("very-long-api-key-with-many-characters-123456789")]
    [InlineData("")]
    public void GoveeApiKey_CanBeSetToAnyValue(string apiKey)
    {
        // Act
        _sut.GoveeApiKey = apiKey;

        // Assert
        _sut.GoveeApiKey.Should().Be(apiKey);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(25.5)]
    [InlineData(40.0)]
    public void Threshold_CanBeSetToValidValues(double threshold)
    {
        // Act
        _sut.Threshold = threshold;

        // Assert
        _sut.Threshold.Should().Be(threshold);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    public void IntervalSeconds_CanBeSetToValidValues(int interval)
    {
        // Act
        _sut.IntervalSeconds = interval;

        // Assert
        _sut.IntervalSeconds.Should().Be(interval);
    }

    [Fact]
    public void MerossCredentials_CanBeSet()
    {
        // Act
        _sut.MerossEmail = "user@example.com";
        _sut.MerossPassword = "password123";
        _sut.MerossSecret = "secret-key";
        _sut.MerossDevice = "SmartPlug-001";

        // Assert
        _sut.MerossEmail.Should().Be("user@example.com");
        _sut.MerossPassword.Should().Be("password123");
        _sut.MerossSecret.Should().Be("secret-key");
        _sut.MerossDevice.Should().Be("SmartPlug-001");
    }

    [Fact]
    public async Task DetectGoveeDevicesAsync_ShouldSaveApiKeyToPreferences()
    {
        // Arrange
        const string apiKey = "detect-api-key";
        _sut.GoveeApiKey = apiKey;

        var devices = new List<GoveeDevice>
        {
            new GoveeDevice { Device = "dev1", Model = "H5184", DeviceName = "Test" }
        };

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(devices);

        // Act
        await _sut.DetectGoveeDevicesCommand.ExecuteAsync(null);

        // Assert
        _preferences.Get(SettingsKeys.GoveeApiKey, "").Should().Be(apiKey);
    }

    [Fact]
    public void StatusMessage_ShouldStartEmpty()
    {
        // Act & Assert
        _sut.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void DeviceList_ShouldStartEmpty()
    {
        // Act & Assert
        _sut.DeviceList.Should().BeEmpty();
    }
}
