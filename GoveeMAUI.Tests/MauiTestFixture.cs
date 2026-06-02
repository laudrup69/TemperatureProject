namespace GoveeMAUI.Tests;

/// <summary>
/// Fixture para inicializar MAUI antes de todos los tests.
/// Se ejecuta una sola vez por sesión de tests.
/// </summary>
[CollectionDefinition("MAUI Tests Collection")]
public class MauiTestsCollection : ICollectionFixture<MauiTestFixture>
{
    // Esta clase define una colección con el fixture. Los tests que usen
    // [Collection("MAUI Tests Collection")] compartirán la misma instancia del fixture.
}

/// <summary>
/// Fixture que inicializa MAUI para tests.
/// Asegura que Preferences está disponible en todos los tests.
/// </summary>
public class MauiTestFixture : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        // Inicializar Preferences para tests
        TestMauiInitializer.Initialize();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
