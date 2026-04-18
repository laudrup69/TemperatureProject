# Instrucciones para Agentes Claude - GoveeMAUI

Un proyecto .NET 9 MAUI multiplataforma (Android + Windows) que monitoriza temperatura Govee y controla enchufes Meross automáticamente.

## Build & Ejecución

**Compilar y ejecutar localmente**:
- **Windows**: VS 2022 → selector plataforma "Windows Machine" → F5
- **Android**: Conecta teléfono (USB Debug ON) → selecciona en dropdown → F5
- **Restaurar paquetes**: Click derecho proyecto → "Restore NuGet Packages"

**Release Android**: Configuration → Release → genera APK firmado en `bin/Release/net9.0-android/`

⚠️ **Keystore Android**: Requiere `C:\keys\govee.keystore` para builds release. Ver [GoveeMAUI.csproj](GoveeMAUI.csproj) línea donde se configura.

## Arquitectura & Patrones

### MVVM con Dependency Injection (Singletons)

Toda inyección de dependencias en [MauiProgram.cs](MauiProgram.cs):
- **Services**: GoveeService, MerossService, MonitorService (Singletons)
- **ViewModels**: MainViewModel, SettingsViewModel (Singletons)
- **Views**: MainPage, SettingsPage (Singletons)

Los ViewModels reciben servicios vía constructor:
```csharp
public class MainViewModel(MonitorService monitor) { }
```

### Tres Servicios Principales

| Servicio | Protocolo | Rol |
|----------|-----------|-----|
| [GoveeService](Services/GoveeService.cs) | REST API v2 | Lee temperatura/humedad del sensor |
| [MerossService](Services/MerossService.cs) | MQTT + REST | Controla enchufe WiFi inteligente |
| [MonitorService](Services/MonitorService.cs) | Async loop | Orquesta lectura cíclica + lógica umbral |

**Flujo monitoreo**: `MonitorService.StartAsync()` → lee Govee cada intervalo → aplica histéresis → controla Meross vía MQTT

## Convenciones de Código

**Nomenclatura**:
- Views: `{Page}.xaml` + `{Page}.xaml.cs` (ej: `MainPage.xaml`)
- ViewModels: `{Feature}ViewModel.cs` (ej: `MainViewModel.cs`)
- Services: `{Domain}Service.cs` (ej: `GoveeService.cs`)

**Modelos**: Todo en [Models/Models.cs](Models/Models.cs) — DTOs para Govee API v2, Meross MQTT, y configuración persistente via `Preferences` API.

**Binding MVVM**: Usa MVVM Toolkit — `[ObservableProperty]` (genera INotifyPropertyChanged) y `[RelayCommand]` para async commands.

**UI Styling**: Dark theme (fondo #1E1E2E, cards #252538) definido en [Resources/Styles/Styles.xaml](Resources/Styles/Styles.xaml).

## Gotchas & Configuración Requerida

### Configuración Crítica (en Ajustes de la app)

| Campo | Problema Común | Solución |
|-------|----------------|----------|
| Govee API Key | Falta → crash al leer sensor | Obtener de email Govee oficial |
| Email Meross | Credenciales inválidas | Usar email de app Meross (no web) |
| Contraseña Meross | Fail de login | Usar contraseña de app Meross |
| Servidor Meross | Timeout en Europa | Editar [MerossService.cs](Services/MerossService.cs#L11): descomentar `iotx-eu.meross.com` |

### Issues Técnicos Frecuentes

1. **Sensor offline**: `ReadSensorReading()` valida rango -40 a 80°C, ignora nulos.
2. **Histéresis**: ON si T < umbral; OFF si T ≥ (umbral + 1°C) → previene oscilaciones.
3. **MQTT desconexiones**: Reconnect automático cada 3s + resubscripción.
4. **Control local primero**: Intenta IP interna HTTP → fallback a MQTT cloud.
5. **Certificados**: TLS ignora errores de certificado (verificar si es intencional).

### Logs & Debugging

- [MonitorService](Services/MonitorService.cs) emite eventos: `OnLogMessage`, `OnError`, `OnReadingUpdated`
- Logs en UI vía `MainViewModel._log` (concatenados)
- Enable debug en [MauiProgram.cs](MauiProgram.cs): `builder.Logging.AddDebug()`

## Código Específico de Plataforma

| Plataforma | Ubicación | Permisos/Manifests |
|-----------|-----------|-------------------|
| Android | [Platforms/Android/](Platforms/Android/) | INTERNET + ACCESS_NETWORK_STATE en [AndroidManifest.xml](Platforms/Android/AndroidManifest.xml) |
| Windows | [Platforms/Windows/](Platforms/Windows/) | Package.appxmanifest (NO Store deployment) |

Ambas comparten código — sin conditional compilation activa. Al agregar permisos/funcionalidades, editar ambos manifests.

## Archivos Clave que Ejemplifican Patrones

- **[MauiProgram.cs](MauiProgram.cs)**: DI setup, singleton scopes
- **[MainViewModel.cs](ViewModels/MainViewModel.cs)**: @ObservableProperty, @RelayCommand, event handlers
- **[GoveeService.cs](Services/GoveeService.cs)**: REST deserialization, conversiones (°F→°C)
- **[MerossService.cs](Services/MerossService.cs)**: HMAC-MD5 signing, dual strategy (local HTTP + cloud MQTT)
- **[MonitorService.cs](Services/MonitorService.cs)**: Async loop con histéresis, event aggregation
- **[Models.cs](Models/Models.cs)**: DTOs, JSON serialization, type-safe settings keys
- **[MainPage.xaml](Views/MainPage.xaml)**: Dark theme, data triggers, binding converters

## Estructura del Repositorio

```
GoveeMAUI/
├── Models/Models.cs              → Todas las clases de datos
├── Services/                     → Tres servicios (REST, MQTT, orquestación)
├── ViewModels/                   → Lógica con @ObservableProperty
├── Views/                        → XAML + code-behind
├── Platforms/{Android,Windows}/  → Código específico de plataforma
├── Resources/                    → Estilos, iconos, splash
├── MauiProgram.cs               → DI + configuración startup
└── GoveeMAUI.csproj             → Configuración build, targets, keystore

Ver [README.md](README.md) para primeros pasos e instalación.
```

## Tips para Agentes

1. **Modificar features**: Los servicios son singletons → cambios persisten automáticamente.
2. **UI updates**: Via `OnPropertyChanged()` + events en ViewModels (no acceso directo a Views).
3. **Persistencia**: `Preferences` API maneja configuración — no requiere BD.
4. **Agregar permisos**: Editar manifests en `Platforms/Android/` y `Platforms/Windows/`.
5. **Mock testing**: Simula `GoveeService.GetDeviceStateAsync()` con temperaturas mock; verifica histéresis en `MonitorService`.

## Troubleshooting

- **Errores Govee API**: Status 429 (rate limit), falta API Key → revisar [GoveeService.cs](Services/GoveeService.cs#L1)
- **Meross ApiStatus != 0**: Error en respuesta MQTT/HTTP → revisar credenciales y conexión
- **MQTT timeout**: Servidor incorrecto (EU vs US) → editar URL en [MerossService.cs](Services/MerossService.cs#L11)
- **Logs vacíos**: Enable debug logging en [MauiProgram.cs](MauiProgram.cs)
