using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class MerossService : IAsyncDisposable
{
    private const string CloudBaseUrl = "https://iotx-eu.meross.com";
    private const string MqttBroker = "mqtt.meross.com";
    private const int MqttPort = 2001;
    private const string DefaultSecret = "23x17ahWarFH6w29";  // Fallback por compatibilidad
    private const int MaxMqttReconnectAttempts = 10;

    private readonly HttpClient _http;
    private IMqttClient? _mqttClient;
    private int _mqttReconnectAttempts = 0;

    private string _userId = "";
    private string _key = "";
    private string _token = "";
    private string _deviceUUID = "";
    private string _deviceInnerIp = "";
    private string _mqttDomain = MqttBroker;
    private readonly string _appId = Guid.NewGuid().ToString("N")[..16];

    public bool IsConnected => _mqttClient?.IsConnected == true;
    public event Action<string>? OnLog;

    public MerossService()
    {
        _http = new HttpClient { BaseAddress = new Uri(CloudBaseUrl) };
        _http.DefaultRequestHeaders.Add("vender", "Meross");
        _http.DefaultRequestHeaders.Add("AppVersion", "1.9.0");
        _http.DefaultRequestHeaders.Add("AppLanguage", "en");
        _http.DefaultRequestHeaders.Add("User-Agent", "okhttp/3.6.0");
    }

    // ── Inicialización ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var email = Preferences.Get(SettingsKeys.MerossEmail, "");
        var password = Preferences.Get(SettingsKeys.MerossPassword, "");
        var devName = Preferences.Get(SettingsKeys.MerossDevice, "");

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

    // ── Login ────────────────────────────────────────────────────────────────

    private async Task LoginAsync(string email, string password)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = GenerateNonce();
        var paramsJson = JsonSerializer.Serialize(new { email, password });
        var params64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(paramsJson));
        var secret = Preferences.Get(SettingsKeys.MerossSecret, DefaultSecret);
        var sign = Md5(secret + timestamp + nonce + params64);

        var formData = new Dictionary<string, string>
        {
            ["params"] = params64,
            ["sign"] = sign,
            ["timestamp"] = timestamp.ToString(),
            ["nonce"] = nonce
        };

        var resp = await _http.PostAsync("/v1/Auth/signIn", new FormUrlEncodedContent(formData));
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
        var secret = Preferences.Get(SettingsKeys.MerossSecret, DefaultSecret);
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

        var resp = await _http.SendAsync(req);
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
            
            // Implementar reconexión con backoff exponencial
            if (_mqttReconnectAttempts < MaxMqttReconnectAttempts)
            {
                _mqttReconnectAttempts++;
                var delayMs = 3000 * (int)Math.Pow(2, Math.Min(_mqttReconnectAttempts - 1, 3)); // Max backoff: 3s * 2^3 = 24s
                Log($"🔄 Reconectando MQTT (intento {_mqttReconnectAttempts}/{MaxMqttReconnectAttempts}) en {delayMs}ms...");
                
                await Task.Delay(delayMs);
                try
                {
                    await ConnectMqttAsync();
                }
                catch (Exception ex)
                {
                    Log($"❌ Reconexión MQTT fallida (intento {_mqttReconnectAttempts}): {ex.Message}");
                }
            }
            else
            {
                Log($"❌ MQTT: Máximo de reconexiones alcanzado ({MaxMqttReconnectAttempts}). Requiere intervención manual.");
            }
        };

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
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .WithCleanSession(true)
            .Build();

        var connectResult = await _mqttClient.ConnectAsync(options);
        Log($"✅ MQTT conectado. CONNACK={connectResult.ResultCode}");
        
        // Reset de intentos al conectar exitosamente
        _mqttReconnectAttempts = 0;

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
            Log($"📨 MQTT recibido [{e.ApplicationMessage.Topic}]:\n   {payload}");
        }
        catch (Exception ex)
        {
            Log($"❌ Error procesando mensaje MQTT: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    // ── Control del enchufe ──────────────────────────────────────────────────

    public async Task SetPlugAsync(bool turnOn)
    {
        Log($"🔌 SetPlugAsync({(turnOn ? "ON" : "OFF")}) iniciado");

        // Estrategia 1: HTTP local (más fiable si estamos en la misma red)
        if (!string.IsNullOrWhiteSpace(_deviceInnerIp))
        {
            Log($"🌐 Intentando control HTTP local → {_deviceInnerIp}");
            var httpOk = await TryLocalHttpAsync(turnOn);
            if (httpOk)
            {
                Log("✅ Control HTTP local exitoso.");
                return;
            }
            Log("⚠️ HTTP local falló. Intentando MQTT cloud...");
        }
        else
        {
            Log("⚠️ No hay IP local disponible. Usando MQTT cloud.");
        }

        // Estrategia 2: MQTT cloud
        await SendMqttCommandAsync(turnOn);
    }

    // ── HTTP Local ────────────────────────────────────────────────────────────

    private async Task<bool> TryLocalHttpAsync(bool turnOn)
    {
        // Intentar 2 veces con timeout
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var msgId = Guid.NewGuid().ToString("N");
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var envelope = new
                {
                    header = new
                    {
                        messageId = msgId,
                        method = "SET",
                        from = $"http://{_deviceInnerIp}/config",
                        @namespace = "Appliance.Control.ToggleX",
                        payloadVersion = 1,
                        sign = Md5(msgId + _key + ts),
                        timestamp = ts,
                        triggerSrc = "app",
                        uuid = _deviceUUID
                    },
                    payload = new
                    {
                        togglex = new { channel = 0, onoff = turnOn ? 1 : 0 }
                    }
                };

                var json = JsonSerializer.Serialize(envelope);
                var url = $"http://{_deviceInnerIp}/config";
                Log($"   POST {url} (intento {attempt + 1}/2)");

                using var localHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await localHttp.PostAsync(url, content);
                var respBody = await resp.Content.ReadAsStringAsync();

                Log($"   HTTP local response ({(int)resp.StatusCode}): {respBody}");
                if (resp.IsSuccessStatusCode)
                    return true;
                    
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
        
        return false;
    }

    // ── MQTT Cloud ────────────────────────────────────────────────────────────

    private async Task SendMqttCommandAsync(bool turnOn)
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

        var msgId = Guid.NewGuid().ToString("N");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var envelope = new
        {
            header = new
            {
                from = $"/app/{_userId}-{_appId}/subscribe",
                messageId = msgId,
                method = "SET",
                @namespace = "Appliance.Control.ToggleX",
                payloadVersion = 1,
                sign = Md5(msgId + _key + ts),
                timestamp = ts,
                triggerSrc = "app",
                uuid = _deviceUUID
            },
            payload = new
            {
                togglex = new { channel = 0, onoff = turnOn ? 1 : 0 }
            }
        };

        var json = JsonSerializer.Serialize(envelope);
        var topic = $"/appliance/{_deviceUUID}/subscribe";

        Log($"📤 MQTT → {topic}");
        Log($"   Comando: {(turnOn ? "ENCENDER" : "APAGAR")}");

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var pubResult = await _mqttClient!.PublishAsync(msg);
        Log($"   ✅ MQTT Publish enviado.");

        // Esperar respuesta del dispositivo
        await Task.Delay(3000);
    }

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

    public async ValueTask DisposeAsync()
    {
        if (_mqttClient != null)
        {
            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();
            _mqttClient.Dispose();
        }
        _http.Dispose();
    }
}