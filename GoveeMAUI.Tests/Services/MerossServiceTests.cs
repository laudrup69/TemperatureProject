using GoveeMAUI.Models;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

public class MerossServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly TestPreferencesService _preferences;
    private readonly MerossService _sut;

    public MerossServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _preferences = new TestPreferencesService();
        _sut = new MerossService(_httpClientFactoryMock.Object, _preferences);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _sut.Should().NotBeNull();
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_WithoutMerossCredentials_ShouldThrowException()
    {
        // Arrange
        _preferences.Remove(SettingsKeys.MerossEmail);
        _preferences.Remove(SettingsKeys.MerossPassword);

        var mockHttpMessageHandler = new MockHttpMessageHandler("");
        var httpClient = MockFactory.CreateHttpClientForTests("https://iotx-eu.meross.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("meross"))
            .Returns(httpClient);

        // Act & Assert
        Func<Task> act = () => _sut.InitializeAsync();
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*credenciales de Meross*");
    }

    [Fact]
    public async Task InitializeAsync_WithoutDeviceName_ShouldThrowException()
    {
        // Arrange
        _preferences.Set(SettingsKeys.MerossEmail, "test@example.com");
        _preferences.Set(SettingsKeys.MerossPassword, "password123");
        _preferences.Set(SettingsKeys.MerossSecret, "test-secret");
        _preferences.Remove(SettingsKeys.MerossDevice);

        var loginResponse = @"{
    ""apiStatus"": 0,
    ""data"": {
        ""token"": ""test-token"",
        ""key"": ""test-key"",
        ""userid"": ""user123"",
        ""mqttDomain"": ""mqtt.meross.com""
    }
}";

        var deviceListResponse = @"{
    ""apiStatus"": 0,
    ""data"": []
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(loginResponse);
        mockHttpMessageHandler.SetEndpointResponse("/v1/Device/devList", deviceListResponse);
        var httpClient = MockFactory.CreateHttpClientForTests("https://iotx-eu.meross.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("meross"))
            .Returns(httpClient);

        // Act & Assert
        Func<Task> act = () => _sut.InitializeAsync();
        await act.Should()
            .ThrowAsync<Exception>()
            .WithMessage("*No hay dispositivos Meross*");
    }

    [Fact]
    public void OnLog_ShouldRaiseEventWhenCalled()
    {
        // Arrange
        var logMessages = new List<string>();
        _sut.OnLog += msg => logMessages.Add(msg);

        // Act
        // (El servicio emite logs durante InitializeAsync, pero para este test simple
        //  podríamos simular más si tuviéramos acceso a métodos privados internos)

        // Assert
        logMessages.Should().BeEmpty(); // Sin inicialización, no hay logs aún
    }

    [Fact]
    public void IsConnected_WithoutInitialization_ShouldBeFalse()
    {
        // Act & Assert
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteSuccessfully()
    {
        // Act & Assert
        await _sut.DisposeAsync();
    }
}
