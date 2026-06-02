using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

public class MonitorServiceTests
{
    private readonly Mock<IGoveeService> _goveeMock;
    private readonly Mock<IMerossService> _merossMock;
    private readonly TestPreferencesService _preferences;
    private readonly MonitorService _sut;

    public MonitorServiceTests()
    {
        _goveeMock = MockFactory.CreateGoveeServiceMock();
        _merossMock = MockFactory.CreateMerossServiceMock();
        _preferences = new TestPreferencesService();
        _sut = new MonitorService(_goveeMock.Object, _merossMock.Object, _preferences);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _sut.Should().NotBeNull();
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_ShouldSetIsRunningToTrue()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "30");

        var reading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        var tcs = new TaskCompletionSource();
        _sut.OnLogMessage += msg =>
        {
            if (msg.Contains("Monitorización"))
                tcs.SetResult();
        };

        // Act
        _sut.StartAsync().Wait(1000);

        // Assert - Esperar breve tiempo para que el bucle inicie
        await Task.Delay(100);
        _sut.IsRunning.Should().BeTrue();

        // Cleanup
        _sut.Stop();
    }

    [Fact]
    public void Stop_ShouldSetIsRunningToFalse()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "30");

        var reading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        // Act
        _sut.StartAsync().Wait(1000);
        Task.Delay(100).Wait();
        _sut.Stop();

        // Assert
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WithoutDeviceId_ShouldAutoDiscoverDevice()
    {
        // Arrange
        _preferences.Remove(SettingsKeys.GoveeDeviceId);
        _preferences.Remove(SettingsKeys.GoveeModel);
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "30");

        var devices = new List<GoveeDevice>
        {
            new GoveeDevice { Device = "auto-device-id", Model = "H5184", DeviceName = "Auto Sensor" }
        };

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(devices);

        var reading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        var discovered = false;
        _sut.OnLogMessage += msg =>
        {
            if (msg.Contains("Sensor detectado"))
                discovered = true;
        };

        // Act
        _sut.StartAsync().Wait(1000);
        await Task.Delay(200);

        // Assert
        discovered.Should().BeTrue();
        _preferences.Get(SettingsKeys.GoveeDeviceId, "").Should().Be("auto-device-id");

        // Cleanup
        _sut.Stop();
    }

    [Fact]
    public async Task StartAsync_WithoutDevices_ShouldRaiseErrorEvent()
    {
        // Arrange
        _preferences.Remove(SettingsKeys.GoveeDeviceId);
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "30");

        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(new List<GoveeDevice>());

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        var errorRaised = false;
        _sut.OnError += msg =>
        {
            if (msg.Contains("No se encontró ningún sensor"))
                errorRaised = true;
        };

        // Act
        _sut.StartAsync().Wait(1000);
        await Task.Delay(200);

        // Assert
        errorRaised.Should().BeTrue();
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SetPlugManuallyAsync_ShouldUpdatePlugState()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");

        var plugStateChanges = new List<bool>();
        _sut.OnPlugStateChanged += state => plugStateChanges.Add(state);

        _merossMock
            .Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SetPlugManuallyAsync(true);

        // Assert
        plugStateChanges.Should().Contain(true);
        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Once);
    }

    [Fact]
    public async Task RunLoop_ShouldTurnOnWhenBelowThreshold()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, 25.0);
        _preferences.Set(SettingsKeys.Interval, 1);

        var reading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        _merossMock
            .Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartAsync();
        await Task.Delay(1500);

        // Assert
        _merossMock.Verify(m => m.SetPlugAsync(true), Times.AtLeastOnce, 
            "Temperatura bajo umbral → enchufe encendido");

        // Cleanup
        _sut.Stop();
    }

    [Fact]
    public async Task RunLoop_ShouldTurnOffWhenAboveThresholdWithMargin()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, 25.0);
        _preferences.Set(SettingsKeys.Interval, 1);

        // Primero, temperatura baja para establecer _plugOn = true
        var lowReading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        
        var callCount = 0;
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() =>
            {
                callCount++;
                // Primera llamada: temperatura baja (para encender)
                // Llamadas posteriores: temperatura alta (para apagar)
                var temp = callCount == 1 ? 20 : 26;
                return Task.FromResult<SensorReading?>(
                    new SensorReading { Temperature = temp, Humidity = 50, Online = true }
                );
            });

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        _merossMock
            .Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartAsync();
        await Task.Delay(1500);  // Primero enciende
        await Task.Delay(2000);  // Luego apaga (espera el siguiente intervalo)

        // Assert - Debería haber llamado primero ON luego OFF
        _merossMock.Verify(m => m.SetPlugAsync(true), Times.AtLeastOnce, 
            "Temperatura bajo umbral → enchufe encendido");
        _merossMock.Verify(m => m.SetPlugAsync(false), Times.AtLeastOnce, 
            "Temperatura sobre umbral+margen → enchufe apagado");

        // Cleanup
        _sut.Stop();
    }

    [Fact]
    public async Task OnReadingUpdated_ShouldFireWhenNewReadingReceived()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "1");

        var readings = new List<SensorReading>();
        _sut.OnReadingUpdated += reading => readings.Add(reading);

        var reading1 = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading1);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Meross no disponible"));

        _merossMock
            .Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        _sut.StartAsync().Wait(1000);
        await Task.Delay(500);

        // Assert
        readings.Should().NotBeEmpty();
        readings[0].Temperature.Should().Be(20);

        // Cleanup
        _sut.Stop();
    }

    [Fact]
    public async Task StartAsync_ShouldHandleMerossConnectionFailure()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, "25");
        _preferences.Set(SettingsKeys.Interval, "30");

        var reading = new SensorReading { Temperature = 20, Humidity = 50, Online = true };
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(reading);

        _merossMock
            .Setup(m => m.InitializeAsync())
            .ThrowsAsync(new InvalidOperationException("Servidor Meross no disponible"));

        var warningLogged = false;
        _sut.OnLogMessage += msg =>
        {
            if (msg.Contains("Meross no disponible"))
                warningLogged = true;
        };

        // Act
        _sut.StartAsync().Wait(1000);
        await Task.Delay(100);

        // Assert - El servicio debe continuar funcionando sin Meross
        warningLogged.Should().BeTrue();
        _sut.IsRunning.Should().BeTrue();

        // Cleanup
        _sut.Stop();
    }
}
