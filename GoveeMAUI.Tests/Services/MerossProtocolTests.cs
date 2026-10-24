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
}
