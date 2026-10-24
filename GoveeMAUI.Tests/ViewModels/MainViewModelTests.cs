using GoveeMAUI.Models;
using GoveeMAUI.Services;
using GoveeMAUI.ViewModels;

namespace GoveeMAUI.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly Mock<IMonitorService> _monitorMock;
    private readonly TestPreferencesService _preferences;
    private readonly MainViewModel _sut;

    public MainViewModelTests()
    {
        _monitorMock = MockFactory.CreateMonitorServiceMock();
        _preferences = new TestPreferencesService();
        _sut = new MainViewModel(_monitorMock.Object, _preferences);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _sut.Should().NotBeNull();
        _sut.Temperature.Should().Be("--.-");
        _sut.Humidity.Should().Be("--.-");
        _sut.IsRunning.Should().BeFalse();
        _sut.CurrentMode.Should().Be(OperationMode.Manual);
    }

    [Fact]
    public void Constructor_ShouldLoadThresholdFromPreferences()
    {
        // Arrange
        _preferences.Set(SettingsKeys.Threshold, 22.5);

        // Act
        var vm = new MainViewModel(_monitorMock.Object, _preferences);

        // Assert
        vm.Threshold.Should().Be(22.5);
    }

    [Fact]
    public async Task ToggleMonitorAsync_FromManualToMonitoring_ShouldStartMonitor()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-key");
        _preferences.Set(SettingsKeys.Threshold, 25);

        _monitorMock
            .Setup(m => m.StartAsync())
            .Returns(Task.CompletedTask);

        _sut.Threshold = 25;
        _sut.CurrentMode = OperationMode.Manual;

        // Act
        await _sut.ToggleMonitorCommand.ExecuteAsync(null);

        // Assert
        _monitorMock.Verify(m => m.StartAsync(), Times.Once);
        _sut.CurrentMode.Should().Be(OperationMode.Monitoring);
        _sut.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleMonitorAsync_FromMonitoringToManual_ShouldStopMonitor()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Monitoring;
        _sut.IsRunning = true;

        // Act
        await _sut.ToggleMonitorCommand.ExecuteAsync(null);

        // Assert
        _monitorMock.Verify(m => m.Stop(), Times.Once);
        _sut.CurrentMode.Should().Be(OperationMode.Manual);
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldShowAppVersion()
    {
        _sut.AppVersionText.Should().StartWith("Versión ");
        _sut.Log.Should().Contain(_sut.AppVersionText);
    }

    [Fact]
    public void StoppedByExternalChange_ShouldSwitchToManualMode()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Monitoring;
        _sut.IsRunning = true;
        _sut.PlugOn = true;

        // Act: apagado desde la app de Meross
        _monitorMock.Raise(m => m.OnStoppedByExternalChange += null, false);

        // Assert
        _sut.CurrentMode.Should().Be(OperationMode.Manual);
        _sut.IsRunning.Should().BeFalse();
        _sut.PlugOn.Should().BeFalse();
        _monitorMock.Verify(m => m.StartAsync(), Times.Never);
    }

    [Fact]
    public async Task ToggleMonitorAsync_WithoutApiKey_ShouldNotStart()
    {
        // Arrange
        _preferences.Remove(SettingsKeys.GoveeApiKey);
        _sut.CurrentMode = OperationMode.Manual;

        // Act
        await _sut.ToggleMonitorCommand.ExecuteAsync(null);

        // Assert
        _monitorMock.Verify(m => m.StartAsync(), Times.Never);
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void SaveThresholdCommand_ShouldPersistThreshold()
    {
        // Arrange
        _sut.Threshold = 23.5;

        // Act
        _sut.SaveThresholdCommand.Execute(null);

        // Assert
        _preferences.Get(SettingsKeys.Threshold, 0.0).Should().Be(23.5);
    }

    [Fact]
    public async Task TogglePlugManuallyAsync_InManualMode_ShouldTogglePlug()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Manual;
        _sut.PlugOn = false;

        _monitorMock
            .Setup(m => m.SetPlugManuallyAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TogglePlugManuallyCommand.ExecuteAsync(null);

        // Assert
        _monitorMock.Verify(m => m.SetPlugManuallyAsync(true), Times.Once);
        _sut.PlugOn.Should().BeTrue();
    }

    [Fact]
    public async Task TogglePlugManuallyAsync_InMonitoringMode_ShouldStopMonitoringFirst()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Monitoring;
        _sut.IsRunning = true;
        _sut.PlugOn = false;

        _monitorMock
            .Setup(m => m.SetPlugManuallyAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TogglePlugManuallyCommand.ExecuteAsync(null);

        // Assert
        _monitorMock.Verify(m => m.Stop(), Times.Once);
        _monitorMock.Verify(m => m.SetPlugManuallyAsync(true), Times.Once);
        _sut.CurrentMode.Should().Be(OperationMode.Manual);
    }

    [Fact]
    public void ManualPlugButtonText_InManualMode_ShouldShowCorrectText()
    {
        // Arrange
        _sut.PlugOn = false;
        _sut.CurrentMode = OperationMode.Manual;

        // Act & Assert
        _sut.ManualPlugButtonText.Should().Contain("🔌");
    }

    [Fact]
    public void IsManualPlugButtonEnabled_InMonitoringMode_ShouldStayTrue()
    {
        // El botón manual NO debe deshabilitarse por monitorizar: al pulsarlo se
        // detiene la monitorización y se pasa a modo manual. Deshabilitarlo hacía
        // que pulsarlo no produjera ningún efecto ni mensaje de log.
        _sut.CurrentMode = OperationMode.Monitoring;

        _sut.IsManualPlugButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsManualPlugButtonEnabled_InManualMode_ShouldBeTrue()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Manual;

        // Act & Assert
        _sut.IsManualPlugButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsManualPlugButtonEnabled_ShouldBeReenabledAfterCommandFails()
    {
        _sut.CurrentMode = OperationMode.Manual;
        _monitorMock
            .Setup(m => m.SetPlugManuallyAsync(It.IsAny<bool>()))
            .ThrowsAsync(new Exception("enchufe no disponible"));

        await _sut.TogglePlugManuallyCommand.ExecuteAsync(null);

        // Si no se reactivara, un único fallo dejaría el botón muerto para siempre.
        _sut.IsManualPlugButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void ControlBandText_ShouldShowOnAndOffTemperatures()
    {
        _sut.Threshold = 19;
        _sut.HysteresisMargin = 1;

        _sut.ControlBandText.Should().Be($"Enciende por debajo de {19.0:F1} °C · Apaga desde {20.0:F1} °C");
    }

    [Fact]
    public void ControlBandText_ShouldNotifyWhenThresholdChanges()
    {
        var changed = new List<string?>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _sut.Threshold = 21;

        changed.Should().Contain(nameof(MainViewModel.ControlBandText));
    }

    [Fact]
    public void ReloadSettings_ShouldPickUpValuesSavedInSettings()
    {
        _preferences.Set(SettingsKeys.Threshold, 22.5);
        _preferences.Set(SettingsKeys.HysteresisMargin, 0.5);

        _sut.ReloadSettings();

        _sut.Threshold.Should().Be(22.5);
        _sut.HysteresisMargin.Should().Be(0.5);
    }

    [Fact]
    public async Task StartingMonitoring_ShouldRequestBackgroundExemption()
    {
        var keepAlive = new Mock<IKeepAliveService>();
        var vm = new MainViewModel(_monitorMock.Object, _preferences, keepAlive.Object);
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-key");

        await vm.ToggleMonitorCommand.ExecuteAsync(null);

        keepAlive.Verify(k => k.RequestBackgroundExemption(), Times.Once);
    }

    [Fact]
    public void Constructor_WhenMonitoringWasLeftEnabled_ShouldResumeIt()
    {
        // Reabrir la app tras cerrarla (o tras un reinicio) con la monitorización activada
        _preferences.Set(SettingsKeys.MonitoringEnabled, 1);

        var vm = new MainViewModel(_monitorMock.Object, _preferences);

        vm.CurrentMode.Should().Be(OperationMode.Monitoring);
        vm.IsRunning.Should().BeTrue();
        _monitorMock.Verify(m => m.StartAsync(), Times.Once);
    }

    [Fact]
    public void Constructor_WhenMonitorAlreadyRunning_ShouldShowItsStateWithoutRestarting()
    {
        // La app se cerró desde recientes pero el proceso siguió vivo monitorizando
        _monitorMock.Setup(m => m.IsRunning).Returns(true);
        _monitorMock.Setup(m => m.PlugState).Returns(true);
        _monitorMock.Setup(m => m.LastReading)
            .Returns(new SensorReading { Temperature = 18.4, Humidity = 55, Online = true });

        var vm = new MainViewModel(_monitorMock.Object, _preferences);

        vm.CurrentMode.Should().Be(OperationMode.Monitoring);
        vm.PlugOn.Should().BeTrue();
        vm.Temperature.Should().Be($"{18.4:F1}");
        _monitorMock.Verify(m => m.StartAsync(), Times.Never);
    }

    [Fact]
    public void Constructor_WhenMonitoringWasStopped_ShouldStayInManualMode()
    {
        _preferences.Set(SettingsKeys.MonitoringEnabled, 0);

        var vm = new MainViewModel(_monitorMock.Object, _preferences);

        vm.CurrentMode.Should().Be(OperationMode.Manual);
        _monitorMock.Verify(m => m.StartAsync(), Times.Never);
    }
}
