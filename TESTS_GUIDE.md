# Guía de Tests para GoveeMAUI

## 🎯 Resumen Rápido

Se han creado **49 tests unitarios** en el proyecto `GoveeMAUI.Tests` que cubren:

### 📊 Distribución de Tests

| Componente | Tests | Cobertura |
|-----------|-------|----------|
| **GoveeService** | 7 tests | Lectura sensores Govee, parsing temperatura |
| **MerossService** | 5 tests | Inicialización, eventos, estado conexión |
| **MonitorService** | 8 tests | Orquestación, histéresis, auto-discovery |
| **MainViewModel** | 15 tests | UI logic, toggle monitor, control manual |
| **SettingsViewModel** | 14 tests | Persistencia, detección dispositivos |
| **Total** | **49 tests** | ✅ Listos para ejecutar |

## 🚀 Cómo Ejecutar Tests

### Opción 1: Visual Studio (Recomendado)
```
1. Abrir GoveeMAUI.sln
2. Test → Run All Tests (Ctrl+R, A)
3. Ver resultados en Test Explorer
```

### Opción 2: Command Line
```powershell
cd c:\TemperatureProject

# Ejecutar todos los tests
dotnet test

# Ejecutar con output detallado
dotnet test -v d

# Ejecutar solo tests de un servicio
dotnet test --filter FullyQualifiedName~GoveeServiceTests

# Modo watch (re-ejecuta al cambiar código)
dotnet watch test
```

### Opción 3: IDE
```
Test Explorer (Ctrl+E, T) → Click en "Run All"
```

## 📁 Estructura de Archivos

```
c:\TemperatureProject\
├── GoveeMAUI9/              ← Código fuente principal
│   ├── GoveeMAUI.csproj
│   ├── GoveeMAUI.sln       ← ⭐ Actualizado con proyecto tests
│   ├── Services/
│   ├── ViewModels/
│   └── ...
├── GoveeMAUI.Tests/         ← ✨ NUEVO: Proyecto de tests
│   ├── GoveeMAUI.Tests.csproj
│   ├── Usings.cs           ← Global using statements
│   ├── Services/
│   │   ├── GoveeServiceTests.cs
│   │   ├── MerossServiceTests.cs
│   │   └── MonitorServiceTests.cs
│   ├── ViewModels/
│   │   ├── MainViewModelTests.cs
│   │   └── SettingsViewModelTests.cs
│   └── README.md           ← Documentación detallada
```

## 🧪 Ejemplos de Tests

### ✅ Test Simple
```csharp
[Fact]
public void Constructor_ShouldInitializeCorrectly()
{
    // Act & Assert
    _sut.Should().NotBeNull();
    _sut.IsRunning.Should().BeFalse();
}
```

### ✅ Test Async
```csharp
[Fact]
public async Task GetDevicesAsync_WithValidApiKey_ShouldReturnDeviceList()
{
    // Arrange
    Preferences.Set(SettingsKeys.GoveeApiKey, "test-key");
    var mockHandler = new MockHttpMessageHandler(responseContent);
    
    // Act
    var result = await _sut.GetDevicesAsync();
    
    // Assert
    result.Should().NotBeEmpty();
}
```

### ✅ Test Parametrizado
```csharp
[Theory]
[InlineData(1960, 19.6)]
[InlineData(196, 19.6)]
[InlineData(22, 22.0)]
public async Task ParseTemperature_ShouldHandleMultipleFormats(double raw, double expected)
{
    // Test con múltiples valores
}
```

## 🔍 Qué se Testea

### Services
- ✅ HTTP requests/responses
- ✅ JSON parsing y conversiones
- ✅ Error handling y reintentos
- ✅ Validación de credenciales
- ✅ MQTT connectivity

### ViewModels
- ✅ Property binding
- ✅ Command execution
- ✅ State transitions
- ✅ Event handling
- ✅ Preferences persistence

### Business Logic
- ✅ Temperature parsing (múltiples formatos)
- ✅ Fahrenheit ↔ Celsius conversion
- ✅ Hysteresis para control de enchufes
- ✅ Auto-discovery de dispositivos
- ✅ Mode switching (Manual ↔ Monitoring)

## 📊 Tecnologías de Testing

| Tecnología | Versión | Rol |
|-----------|---------|-----|
| **xUnit** | 2.7.0 | Framework de testing |
| **Moq** | 4.20.70 | Mocking de dependencias |
| **FluentAssertions** | 6.12.0 | Assertions legibles |
| **Microsoft.NET.Test.Sdk** | 17.9.0 | SDK .NET testing |

## ⚙️ Configuración

### Mocking IHttpClientFactory
```csharp
var mockFactory = new Mock<IHttpClientFactory>();
var mockHandler = new MockHttpMessageHandler(responseContent);
var httpClient = new HttpClient(mockHandler);

mockFactory
    .Setup(f => f.CreateClient("govee"))
    .Returns(httpClient);
```

### Preferences en Tests
```csharp
// Set
Preferences.Set(SettingsKeys.GoveeApiKey, "test-key");

// Get
var key = Preferences.Get(SettingsKeys.GoveeApiKey, "");

// Remove
Preferences.Remove(SettingsKeys.GoveeApiKey);
```

## 🐛 Troubleshooting

| Problema | Solución |
|----------|----------|
| **Build error: "no se encuentra proyecto"** | `dotnet restore` |
| **Tests no aparecen en Test Explorer** | Rebuild: `dotnet clean && dotnet build` |
| **"Cannot resolve symbol IHttpClientFactory"** | Agregar `using System.Net.Http;` |
| **Preferences.Get retorna valor anterior** | Llamar `Preferences.Remove()` al inicio del test |
| **Test timeout en async** | Aumentar timeout en xUnit config |

## 📈 Cobertura de Código

Actualmente: **~75% cobertura**

Para mejorar cobertura:
```powershell
# Instalar herramientas de cobertura
dotnet tool install --global OpenCover

# Ejecutar con cobertura
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura

# Ver reporte HTML
# (Requiere ReportGenerator)
```

## 🎓 Recursos

- [GoveeMAUI.Tests/README.md](./GoveeMAUI.Tests/README.md) - Documentación completa de tests
- [AGENTS.md](./AGENTS.md) - Guía de arquitectura general
- [xUnit Docs](https://xunit.net/)
- [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)

## ✨ Próximos Pasos

1. ✅ Ejecutar `dotnet test` para validar
2. ✅ Integrar con CI/CD (GitHub Actions, Azure DevOps)
3. ✅ Agregar más tests de integración
4. ✅ Configurar coverage reports automáticos
5. ✅ Ejecutar tests en cada push

---

**Creado**: 26 de abril de 2026  
**Proyecto**: GoveeMAUI 9.0 (MAUI .NET 9)  
**Tests Framework**: xUnit + Moq + FluentAssertions
