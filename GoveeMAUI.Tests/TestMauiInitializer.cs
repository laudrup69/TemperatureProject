namespace GoveeMAUI.Tests;

/// <summary>
/// Inicializador de MAUI para tests unitarios.
/// Los tests usan TestPreferencesService en lugar de MAUI Preferences,
/// por lo que esta clase está aquí solo como placeholder.
/// </summary>
public static class TestMauiInitializer
{
    private static bool _initialized = false;
    private static readonly object _lockObject = new();

    /// <summary>
    /// Inicializa el entorno de tests.
    /// Los tests usan TestPreferencesService (en memoria) en lugar de MAUI Preferences,
    /// evitando completamente la dependencia de DispatcherQueue y componentes de GUI.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        lock (_lockObject)
        {
            if (_initialized)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine("[TestInit] Test context initialized (using TestPreferencesService)");
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TestInit] Error: {ex.Message}");
                _initialized = true;
            }
        }
    }
}
