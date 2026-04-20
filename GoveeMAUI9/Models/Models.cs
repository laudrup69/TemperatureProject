using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoveeMAUI.Models;

// ── Preferences keys ──────────────────────────────────────────────────────────

public static class SettingsKeys
{
    public const string GoveeApiKey = "govee_api_key";
    public const string GoveeDeviceId = "govee_device_id";
    public const string GoveeModel = "govee_model";
    public const string MerossEmail = "meross_email";
    public const string MerossPassword = "meross_password";
    public const string MerossSecret = "meross_secret";
    public const string MerossDevice = "meross_device";
    public const string Threshold = "threshold";
    public const string Interval = "interval_seconds";
}

// ── Govee modelo interno ──────────────────────────────────────────────────────

public class GoveeDevice
{
    public string Device { get; set; } = "";
    public string Model { get; set; } = "";
    public string DeviceName { get; set; } = "";
}

public class SensorReading
{
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public bool Online { get; set; }
}

// ── Govee API v2 — Listado de dispositivos ────────────────────────────────────

public class GoveeV2DeviceListResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public List<GoveeV2Device> Data { get; set; } = [];
}

public class GoveeV2Device
{
    [JsonPropertyName("sku")] public string Sku { get; set; } = "";
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

// ── Govee API v2 — Estado del dispositivo ─────────────────────────────────────

public class GoveeV2StateResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("payload")] public GoveeV2Payload? Payload { get; set; }
}

public class GoveeV2Payload
{
    [JsonPropertyName("sku")] public string Sku { get; set; } = "";
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("capabilities")] public List<GoveeV2Capability> Capabilities { get; set; } = [];
}

public class GoveeV2Capability
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("state")] public GoveeV2State? State { get; set; }
}

public class GoveeV2State
{
    [JsonPropertyName("value")] public object? Value { get; set; }
}

// ── Meross — Login ────────────────────────────────────────────────────────────

public class MerossLoginResponse
{
    [JsonPropertyName("apiStatus")] public int ApiStatus { get; set; }
    [JsonPropertyName("info")] public string Info { get; set; } = "";
    [JsonPropertyName("data")] public MerossLoginData? Data { get; set; }
}

public class MerossLoginData
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("userid")] public string UserId { get; set; } = "";
    [JsonPropertyName("mqttDomain")] public string MqttDomain { get; set; } = "";
}

// ── Meross — Dispositivos ─────────────────────────────────────────────────────

public class MerossDeviceListResponse
{
    [JsonPropertyName("apiStatus")] public int ApiStatus { get; set; }
    [JsonPropertyName("data")] public List<MerossDevice>? Data { get; set; }
}

public class MerossDevice
{
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("devName")] public string DevName { get; set; } = "";
    [JsonPropertyName("deviceType")] public string DeviceType { get; set; } = "";
    [JsonPropertyName("onlineStatus")] public int OnlineStatus { get; set; }
    [JsonPropertyName("innerIp")] public string InnerIp { get; set; } = "";
}

// ── Meross — MQTT ─────────────────────────────────────────────────────────────

public class MerossMqttMessage
{
    [JsonPropertyName("header")] public MerossHeader Header { get; set; } = new();
    [JsonPropertyName("payload")] public object Payload { get; set; } = new { };
}

public class MerossHeader
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("messageId")] public string MessageId { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "SET";
    [JsonPropertyName("namespace")] public string Namespace { get; set; } = "";
    [JsonPropertyName("payloadVersion")] public int PayloadVersion { get; set; } = 1;
    [JsonPropertyName("sign")] public string Sign { get; set; } = "";
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("triggerSrc")] public string TriggerSrc { get; set; } = "app";
}