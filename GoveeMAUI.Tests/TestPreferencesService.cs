using GoveeMAUI.Models;

namespace GoveeMAUI.Tests;

/// <summary>
/// Implementación de prueba que almacena valores en memoria
/// para evitar la inicialización de MAUI Preferences en contexto de tests.
/// </summary>
public class TestPreferencesService : IPreferencesService
{
    private readonly Dictionary<string, object> _store = new();

    public string Get(string key, string defaultValue)
        => _store.TryGetValue(key, out var val) ? val as string ?? defaultValue : defaultValue;

    public int Get(string key, int defaultValue)
        => _store.TryGetValue(key, out var val) ? (val is int i) ? i : defaultValue : defaultValue;

    public double Get(string key, double defaultValue)
        => _store.TryGetValue(key, out var val) ? (val is double d) ? d : defaultValue : defaultValue;

    public void Set(string key, string value) => _store[key] = value;
    public void Set(string key, int value) => _store[key] = value;
    public void Set(string key, double value) => _store[key] = value;

    public void Remove(string key) => _store.Remove(key);
    public void Clear() => _store.Clear();
}
