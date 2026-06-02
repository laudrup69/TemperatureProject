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
    public void IsManualPlugButtonEnabled_InMonitoringMode_ShouldBeFalse()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Monitoring;

        // Act & Assert
        _sut.IsManualPlugButtonEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsManualPlugButtonEnabled_InManualMode_ShouldBeTrue()
    {
        // Arrange
        _sut.CurrentMode = OperationMode.Manual;

        // Act & Assert
        _sut.IsManualPlugButtonEnabled.Should().BeTrue();
    }
}
