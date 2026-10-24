using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

/// <summary>
/// Cambios del enchufe hechos fuera de esta app (p. ej. desde la de Meross): quien
/// lo cambió manda, así que se deja de monitorizar. Los avisos que provoca la propia
/// app no deben confundirse con un cambio manual.
/// </summary>
public class MonitorServiceExternalChangeTests
{
    private readonly Mock<IGoveeService> _goveeMock;
    private readonly Mock<IMerossService> _merossMock;
    private readonly TestPreferencesService _preferences;
    private readonly MonitorService _sut;

    public MonitorServiceExternalChangeTests()
    {
        _goveeMock = MockFactory.CreateGoveeServiceMock();
        _merossMock = MockFactory.CreateMerossServiceMock();
        _preferences = new TestPreferencesService();
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, 19.0);
        _preferences.Set(SettingsKeys.Interval, 1);
        _sut = new MonitorService(_goveeMock.Object, _merossMock.Object, _preferences);
    }

    private void SetTemperature(double temperature)
        => _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SensorReading { Temperature = temperature, Humidity = 50, Online = true });

    private void SetActualPlugState(bool? state)
        => _merossMock.Setup(m => m.GetPlugStateAsync()).ReturnsAsync(state);

    private void PlugReports(bool on)
        => _merossMock.Raise(m => m.OnPlugStateReported += null, on);

    private async Task StartAndWaitFirstReadingAsync()
    {
        var firstReading = new TaskCompletionSource();
        _sut.OnReadingUpdated += _ => firstReading.TrySetResult();
        await _sut.StartAsync();
        await firstReading.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ChangedOutsideTheApp_ShouldStopMonitoring(bool before, bool after)
    {
        // 19.2 °C está dentro de la banda: la app no envía ninguna orden por su cuenta
        SetActualPlugState(before);
        SetTemperature(19.2);
        bool? reportedState = null;
        _sut.OnStoppedByExternalChange += on => reportedState = on;

        await StartAndWaitFirstReadingAsync();
        PlugReports(after);

        _sut.IsRunning.Should().BeFalse();
        _sut.PlugState.Should().Be(after);
        reportedState.Should().Be(after);
        // No debe restaurarse al reabrir la app ni al reiniciar el móvil
        _preferences.Get(SettingsKeys.MonitoringEnabled, 1).Should().Be(0);
    }

    [Fact]
    public async Task TurnedOffOutsideTheApp_WhileCold_ShouldNotTurnItBackOn()
    {
        SetActualPlugState(true);
        SetTemperature(17.0);

        await StartAndWaitFirstReadingAsync();
        PlugReports(false);
        // Varios intervalos: el bucle ya no debe volver a encenderlo
        await Task.Delay(2500);

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ReportMatchingKnownState_ShouldKeepMonitoring()
    {
        SetActualPlugState(true);
        SetTemperature(19.2);
        var stopped = false;
        _sut.OnStoppedByExternalChange += _ => stopped = true;

        await StartAndWaitFirstReadingAsync();
        PlugReports(true);

        _sut.IsRunning.Should().BeTrue();
        stopped.Should().BeFalse();
        _sut.Stop();
    }

    [Fact]
    public async Task EchoOfOurOwnCommand_ShouldKeepMonitoring()
    {
        // El enchufe avisa del cambio antes de confirmar la orden que lo provocó
        SetActualPlugState(false);
        SetTemperature(17.0);
        _merossMock.Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .Callback<bool>(PlugReports)
            .ReturnsAsync(true);

        await StartAndWaitFirstReadingAsync();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Once);
        _sut.IsRunning.Should().BeTrue();
        _sut.PlugState.Should().BeTrue();
        _sut.Stop();
    }

    [Fact]
    public async Task ReportWithUnknownState_ShouldOnlyUpdateState()
    {
        // Sin saber el estado no se puede distinguir un cambio manual de uno nuestro
        SetActualPlugState(null);
        SetTemperature(19.2);

        await StartAndWaitFirstReadingAsync();
        PlugReports(false);

        _sut.IsRunning.Should().BeTrue();
        _sut.PlugState.Should().BeFalse();
        _sut.Stop();
    }

    [Fact]
    public void ReportWhileNotMonitoring_ShouldNotThrow()
    {
        var act = () => PlugReports(true);

        act.Should().NotThrow();
        _sut.IsRunning.Should().BeFalse();
    }
}
