using System.Text.Json;
using GoveeMAUI.Services;

namespace GoveeMAUI.Tests.Services;

/// <summary>
/// Interpretación de las respuestas del enchufe Meross: de ella depende saber si
/// una orden se ejecutó y en qué estado está el enchufe.
/// </summary>
public class MerossProtocolTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParseAck_SetAck_ShouldReturnPayload()
    {
        var ack = MerossService.ParseAck(
            """{"header":{"method":"SETACK","messageId":"abc"},"payload":{}}""", "SET");

        ack.Should().NotBeNull();
    }

    [Fact]
    public void ParseAck_Error_ShouldReturnNull()
    {
        // Lo que devuelve el enchufe con HTTP 200 cuando rechaza la orden
        var ack = MerossService.ParseAck(
            """{"header":{"method":"ERROR","messageId":"abc"},"payload":{"error":{"code":5001,"detail":"sign error"}}}""",
            "SET");

        ack.Should().BeNull();
    }

    [Fact]
    public void ParseAck_AckOfOtherMethod_ShouldReturnNull()
    {
        var ack = MerossService.ParseAck("""{"header":{"method":"GETACK"},"payload":{}}""", "SET");

        ack.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not json</html>")]
    [InlineData("[]")]
    public void ParseAck_InvalidBody_ShouldReturnNull(string body)
    {
        MerossService.ParseAck(body, "SET").Should().BeNull();
    }

    [Theory]
    [InlineData("""{"all":{"digest":{"togglex":[{"channel":0,"onoff":1}]}}}""", true)]
    [InlineData("""{"all":{"digest":{"togglex":[{"channel":0,"onoff":0}]}}}""", false)]
    [InlineData("""{"all":{"digest":{"togglex":[{"channel":1,"onoff":0},{"channel":0,"onoff":1}]}}}""", true)]
    [InlineData("""{"all":{"digest":{"togglex":{"channel":0,"onoff":1}}}}""", true)]
    [InlineData("""{"all":{"digest":{"toggle":{"onoff":0}}}}""", false)]
    public void ParseOnOff_KnownFormats_ShouldReadChannelZero(string payload, bool expected)
    {
        MerossService.ParseOnOff(Json(payload)).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"all":{"digest":{}}}""")]
    [InlineData("""{"all":{"digest":{"togglex":[]}}}""")]
    [InlineData("""{"all":{"digest":{"togglex":[{"channel":0}]}}}""")]
    public void ParseOnOff_UnknownFormat_ShouldReturnNull(string payload)
    {
        MerossService.ParseOnOff(Json(payload)).Should().BeNull();
    }

    [Fact]
    public void ParseMessageId_ShouldReadHeader()
    {
        MerossService.ParseMessageId("""{"header":{"messageId":"abc123"}}""").Should().Be("abc123");
        MerossService.ParseMessageId("not json").Should().BeNull();
    }

    private const string Uuid = "2106abc0123456789";

    [Theory]
    [InlineData("""{"header":{"method":"PUSH","namespace":"Appliance.Control.ToggleX","from":"/appliance/2106abc0123456789/publish"},"payload":{"togglex":[{"channel":0,"onoff":0,"lmTime":1700000000}]}}""", false)]
    [InlineData("""{"header":{"method":"PUSH","namespace":"Appliance.Control.ToggleX","from":"/appliance/2106abc0123456789/publish"},"payload":{"togglex":{"channel":0,"onoff":1}}}""", true)]
    [InlineData("""{"header":{"method":"PUSH","namespace":"Appliance.Control.Toggle","uuid":"2106abc0123456789"},"payload":{"toggle":{"onoff":1}}}""", true)]
    public void ParsePush_ToggleFromOurPlug_ShouldReadState(string json, bool expected)
    {
        MerossService.ParsePush(json, Uuid).Should().Be(expected);
    }

    [Theory]
    // Otro dispositivo de la misma cuenta
    [InlineData("""{"header":{"method":"PUSH","from":"/appliance/otro-enchufe/publish"},"payload":{"togglex":[{"channel":0,"onoff":0}]}}""")]
    // Respuesta a una orden, no un aviso
    [InlineData("""{"header":{"method":"SETACK","from":"/appliance/2106abc0123456789/publish"},"payload":{}}""")]
    // Aviso de otra cosa (p. ej. consumo)
    [InlineData("""{"header":{"method":"PUSH","from":"/appliance/2106abc0123456789/publish"},"payload":{"electricity":{"power":1200}}}""")]
    [InlineData("""{"header":{"method":"PUSH","from":"/appliance/2106abc0123456789/publish"}}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ParsePush_NotAToggleFromOurPlug_ShouldReturnNull(string json)
    {
        MerossService.ParsePush(json, Uuid).Should().BeNull();
    }

    [Fact]
    public void ParsePush_WithoutResolvedDevice_ShouldReturnNull()
    {
        MerossService.ParsePush(
            """{"header":{"method":"PUSH","from":"/appliance//publish"},"payload":{"togglex":[{"channel":0,"onoff":0}]}}""",
            "").Should().BeNull();
    }
}
