using System.Text;
using System.Text.Json;
using GoveeMAUI.Models;

namespace GoveeMAUI.Services;

public class GoveeService
{
    private const string BaseUrl = "https://openapi.api.govee.com";
    private readonly IHttpClientFactory _httpClientFactory;
    private const int MaxRetries = 3;
    private const int InitialDelayMs = 2000;

    public GoveeService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient GetClient()
    {
        var key = Preferences.Get(SettingsKeys.GoveeApiKey, "");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Falta la API Key de Govee. Ve a Ajustes.");

        var client = _httpClientFactory.CreateClient("govee");
        client.DefaultRequestHeaders.Remove("Govee-API-Key");
        client.DefaultRequestHeaders.Add("Govee-API-Key", key);
        return client;
    }

    /// <summary>
    /// Ejecuta una operación con reintentos exponenciales.
    /// </summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                var delayMs = InitialDelayMs * (int)Math.Pow(2, attempt);
                System.Diagnostics.Debug.WriteLine($"[Govee] {operationName} falló (intento {attempt + 1}/{MaxRetries}): {ex.Message}. Reintentando en {delayMs}ms...");
                await Task.Delay(delayMs);
            }
        }
        
        throw new InvalidOperationException($"{operationName} falló después de {MaxRetries} intentos.");
    }

    // ── Listado de dispositivos (API v2) ─────────────────────────────────────

    public async Task<List<GoveeDevice>> GetDevicesAsync()
    {
        return await RetryAsync(async () =>
        {
            var response = await GetClient().GetAsync("/router/api/v1/user/devices");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoveeV2DeviceListResponse>(json);

            // Convertimos los dispositivos v2 al modelo interno GoveeDevice
            return (result?.Data ?? []).Select(d => new GoveeDevice
            {
                Device = d.Device,
                Model = d.Sku,
                DeviceName = d.DeviceName
            }).ToList();
        }, "GetDevicesAsync");
    }

    // ── Estado del dispositivo (temperatura y humedad) ────────────────────────

    public async Task<SensorReading> GetDeviceStateAsync(string deviceId, string model)
    {
        return await RetryAsync(async () =>
        {
            var body = new
            {
                requestId = Guid.NewGuid().ToString(),
                payload = new
                {
                    sku = model,
                    device = deviceId
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await GetClient().PostAsync("/router/api/v1/device/state", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoveeV2StateResponse>(json);

            var reading = new SensorReading { Online = true };

            foreach (var cap in result?.Payload?.Capabilities ?? [])
            {
                if (cap.Instance == "sensorTemperature" && cap.State?.Value != null)
                {
                    var raw = ParseValue(cap.State.Value);

                    // Govee API v2 devuelve temperatura en décimas de grado
                    // Si el valor es > 200 viene multiplicado x100 (ej: 1960 = 19.60°C)
                    if (raw > 200)
                        raw /= 100.0;

                    // Si tras dividir sigue siendo > 40 es porque viene en Fahrenheit
                    // (ej: 67.3°F → 19.6°C). Lo convertimos a Celsius.
                    if (raw > 40)
                        raw = (raw - 32) * 5.0 / 9.0;

                    reading.Temperature = Math.Round(raw, 1);
                }
                else if (cap.Instance == "sensorHumidity" && cap.State?.Value != null)
                {
                    reading.Humidity = ParseValue(cap.State.Value);
                    if (reading.Humidity > 100)
                        reading.Humidity /= 100.0;
                }
                else if (cap.Instance == "online" && cap.State?.Value != null)
                {
                    reading.Online = cap.State.Value.ToString() == "true" ||
                                     cap.State.Value.ToString() == "True" ||
                                     cap.State.Value.ToString() == "1";
                }
            }

            return reading;
        }, "GetDeviceStateAsync");
    }

    private static double ParseValue(object value)
    {
        if (value is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(), out var v) ? v : 0,
                _ => 0
            };
        }
        return double.TryParse(value.ToString(), out var r) ? r : 0;
    }
}