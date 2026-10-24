using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class MerossService : IMerossService
{
    private const string CloudBaseUrl = "https://iotx-eu.meross.com";
    private const string MqttBroker = "mqtt.meross.com";
    private const int MqttPort = 2001;
    private const string DefaultSecret = "23x17ahWarFH6w29";  // Fallback por compatibilidad
    private const int MaxMqttReconnectAttempts = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPreferencesService _preferences;
    private IMqttClient? _mqttClient;
    private int _mqttReconnectAttempts = 0;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private string _userId = "";
    private string _key = "";
    private string _token = "";
    private string _deviceUUID = "";
    private string _deviceInnerIp = "";
    private string _mqttDomain = MqttBroker;
    private readonly string _appId = Guid.NewGuid().ToString("N")[..16];

    public bool IsConnected => _mqttClient?.IsConnected == true;
    public event Action<string>? OnLog;

    public MerossService(IHttpClientFactory httpClientFactory, IPreferencesService preferences)
    {
        _httpClientFactory = httpClientFactory;
        _preferences = preferences;
    }

    // ── Inicialización ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var email = _preferences.Get(SettingsKeys.MerossEmail, "");
        var password = _preferences.Get(SettingsKeys.MerossPassword, "");
        var devName = _preferences.Get(SettingsKeys.MerossDevice, "");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Faltan credenciales de Meross. Ve a Ajustes.");

        Log("🔐 Login Meross...");
        await LoginAsync(email, password);
        Log($"✅ Login OK. UserId={_userId} Key={_key[..6]}...");

        Log("🔍 Buscando dispositivos...");
        await ResolveDeviceAsync(devName);
        Log($"✅ Dispositivo: UUID={_deviceUUID} IP={_deviceInnerIp}");

        Log($"📡 Conectando MQTT a {_mqttDomain}:{MqttPort}...");
        await ConnectMqttAsync();
    }

    /// <summary>
    /// Inicializa la sesión Meross sólo si aún no hay una resuelta. Idempotente y
    /// serializado, para que cualquier ruta que necesite hablar con el enchufe
    /// (el bucle de monitorización o el botón manual) pueda llamarla sin riesgo
    /// de doble login.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        // Sin UUID no hay nada que firmar ni a quién publicar: ni HTTP local ni MQTT
        // funcionan. Es la señal de que InitializeAsync todavía no corrió.
        if (!string.IsNullOrEmpty(_deviceUUID))
            return;

        await _initGate.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_deviceUUID))
                return;

            Log("ℹ️ Sesión Meross no inicializada. Inicializando ahora...");
            await InitializeAsync();
        }
        finally
        {
            _initGate.Release();
        }
    }

    // ── Login ────────────────────────────────────────────────────────────────

    private async Task LoginAsync(string email, string password)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = GenerateNonce();
        var paramsJson = JsonSerializer.Serialize(new { email, password });
        var params64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(paramsJson));
        var secret = _preferences.Get(SettingsKeys.MerossSecret, DefaultSecret);
        var sign = Md5(secret + timestamp + nonce + params64);

        var formData = new Dictionary<string, string>
        {
            ["params"] = params64,
            ["sign"] = sign,
            ["timestamp"] = timestamp.ToString(),
            ["nonce"] = nonce
        };

        var client = _httpClientFactory.CreateClient("meross");
        var resp = await client.PostAsync("/v1/Auth/signIn", new FormUrlEncodedContent(formData));
        var rawJson = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Login HTTP {(int)resp.StatusCode}: {rawJson}");

        var result = JsonSerializer.Deserialize<MerossLoginResponse>(rawJson);
        if (result?.ApiStatus != 0 || result.Data == null)
            throw new Exception($"Login fallido ({result?.ApiStatus}): {result?.Info}");

        _userId = result.Data.UserId;
        _key = result.Data.Key;
        _token = result.Data.Token;

        if (!string.IsNullOrWhiteSpace(result.Data.MqttDomain))
            _mqttDomain = result.Data.MqttDomain;
    }

    // ── Dispositivos ─────────────────────────────────────────────────────────

    public async Task<List<MerossDevice>> GetDevicesAsync()
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Primero llama a InitializeAsync.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = GenerateNonce();
        var params64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        var secret = _preferences.Get(SettingsKeys.MerossSecret, DefaultSecret);
        var sign = Md5(secret + timestamp + nonce + params64);

        var formData = new Dictionary<string, string>
        {
            ["params"] = params64,
            ["sign"] = sign,
            ["timestamp"] = timestamp.ToString(),
            ["nonce"] = nonce
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/Device/devList");
        req.Headers.Add("Authorization", $"Basic {_token}");
        req.Content = new FormUrlEncodedContent(formData);

        var client = _httpClientFactory.CreateClient("meross");
        var resp = await client.SendAsync(req);
        var rawJson = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetDevices HTTP {(int)resp.StatusCode}: {rawJson}");

        // Log completo para ver todos los campos disponibles
        Log($"   DevList raw: {rawJson}");

        var result = JsonSerializer.Deserialize<MerossDeviceListResponse>(rawJson);
        return result?.Data ?? [];
    }

    private async Task ResolveDeviceAsync(string preferredName)
    {
        var devices = await GetDevicesAsync();
        if (devices.Count == 0)
            throw new Exception("No hay dispositivos Meross.");

        Log($"   Dispositivos: {string.Join(", ", devices.Select(d => $"{d.DevName}[{d.Uuid}] IP={d.InnerIp}"))}");

        var device = string.IsNullOrWhiteSpace(preferredName)
            ? devices[0]
            : devices.FirstOrDefault(d =>
                d.DevName.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
              ?? devices[0];

        _deviceUUID = device.Uuid;
        _deviceInnerIp = device.InnerIp;
        Log($"   → Usando: {device.DevName} IP={_deviceInnerIp} Tipo={device.DeviceType}");
    }

    // ── MQTT ─────────────────────────────────────────────────────────────────

    private async Task ConnectMqttAsync()
    {
        _mqttClient?.Dispose();
        _mqttClient = new MqttFactory().CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += OnMqttMessageReceived;

        _mqttClient.DisconnectedAsync += async e =>
        {
            Log($"⚠️ MQTT desconectado: Reason={e.Reason} Exception={e.Exception?.Message}");
            System.Diagnostics.Debug.WriteLine($"[MerossService] MQTT DisconnectedAsync: {e.Reason}");
            
            // Implementar reconexión con backoff exponencial
            if (_mqttReconnectAttempts < MaxMqttReconnectAttempts)
            {
                _mqttReconnectAttempts++;
                var delayMs = 3000 * (int)Math.Pow(2, Math.Min(_mqttReconnectAttempts - 1, 3)); // Max backoff: 3s * 2^3 = 24s
                Log($"🔄 Reconectando MQTT (intento {_mqttReconnectAttempts}/{MaxMqttReconnectAttempts}) en {delayMs}ms...");
                System.Diagnostics.Debug.WriteLine($"[MerossService] Reconexión MQTT en {delayMs}ms");
                
                await Task.Delay(delayMs);
                try
                {
                    await ConnectMqttAsync();
                }
                catch (Exception ex)
                {
                    Log($"❌ Reconexión MQTT fallida (intento {_mqttReconnectAttempts}): {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[MerossService] Reconexión MQTT falló: {ex.Message}");
                }
            }
            else
            {
                Log($"❌ MQTT: Máximo de reconexiones alcanzado ({MaxMqttReconnectAttempts}). Requiere intervención manual.");
                System.Diagnostics.Debug.WriteLine($"[MerossService] MQTT max reconexiones alcanzadas");
            }
        };

        // Configuración robusta de MQTT con keep-alive más agresivo
        // KeepAlive: envía ping cada 30s para detectar desconexiones silenciosas rápidamente
        var options = new MqttClientOptionsBuilder()
            .WithClientId($"app:{_appId}:{_userId}")
            .WithTcpServer(_mqttDomain, MqttPort)
            .WithTlsOptions(o =>
            {
                o.UseTls();
                o.WithIgnoreCertificateChainErrors();
                o.WithIgnoreCertificateRevocationErrors();
            })
            .WithCredentials(_userId, Md5(_userId + _key))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))  // ⬆️ Reducido de 60s a 30s para detectar desconexiones antes
            .WithCleanSession(true)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)  // Versión estable
            .Build();

        System.Diagnostics.Debug.WriteLine($"[MerossService] Conectando MQTT a {_mqttDomain}:{MqttPort}");
        var connectResult = await _mqttClient.ConnectAsync(options);
        Log($"✅ MQTT conectado. CONNACK={connectResult.ResultCode}");
        System.Diagnostics.Debug.WriteLine($"[MerossService] MQTT conectado exitosamente");
        
        _mqttReconnectAttempts = 0;  // Reset contador después de conexión exitosa

        // Suscribirse a topic de respuestas de la app
        var appTopic = $"/app/{_userId}-{_appId}/subscribe";
        // Suscribirse también al topic de publicaciones del dispositivo
        var deviceTopic = $"/appliance/{_deviceUUID}/publish";

        await _mqttClient.SubscribeAsync(appTopic);
        await _mqttClient.SubscribeAsync(deviceTopic);
        Log($"   Suscrito a: {appTopic}");
        Log($"   Suscrito a: {deviceTopic}");
    }

    private Task OnMqttMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var payload = e.ApplicationMessage.ConvertPayloadToString();
            Log($"📨 MQTT recibido [{e.ApplicationMessage.Topic}]:\n   {Truncate(payload)}");

            var messageId = ParseMessageId(payload);
            if (messageId != null && _pendingAcks.TryGetValue(messageId, out var ackTcs))
                ackTcs.TrySetResult(payload);
        }
        catch (Exception ex)
        {
            Log($"❌ Error procesando mensaje MQTT: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    // ── Control del enchufe ──────────────────────────────────────────────────

    private const string ToggleXNamespace = "Appliance.Control.ToggleX";
    private const string SystemAllNamespace = "Appliance.System.All";
    private static readonly TimeSpan MqttAckTimeout = TimeSpan.FromSeconds(8);

    // Órdenes MQTT esperando respuesta, por messageId: el enchufe contesta en el
    // topic de la app con el mismo messageId que le enviamos.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingAcks = new();

    public async Task<bool> SetPlugAsync(bool turnOn)
    {
        Log($"🔌 SetPlugAsync({(turnOn ? "ON" : "OFF")}) iniciado");

        // El botón manual puede ser lo primero que se pulse tras abrir la app, sin
        // que StartAsync() haya inicializado nada. Garantizamos la sesión aquí.
        await EnsureInitializedAsync();

        var ack = await SendCommandAsync("SET", ToggleXNamespace,
            new { togglex = new { channel = 0, onoff = turnOn ? 1 : 0 } });

        if (ack == null)
        {
            Log("⚠️ El enchufe no confirmó la orden.");
            return false;
        }

        Log($"✅ Enchufe confirmó {(turnOn ? "ENCENDIDO" : "APAGADO")}.");
        return true;
    }

    public async Task<bool?> GetPlugStateAsync()
    {
        await EnsureInitializedAsync();

        var ack = await SendCommandAsync("GET", SystemAllNamespace, new { });
        var state = ack == null ? null : ParseOnOff(ack.Value);

        Log($"🔎 Estado del enchufe: {(state == null ? "desconocido" : state.Value ? "ON" : "OFF")}");
        return state;
    }

    /// <summary>
    /// Envía un mensaje firmado al enchufe y devuelve el payload de su ACK
    /// (SETACK/GETACK), o null si ninguna vía obtuvo confirmación.
    /// </summary>
    private async Task<JsonElement?> SendCommandAsync(string method, string ns, object payload)
    {
        // Estrategia 1: HTTP local (más fiable si estamos en la misma red)
        if (!string.IsNullOrWhiteSpace(_deviceInnerIp))
        {
            Log($"🌐 Intentando HTTP local → {_deviceInnerIp}");
            var localAck = await TryLocalHttpAsync(method, ns, payload);
            if (localAck != null)
            {
                Log("✅ HTTP local confirmado.");
                return localAck;
            }
            Log("⚠️ HTTP local sin confirmación. Intentando MQTT cloud...");
        }
        else
        {
            Log("⚠️ No hay IP local disponible. Usando MQTT cloud.");
        }

        // Estrategia 2: MQTT cloud
        return await SendMqttCommandAsync(method, ns, payload);
    }

    private (string MessageId, string Json) BuildMessage(string method, string ns, object payload, string from)
    {
        var msgId = Guid.NewGuid().ToString("N");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var envelope = new
        {
            header = new
            {
                from,
                messageId = msgId,
                method,
                @namespace = ns,
                payloadVersion = 1,
                sign = Md5(msgId + _key + ts),
                timestamp = ts,
                triggerSrc = "app",
                uuid = _deviceUUID
            },
            payload
        };

        return (msgId, JsonSerializer.Serialize(envelope));
    }

    // ── HTTP Local ────────────────────────────────────────────────────────────

    private async Task<JsonElement?> TryLocalHttpAsync(string method, string ns, object payload)
    {
        // Intentar 2 veces con timeout
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var url = $"http://{_deviceInnerIp}/config";
                var (_, json) = BuildMessage(method, ns, payload, url);
                Log($"   POST {url} (intento {attempt + 1}/2)");

                using var localHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await localHttp.PostAsync(url, content);
                var respBody = await resp.Content.ReadAsStringAsync();

                Log($"   HTTP local response ({(int)resp.StatusCode}): {Truncate(respBody)}");

                // Un 200 no basta: el enchufe responde 200 también cuando rechaza la
                // orden (p. ej. firma inválida), con method=ERROR en la cabecera.
                if (resp.IsSuccessStatusCode)
                {
                    var ack = ParseAck(respBody, method);
                    if (ack != null)
                        return ack;
                    Log("   ⚠️ El enchufe respondió sin ACK.");
                }

                if (attempt < 1)
                {
                    Log($"   ⚠️ HTTP local falló, reintentando...");
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                Log($"   HTTP local excepción (intento {attempt + 1}): {ex.Message}");
                if (attempt < 1)
                    await Task.Delay(1000);
            }
        }

        return null;
    }

    // ── MQTT Cloud ────────────────────────────────────────────────────────────

    private async Task<JsonElement?> SendMqttCommandAsync(string method, string ns, object payload)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            Log("⚠️ MQTT desconectado. Intentando reconectar...");
            try
            {
                await ConnectMqttAsync();
            }
            catch (Exception ex)
            {
                Log($"❌ Fallo al reconectar MQTT: {ex.Message}");
                throw;
            }
        }

        var (msgId, json) = BuildMessage(method, ns, payload, $"/app/{_userId}-{_appId}/subscribe");
        var topic = $"/appliance/{_deviceUUID}/subscribe";

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var ackTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks[msgId] = ackTcs;
        try
        {
            Log($"📤 MQTT {method} {ns} → {topic}");
            await _mqttClient!.PublishAsync(msg);

            // Que el broker acepte el mensaje no significa que el enchufe lo haya
            // ejecutado: esperamos su respuesta con nuestro messageId.
            var finished = await Task.WhenAny(ackTcs.Task, Task.Delay(MqttAckTimeout));
            if (finished != ackTcs.Task)
            {
                Log($"⚠️ El enchufe no respondió por MQTT en {MqttAckTimeout.TotalSeconds:F0}s.");
                return null;
            }

            var ack = ParseAck(await ackTcs.Task, method);
            if (ack == null)
                Log("⚠️ El enchufe respondió por MQTT sin ACK.");
            return ack;
        }
        finally
        {
            _pendingAcks.TryRemove(msgId, out _);
        }
    }

    // ── Parseo de respuestas ─────────────────────────────────────────────────

    /// <summary>
    /// Devuelve el payload si <paramref name="json"/> es el ACK de <paramref name="method"/>
    /// (SET → SETACK, GET → GETACK); null si es un ERROR o no se puede interpretar.
    /// </summary>
    internal static JsonElement? ParseAck(string json, string method)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("header", out var header)
                || !header.TryGetProperty("method", out var m)
                || m.GetString() != method + "ACK")
                return null;

            return root.TryGetProperty("payload", out var payload)
                ? payload.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extrae el estado del canal 0 de la respuesta a Appliance.System.All.
    /// Según el modelo viene en digest.togglex (lista u objeto) o en digest.toggle.
    /// </summary>
    internal static bool? ParseOnOff(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("all", out var all)
            || !all.TryGetProperty("digest", out var digest))
            return null;

        if (digest.TryGetProperty("togglex", out var togglex))
        {
            if (togglex.ValueKind != JsonValueKind.Array)
                return ReadOnOff(togglex);

            foreach (var channel in togglex.EnumerateArray())
            {
                if (!channel.TryGetProperty("channel", out var ch) || ch.GetInt32() == 0)
                    return ReadOnOff(channel);
            }
            return null;
        }

        return digest.TryGetProperty("toggle", out var toggle) ? ReadOnOff(toggle) : null;
    }

    private static bool? ReadOnOff(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty("onoff", out var onoff)
           && onoff.ValueKind == JsonValueKind.Number
            ? onoff.GetInt32() == 1
            : null;

    internal static string? ParseMessageId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("header", out var header)
                   && header.TryGetProperty("messageId", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Log(string msg) => OnLog?.Invoke(msg);

    private static string GenerateNonce()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 16)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }

    private static string Md5(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLower();

    // ── Desconexión ──────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        if (_mqttClient?.IsConnected == true)
        {
            Log("Desconectando MQTT...");
            await _mqttClient.DisconnectAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_mqttClient != null)
        {
            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();
            _mqttClient.Dispose();
        }
        // HttpClient no necesita Dispose (IHttpClientFactory lo maneja)
    }
}