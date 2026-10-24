using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

/// <summary>
/// Apagado de seguridad: sin lecturas válidas durante el plazo configurado, la
/// caldera no debe quedarse encendida a ciegas.
/// </summary>
public class MonitorServiceFailsafeTests
{
    private readonly Mock<IGoveeService> _goveeMock;
    private readonly Mock<IMerossService> _merossMock;
    private readonly TestPreferencesService _preferences;
    private readonly FakeClock _clock = new();
    private readonly MonitorService _sut;

    public MonitorServiceFailsafeTests()
    {
        _goveeMock = MockFactory.CreateGoveeServiceMock();
        _merossMock = MockFactory.CreateMerossServiceMock();
        _merossMock.Setup(m => m.GetPlugStateAsync()).ReturnsAsync(true);
        _preferences = new TestPreferencesService();
        _preferences.Set(SettingsKeys.GoveeDeviceId, "device123");
        _preferences.Set(SettingsKeys.GoveeModel, "H5184");
        _preferences.Set(SettingsKeys.Threshold, 19.0);
        _preferences.Set(SettingsKeys.Interval, 1);
        _sut = new MonitorService(_goveeMock.Object, _merossMock.Object, _preferences, time: _clock);
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private void SensorFails()
        => _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("sin red"));

    private void SensorReads(double temperature, bool online = true)
        => _goveeMock
            .Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SensorReading { Temperature = temperature, Humidity = 50, Online = online });

    private int PlugCommands(bool on)
        => _merossMock.Invocations.Count(i =>
            i.Method.Name == nameof(IMerossService.SetPlugAsync) && (bool)i.Arguments[0] == on);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    [Fact]
    public async Task NoReadingsFor30Minutes_ShouldTurnOff()
    {
        SensorFails();

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        await WaitUntilAsync(() => PlugCommands(false) > 0, TimeSpan.FromSeconds(3));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
    }

    [Fact]
    public async Task NoReadingsForLessThan30Minutes_ShouldLeavePlugAlone()
    {
        SensorFails();

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(29));
        await Task.Delay(1500);
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task SensorOffline_ShouldCountAsNoReadings()
    {
        SensorReads(15, online: false);

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        await WaitUntilAsync(() => PlugCommands(false) > 0, TimeSpan.FromSeconds(3));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Never);
    }

    [Fact]
    public async Task ReadingsComeBack_ShouldResumeNormalControl()
    {
        SensorFails();
        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        await WaitUntilAsync(() => PlugCommands(false) > 0, TimeSpan.FromSeconds(3));

        SensorReads(15);
        await WaitUntilAsync(() => PlugCommands(true) > 0, TimeSpan.FromSeconds(3));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(true), Times.Once);
    }

    [Fact]
    public async Task UnconfirmedFailsafeOff_ShouldBeResent()
    {
        SensorFails();
        _merossMock.Setup(m => m.SetPlugAsync(It.IsAny<bool>())).ReturnsAsync(false);

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        await WaitUntilAsync(() => PlugCommands(false) >= 2, TimeSpan.FromSeconds(4));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.AtLeast(2));
    }

    [Fact]
    public async Task FailsafeDisabled_ShouldNeverTurnOff()
    {
        _preferences.Set(SettingsKeys.FailsafeMinutes, 0);
        SensorFails();

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromHours(5));
        await Task.Delay(1500);
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task CustomFailsafePeriod_ShouldBeUsed()
    {
        _preferences.Set(SettingsKeys.FailsafeMinutes, 10);
        SensorFails();

        await _sut.StartAsync();
        _clock.Advance(TimeSpan.FromMinutes(11));
        await WaitUntilAsync(() => PlugCommands(false) > 0, TimeSpan.FromSeconds(3));
        _sut.Stop();

        _merossMock.Verify(m => m.SetPlugAsync(false), Times.Once);
    }
}
