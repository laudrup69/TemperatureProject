using GoveeMAUI.Models;
using GoveeMAUI.Services;
using System.Text.Json;

namespace GoveeMAUI.Tests.Services;

public class GoveeServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly TestPreferencesService _preferences;
    private readonly GoveeService _sut;

    public GoveeServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _preferences = new TestPreferencesService();
        _sut = new GoveeService(_httpClientFactoryMock.Object, _preferences);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _sut.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDevicesAsync_WithValidApiKey_ShouldReturnDeviceList()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""message"": ""OK"",
    ""data"": [
        {
            ""sku"": ""H5184"",
            ""device"": ""aabbccdd123"",
            ""deviceName"": ""Kitchen Sensor"",
            ""type"": ""TempHumi""
        }
    ]
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDevicesAsync();

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(1);
        result[0].Device.Should().Be("aabbccdd123");
        result[0].DeviceName.Should().Be("Kitchen Sensor");
        result[0].Model.Should().Be("H5184");
    }

    [Fact]
    public async Task GetDevicesAsync_WithoutApiKey_ShouldThrowException()
    {
        // Arrange
        _preferences.Remove(SettingsKeys.GoveeApiKey);

        var mockHttpMessageHandler = new MockHttpMessageHandler("");
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act & Assert
        Func<Task> act = () => _sut.GetDevicesAsync();
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*API Key de Govee*");
    }

    [Theory]
    [InlineData(1960, 19.6, "1960 → 19.60°C: división por 100")]
    [InlineData(196, 91.1, "196 → 91.1°C: convertido de Fahrenheit")]
    [InlineData(22, 22, "22 → 22°C: valor válido ya")]
    public async Task GetDeviceStateAsync_ShouldParseTemperatureCorrectly(double rawTemp, double expectedTemp, string reason)
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""payload"": {
        ""sku"": ""H5184"",
        ""device"": ""aabbccdd123"",
        ""capabilities"": [
            {
                ""type"": ""property"",
                ""instance"": ""sensorTemperature"",
                ""state"": { ""value"": " + rawTemp + @" }
            },
            {
                ""type"": ""property"",
                ""instance"": ""online"",
                ""state"": { ""value"": true }
            }
        ]
    }
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDeviceStateAsync("aabbccdd123", "H5184");

        // Assert - Permitir pequeña variación por redondeo
        result.Temperature.Should().BeApproximately(expectedTemp, 0.1, reason);
        result.Online.Should().BeTrue();
    }

    [Fact]
    public async Task GetDeviceStateAsync_ShouldParseFahrenheitAndConvert()
    {
        // Arrange: 67.3°F ≈ 19.6°C
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""payload"": {
        ""sku"": ""H5184"",
        ""device"": ""aabbccdd123"",
        ""capabilities"": [
            {
                ""type"": ""property"",
                ""instance"": ""sensorTemperature"",
                ""state"": { ""value"": 67.3 }
            }
        ]
    }
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDeviceStateAsync("aabbccdd123", "H5184");

        // Assert
        result.Temperature.Should().BeApproximately(19.6, 0.1);
    }

    [Fact]
    public async Task GetDeviceStateAsync_ShouldParseHumidity()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""payload"": {
        ""sku"": ""H5184"",
        ""device"": ""aabbccdd123"",
        ""capabilities"": [
            {
                ""type"": ""property"",
                ""instance"": ""sensorHumidity"",
                ""state"": { ""value"": 5500 }
            }
        ]
    }
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDeviceStateAsync("aabbccdd123", "H5184");

        // Assert - 5500 / 100 = 55%
        result.Humidity.Should().BeApproximately(55, 0.1);
    }

    [Fact]
    public async Task GetDeviceStateAsync_ShouldHandleOfflineStatus()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""payload"": {
        ""sku"": ""H5184"",
        ""device"": ""aabbccdd123"",
        ""capabilities"": [
            {
                ""type"": ""property"",
                ""instance"": ""online"",
                ""state"": { ""value"": false }
            }
        ]
    }
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDeviceStateAsync("aabbccdd123", "H5184");

        // Assert
        result.Online.Should().BeFalse();
    }

    [Fact]
    public async Task GetDeviceStateAsync_WithoutCapabilities_ShouldReturnDefaultReading()
    {
        // Arrange
        _preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");

        var responseContent = @"{
    ""code"": 0,
    ""payload"": {
        ""sku"": ""H5184"",
        ""device"": ""aabbccdd123"",
        ""capabilities"": []
    }
}";

        var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
        var httpClient = MockFactory.CreateHttpClientForTests("https://openapi.api.govee.com", mockHttpMessageHandler);

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("govee"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetDeviceStateAsync("aabbccdd123", "H5184");

        // Assert
        result.Online.Should().BeTrue();
        result.Temperature.Should().Be(0);
        result.Humidity.Should().Be(0);
    }
}
