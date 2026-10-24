using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

/// <summary>
/// Lógica de control del enchufe: histéresis, estado desconocido, órdenes sin
/// confirmar y parada por errores.
/// </summary>
public class MonitorServiceControlTests
{
    private readonly Mock<IGoveeService> _goveeMock;
    private readonly Mock<IMerossService> _merossMock;
    private readonly TestPreferencesService _preferences;
    private readonly MonitorService _sut;

    public MonitorServiceControlTests()
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

    /// <summary>Arranca y espera a que se procese la primera lectura.</summary>
    private async Task StartAndWaitFirstReadingAsync()
    {
        var firstReading = new TaskCompletionSource();
        _sut.OnReadingUpdated += _ => firstReading.TrySetResult();
        await _sut.StartAsync();
        await firstReading.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // La orden al enchufe se envía justo después de notificar la lectura
        await Task.Delay(100);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    [Fact]
    public async Task InsideHysteresisBand_ShouldNotTurnOff()
    {
        // Caso reportado: umbral 19, 19.4 °C, enchufe encendido → sigue encendido
        // porque con el margen por defecto (0,5) no se apaga hasta 19.5 °C.
        SetActualPlugState(true);
        SetTemperature(19.4);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task UnknownPlugState_AboveUpperLimit_ShouldTurnOff()
    {
        // Antes, el estado empezaba en "apagado" y nunca se mandaba apagar un
        // enchufe que ya estaba encendido al arrancar la app.
        SetActualPlugState(null);
        SetTemperature(20.5);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
    }

    [Fact]
    public async Task PlugReportedOn_AboveUpperLimit_ShouldTurnOff()
    {
        SetActualPlugState(true);
        SetTemperature(21);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
    }

    [Fact]
    public async Task PlugReportedOff_AboveUpperLimit_ShouldNotSendCommand()
    {
        SetActualPlugState(false);
        SetTemperature(21);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task PlugReportedOn_BelowThreshold_ShouldNotSendCommand()
    {
        SetActualPlugState(true);
        SetTemperature(17);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ShouldPublishActualPlugState()
    {
        SetActualPlugState(true);
        SetTemperature(19.4);
        var states = new List<bool>();
        _sut.OnPlugStateChanged += states.Add;

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        states.Should().Equal(true);
    }

    [Fact]
    public async Task CustomMargin_ShouldTurnOffAtThresholdPlusMargin()
    {
        _preferences.Set(SettingsKeys.HysteresisMargin, 0.3);
        SetActualPlugState(true);
        SetTemperature(19.4);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
    }

    [Fact]
    public async Task UnconfirmedCommand_ShouldBeResentOnNextReading()
    {
        SetActualPlugState(false);
        SetTemperature(15);
        _merossMock.Setup(m => m.SetPlugAsync(It.IsAny<bool>())).ReturnsAsync(false);

        await _sut.StartAsync();
        await WaitUntilAsync(
            () => _merossMock.Invocations.Count(i => i.Method.Name == nameof(IMerossService.SetPlugAsync)) >= 2,
            TimeSpan.FromSeconds(4));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.AtLeast(2));
    }

    [Fact]
    public async Task ConfirmedCommand_ShouldNotBeResent()
    {
        SetActualPlugState(false);
        SetTemperature(15);

        await _sut.StartAsync();
        await Task.Delay(2500); // primera lectura + dos intervalos de 1 s
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Once);
    }

    [Fact]
    public async Task FailedCommand_ShouldBeResentOnNextReading()
    {
        SetActualPlugState(false);
        SetTemperature(15);
        _merossMock
            .Setup(m => m.SetPlugAsync(It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("MQTT caído"));

        await _sut.StartAsync();
        await WaitUntilAsync(
            () => _merossMock.Invocations.Count(i => i.Method.Name == nameof(IMerossService.SetPlugAsync)) >= 2,
            TimeSpan.FromSeconds(4));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.AtLeast(2));
    }

    [Fact]
    public async Task TooManyErrors_ShouldKeepRetrying()
    {
        // Con la app cerrada nadie podría volver a arrancarla: un corte de red no
        // debe detener el control.
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("sin red"));
        var errorRaised = false;
        _sut.OnError += _ => errorRaised = true;
        var reads = () => _goveeMock.Invocations.Count(i => i.Method.Name == nameof(IGoveeService.GetDeviceStateAsync));

        await _sut.StartAsync();
        await WaitUntilAsync(() => reads() >= 7, TimeSpan.FromSeconds(10));
        _sut.Stop();

        reads().Should().BeGreaterThanOrEqualTo(7);
        errorRaised.Should().BeFalse();
    }

    [Fact]
    public async Task RecoversAfterErrors_ShouldControlThePlugAgain()
    {
        var calls = 0;
        _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() => ++calls <= 2
                ? Task.FromException<SensorReading?>(new HttpRequestException("sin red"))
                : Task.FromResult<SensorReading?>(new SensorReading { Temperature = 15, Humidity = 50, Online = true }));

        await _sut.StartAsync();
        await WaitUntilAsync(
            () => _merossMock.Invocations.Any(i => i.Method.Name == nameof(IMerossService.SetPlugAsync)),
            TimeSpan.FromSeconds(5));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Once);
    }

    [Fact]
    public async Task DiscoveryNetworkError_ShouldRetryUntilSensorFound()
    {
        _preferences.Remove(SettingsKeys.GoveeDeviceId);
        var calls = 0;
        _goveeMock
            .Setup(g => g.GetDevicesAsync())
            .Returns(() => ++calls == 1
                ? Task.FromException<List<GoveeDevice>>(new HttpRequestException("sin red tras reiniciar"))
                : Task.FromResult(new List<GoveeDevice> { new() { Device = "dev-1", Model = "H5184", DeviceName = "Salón" } }));
        SetTemperature(19.4);
        var reading = new TaskCompletionSource();
        _sut.OnReadingUpdated += _ => reading.TrySetResult();

        await _sut.StartAsync();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _sut.Stop();

        _preferences.Get(SettingsKeys.GoveeDeviceId, "").Should().Be("dev-1");
    }

    [Fact]
    public async Task NoSensorInAccount_ShouldStopAndForgetMonitoring()
    {
        var keepAlive = new Mock<IKeepAliveService>();
        var sut = new MonitorService(_goveeMock.Object, _merossMock.Object, _preferences, keepAlive.Object);
        _preferences.Remove(SettingsKeys.GoveeDeviceId);
        _goveeMock.Setup(g => g.GetDevicesAsync()).ReturnsAsync(new List<GoveeDevice>());
        var error = new TaskCompletionSource();
        sut.OnError += _ => error.TrySetResult();

        await sut.StartAsync();
        await error.Task.WaitAsync(TimeSpan.FromSeconds(3));

        sut.IsRunning.Should().BeFalse();
        _preferences.Get(SettingsKeys.MonitoringEnabled, -1).Should().Be(0);
        keepAlive.Verify(k => k.Stop(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldRememberMonitoringAndKeepAppAlive()
    {
        var keepAlive = new Mock<IKeepAliveService>();
        var sut = new MonitorService(_goveeMock.Object, _merossMock.Object, _preferences, keepAlive.Object);
        SetTemperature(19.4);

        await sut.StartAsync();

        _preferences.Get(SettingsKeys.MonitoringEnabled, -1).Should().Be(1);
        keepAlive.Verify(k => k.Start(), Times.Once);

        sut.Stop();

        _preferences.Get(SettingsKeys.MonitoringEnabled, -1).Should().Be(0);
        keepAlive.Verify(k => k.Stop(), Times.Once);
    }

    [Fact]
    public async Task ConcurrentStarts_ShouldRunASingleLoop()
    {
        // La UI y el servicio de Android pueden pedir el arranque a la vez
        var meross = new TaskCompletionSource();
        _merossMock.Setup(m => m.InitializeAsync()).Returns(meross.Task);
        SetTemperature(19.4);

        var first = _sut.StartAsync();
        var second = _sut.StartAsync();
        meross.SetResult();
        await Task.WhenAll(first, second);
        await Task.Delay(300);
        _sut.Stop();

        _merossMock.Verify(m => m.InitializeAsync(), Times.Once);
        _goveeMock.Verify(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task LastReadingAndPlugState_ShouldBeExposedForTheUi()
    {
        SetActualPlugState(true);
        SetTemperature(18.5);

        await StartAndWaitFirstReadingAsync();
        _sut.Stop();

        _sut.LastReading!.Temperature.Should().Be(18.5);
        _sut.PlugState.Should().BeTrue();
    }

    [Fact]
    public async Task RestartRightAfterStop_ShouldKeepNewRunActive()
    {
        SetTemperature(19.4);

        await _sut.StartAsync();
        await Task.Delay(200);
        _sut.Stop();
        await _sut.StartAsync();

        // El bucle anterior termina ahora; no debe marcar como parado el nuevo
        await Task.Delay(300);
        _sut.IsRunning.Should().BeTrue();

        _sut.Stop();
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SetPlugManuallyAsync_Unconfirmed_ShouldLogWarning()
    {
        _merossMock.Setup(m => m.SetPlugAsync(It.IsAny<bool>())).ReturnsAsync(false);
        var logs = new List<string>();
        _sut.OnLogMessage += logs.Add;

        await _sut.SetPlugManuallyAsync(true);

        logs.Should().Contain(l => l.Contains("no confirmó"));
    }
}
