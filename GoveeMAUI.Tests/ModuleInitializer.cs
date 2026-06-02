using System.Runtime.CompilerServices;

namespace GoveeMAUI.Tests;

/// <summary>
/// Module initializer que se ejecuta antes de cualquier código de test.
/// Desactiva componentes MAUI que requieren UI y establece Preferences en modo test.
/// </summary>
internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Intentar desactivar componentes de MAUI que requieren UI
        try
        {
            // Desactivar validaciones de UI de MAUI si es posible
            AppContext.SetSwitch("System.Runtime.InteropServices.DoNotMarshalOutParams", true);
        }
        catch { }
        
        // Inicializar el TestMauiInitializer
        TestMauiInitializer.Initialize();
    }
}
