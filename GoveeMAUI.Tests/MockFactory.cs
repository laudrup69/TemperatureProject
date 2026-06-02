using GoveeMAUI.Models;
using GoveeMAUI.Services;
using Moq;

namespace GoveeMAUI.Tests;

/// <summary>
/// Factory para crear mocks con eventos correctamente inicializados
/// </summary>
public static class MockFactory
{
    /// <summary>
    /// Crea un mock de IMerossService con evento OnLog inicializado
    /// </summary>
    public static Mock<IMerossService> CreateMerossServiceMock()
    {
        var mock = new Mock<IMerossService>();
        
        // Inicializar evento OnLog para que no sea null
        var onLogEvent = new EventRaiser();
        mock.SetupAdd(m => m.OnLog += It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => onLogEvent.Subscribe(a));
        mock.SetupRemove(m => m.OnLog -= It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => onLogEvent.Unsubscribe(a));
        
        // Setup default behaviors
        mock.Setup(m => m.IsConnected).Returns(true);
        mock.Setup(m => m.InitializeAsync()).Returns(Task.CompletedTask);
        mock.Setup(m => m.SetPlugAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        mock.Setup(m => m.DisconnectAsync()).Returns(Task.CompletedTask);
        
        return mock;
    }

    /// <summary>
    /// Crea un mock de IGoveeService
    /// </summary>
    public static Mock<IGoveeService> CreateGoveeServiceMock()
    {
        var mock = new Mock<IGoveeService>();
        
        // Setup default behaviors
        mock.Setup(g => g.GetDevicesAsync())
            .ReturnsAsync(new List<GoveeDevice> { new() { Device = "test123", Model = "H5184", DeviceName = "Test" } });
        mock.Setup(g => g.GetDeviceStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SensorReading { Temperature = 25, Humidity = 50, Online = true });
        
        return mock;
    }

    /// <summary>
    /// Crea un mock de IPreferencesService
    /// </summary>
    public static Mock<IPreferencesService> CreatePreferencesServiceMock()
    {
        return new Mock<IPreferencesService>();
    }

    /// <summary>
    /// Crea un HttpClient con BaseAddress configurado para tests
    /// </summary>
    public static HttpClient CreateHttpClientForTests(string baseUrl, HttpMessageHandler messageHandler)
    {
        var client = new HttpClient(messageHandler) { BaseAddress = new Uri(baseUrl) };
        return client;
    }

    /// <summary>
    /// Crea un mock de IMonitorService con eventos correctamente inicializados
    /// </summary>
    public static Mock<IMonitorService> CreateMonitorServiceMock()
    {
        var mock = new Mock<IMonitorService>();
        
        // Inicializar eventos
        var onReadingEvent = new EventRaiser();
        var onPlugEvent = new EventRaiser();
        var onLogEvent = new EventRaiser();
        var onErrorEvent = new EventRaiser();
        
        mock.SetupAdd(m => m.OnReadingUpdated += It.IsAny<Action<SensorReading>>())
            .Callback<Action<SensorReading>>(a => { });
        mock.SetupRemove(m => m.OnReadingUpdated -= It.IsAny<Action<SensorReading>>())
            .Callback<Action<SensorReading>>(a => { });
        
        mock.SetupAdd(m => m.OnPlugStateChanged += It.IsAny<Action<bool>>())
            .Callback<Action<bool>>(a => { });
        mock.SetupRemove(m => m.OnPlugStateChanged -= It.IsAny<Action<bool>>())
            .Callback<Action<bool>>(a => { });
        
        mock.SetupAdd(m => m.OnLogMessage += It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => { });
        mock.SetupRemove(m => m.OnLogMessage -= It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => { });
        
        mock.SetupAdd(m => m.OnError += It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => { });
        mock.SetupRemove(m => m.OnError -= It.IsAny<Action<string>>())
            .Callback<Action<string>>(a => { });
        
        // Setup default behaviors
        mock.Setup(m => m.IsRunning).Returns(false);
        mock.Setup(m => m.StartAsync()).Returns(Task.CompletedTask);
        mock.Setup(m => m.Stop());
        mock.Setup(m => m.SetPlugManuallyAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        
        return mock;
    }
}

/// <summary>
/// Helper para manejar eventos en mocks
/// </summary>
internal class EventRaiser
{
    private readonly List<Action<string>> _subscribers = new();

    public void Subscribe(Action<string> handler)
    {
        if (handler != null)
            _subscribers.Add(handler);
    }

    public void Unsubscribe(Action<string> handler)
    {
        if (handler != null)
            _subscribers.Remove(handler);
    }

    public void Raise(string message)
    {
        foreach (var handler in _subscribers)
            handler?.Invoke(message);
    }
}
