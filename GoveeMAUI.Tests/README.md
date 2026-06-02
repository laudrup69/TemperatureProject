# GoveeMAUI Unit Tests

Proyecto de pruebas unitarias para la solución GoveeMAUI usando **xUnit**, **Moq** y **FluentAssertions**.

## Estructura

```
GoveeMAUI.Tests/
├── Services/
│   ├── GoveeServiceTests.cs          # Tests para lectura de sensores Govee
│   ├── MerossServiceTests.cs         # Tests para control de enchufes Meross
│   └── MonitorServiceTests.cs        # Tests para orquestación y lógica de monitoreo
├── ViewModels/
│   ├── MainViewModelTests.cs         # Tests para MainViewModel (UI logic)
│   └── SettingsViewModelTests.cs     # Tests para SettingsViewModel
├── Usings.cs                         # Global usings (xUnit, Moq, FluentAssertions)
└── GoveeMAUI.Tests.csproj           # Configuración del proyecto de tests
```

## Cobertura de Tests

### GoveeServiceTests (7 tests)
- ✅ Constructor initialization
- ✅ Device list retrieval with valid API key
- ✅ Missing API key error handling
- ✅ Temperature parsing (múltiples formatos)
- ✅ Fahrenheit to Celsius conversion
- ✅ Humidity parsing
- ✅ Offline device status handling

### MerossServiceTests (5 tests)
- ✅ Constructor initialization
- ✅ Missing credentials error handling
- ✅ Missing device name error handling
- ✅ OnLog event raising
- ✅ Connection state validation

### MonitorServiceTests (8 tests)
- ✅ Constructor initialization
- ✅ Service startup and status
- ✅ Service shutdown
- ✅ Auto-discovery of Govee devices
- ✅ Error handling when no devices found
- ✅ Manual plug control
- ✅ Hysteresis logic for temperature thresholds
- ✅ Graceful handling of Meross connection failures

### MainViewModelTests (15 tests)
- ✅ Property initialization
- ✅ Threshold loading from preferences
- ✅ Monitor service event hooking
- ✅ Toggle monitoring (manual ↔ monitoring mode)
- ✅ API key validation before starting monitoring
- ✅ Threshold persistence
- ✅ Manual plug toggling
- ✅ Temperature/humidity formatting
- ✅ Plug state updates
- ✅ Log message handling and truncation
- ✅ Error status handling
- ✅ Button state management

### SettingsViewModelTests (14 tests)
- ✅ Settings loading from preferences
- ✅ Default values initialization
- ✅ Settings persistence
- ✅ Device detection with valid API key
- ✅ Device detection error handling
- ✅ Property observability (INotifyPropertyChanged)
- ✅ API key validation
- ✅ Threshold range validation
- ✅ Interval configuration
- ✅ Meross credentials management

## Ejecución de Tests

### 1. Desde Visual Studio
```
Test → Run All Tests (Ctrl+R, A)
```

### 2. Desde Command Line
```powershell
# Ejecutar todos los tests
dotnet test

# Ejecutar con verbosidad detallada
dotnet test -v d

# Ejecutar solo tests de un archivo específico
dotnet test --filter FullyQualifiedName~GoveeServiceTests

# Ejecutar con cobertura de código (requiere OpenCover)
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

### 3. Watch Mode (Ejecutar automáticamente al cambiar código)
```powershell
dotnet watch test
```

## Convenciones de Testing

### Naming
- `{ClassUnderTest}Tests` → clase de prueba
- `{Method}_{Scenario}_{ExpectedBehavior}` → nombre de test

### Arrange-Act-Assert (AAA)
```csharp
[Fact]
public async Task GetDevicesAsync_WithValidApiKey_ShouldReturnDeviceList()
{
    // Arrange - Setup inicial
    Preferences.Set(SettingsKeys.GoveeApiKey, "test-api-key");
    var mockHttpMessageHandler = new MockHttpMessageHandler(responseContent);
    // ...

    // Act - Ejecutar método
    var result = await _sut.GetDevicesAsync();

    // Assert - Verificar resultados
    result.Should().NotBeEmpty();
}
```

### Mocking
```csharp
// Mock dependencies
var mockService = new Mock<IGoveeService>();
mockService
    .Setup(s => s.GetDevicesAsync())
    .ReturnsAsync(devices);

// Verify calls
mockService.Verify(s => s.GetDevicesAsync(), Times.Once);
```

## Bibliotecas Utilizadas

| Librería | Versión | Propósito |
|----------|---------|----------|
| xunit | 2.7.0 | Framework de testing |
| Moq | 4.20.70 | Mocking de dependencias |
| FluentAssertions | 6.12.0 | Assertions más legibles |
| Microsoft.NET.Test.Sdk | 17.9.0 | SDK para tests .NET |

## Próximas Mejoras

- [ ] Agregar tests de integración para flujos completos
- [ ] Aumentar cobertura a >80%
- [ ] Agregar tests de stress para Meross MQTT reconnection
- [ ] Tests end-to-end (E2E) para validar completa application flow
- [ ] Performance benchmarks para lectura de sensores
- [ ] Tests parametrizados adicionales para edge cases

## Troubleshooting

### "Tests no aparecen en Test Explorer"
```powershell
# Rebuild project
dotnet clean
dotnet build
```

### "Falta IHttpClientFactory en Mocks"
Los mocks requieren `null!` forcibly:
```csharp
var mock = new Mock<GoveeService>(null!);
```

### "Preferences.Get retorna valores de tests previos"
Limpiar preferences entre tests:
```csharp
[Fact]
public void Test()
{
    Preferences.Remove(SettingsKeys.GoveeApiKey);
    // ... test
}
```

## Recursos Útiles

- [xUnit Documentation](https://xunit.net/)
- [Moq Documentation](https://github.com/moq/moq4/wiki/Quickstart)
- [FluentAssertions Docs](https://fluentassertions.com/)
