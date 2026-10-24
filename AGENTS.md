# Instrucciones para Agentes Claude - GoveeMAUI

Un proyecto .NET 9 MAUI multiplataforma (Android + Windows) que monitoriza temperatura Govee y controla enchufes Meross automáticamente.

## Build & Ejecución

**Compilar y ejecutar localmente**:
- **Windows**: VS 2022 → selector plataforma "Windows Machine" → F5
- **Android**: Conecta teléfono (USB Debug ON) → selecciona en dropdown → F5
- **Restaurar paquetes**: Click derecho proyecto → "Restore NuGet Packages"

**Release Android**: Configuration → Release → genera APK firmado en `bin/Release/net9.0-android/`

**CI/CD** (`.github/workflows/`):
- `ci.yml`: en cada PR a `main` (y push a `main`) ejecuta los tests y compila Android. Es la puerta de entrada a `main`.
- `release.yml`: al subir una etiqueta `vX.Y.Z` (`git tag v2.2.0 && git push TemperatureGovee v2.2.0`) compila el APK firmado y crea la release de GitHub con el APK adjunto. No repite los tests. La versión sale de la etiqueta (versionCode = mayor·10000 + menor·100 + parche); la del `.csproj` sólo afecta a compilaciones locales. Secretos necesarios: `GOVEE_KEYSTORE_BASE64`, `GOVEE_SIGNING_ALIAS`, `GOVEE_SIGNING_KEYPASS`, `GOVEE_SIGNING_STOREPASS`.

⚠️ **Keystore Android**: Requiere `C:\keys\govee.keystore` para builds release. Ver [GoveeMAUI.csproj](GoveeMAUI.csproj) línea donde se configura.

## Arquitectura & Patrones

### MVVM con Dependency Injection (Singletons)

Toda inyección de dependencias en [MauiProgram.cs](MauiProgram.cs):
- **Services**: GoveeService, MerossService, MonitorService (Singletons)
- **ViewModels**: MainViewModel, SettingsViewModel (Singletons)
- **Views**: MainPage, SettingsPage (Transient: Android recrea la ventana al reabrir la app, ver `App.CreateWindow`)

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

**Flujo monitoreo**: `MonitorService.StartAsync()` → lee Govee cada intervalo → aplica histéresis → controla Meross (HTTP local o MQTT, con ACK)

**Monitorización en segundo plano**: la monitorización no depende de la UI. `StartAsync()` guarda `SettingsKeys.MonitoringEnabled = 1` y arranca `IKeepAliveService`; `Stop()` lo pone a 0. En Android, `MonitorForegroundService` (START_STICKY) y `BootReceiver` la restauran si el sistema mata el proceso, al reiniciar o al actualizar la app; `MainViewModel` refleja el estado al abrirse. Los errores de red no detienen el bucle (reintenta cada intervalo); sólo se para si Govee no tiene ningún sensor. **Apagado de seguridad**: sin lecturas válidas (sensor online) durante `SettingsKeys.FailsafeMinutes` (por defecto 30, 0 = desactivado) se apaga el enchufe hasta que vuelvan; el plazo cuenta desde el arranque. El enchufe controla una caldera. **Cambio manual externo**: si el enchufe avisa por MQTT (PUSH ToggleX, topic `/app/{userId}/subscribe`) de un cambio que no ha provocado la app (p. ej. desde la app de Meross), `MonitorService` llama a `Stop()` y emite `OnStoppedByExternalChange`; los ecos de órdenes propias se ignoran (`_commandInFlight`).

## Convenciones de Código

**Nomenclatura**:
- Views: `{Page}.xaml` + `{Page}.xaml.cs` (ej: `MainPage.xaml`)
- ViewModels: `{Feature}ViewModel.cs` (ej: `MainViewModel.cs`)
- Services: `{Domain}Service.cs` (ej: `GoveeService.cs`)

**Modelos**: Todo en [Models/Models.cs](Models/Models.cs) — DTOs para Govee API v2, Meross MQTT, y configuración persistente via `Preferences` API.

**Binding MVVM**: Usa MVVM Toolkit — `[ObservableProperty]` (genera INotifyPropertyChanged) y `[RelayCommand]` para async commands.

**UI Styling**: Tema claro de inspiración iOS (fondo #F2F2F7, tarjetas blancas, acento naranja #FF6B2C). Paleta en [Resources/Styles/Colors.xaml](GoveeMAUI9/Resources/Styles/Colors.xaml) y estilos (`Card`, `Field`, `PrimaryButton`...) en [Styles.xaml](GoveeMAUI9/Resources/Styles/Styles.xaml), ambos fusionados en `App.xaml`; usar `{StaticResource ...}` en vez de colores literales. Los colores de estado de `MainViewModel` repiten esos tonos. Icono adaptativo: `appicon.svg` (fondo) + `appiconfg.svg` (caldera).

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
2. **Histéresis**: ON si T < umbral; OFF si T ≥ (umbral + margen) → previene oscilaciones. Margen configurable en Ajustes (`SettingsKeys.HysteresisMargin`, por defecto 0,5 °C). El estado del enchufe se lee de Meross al arrancar; si es desconocido o una orden no se confirma, la orden se reenvía en la siguiente lectura.
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

Ambas comparten código. Única compilación condicional: el registro de `IKeepAliveService` en MauiProgram.cs (Android: `Platforms/Android/AndroidKeepAliveService.cs`, `MonitorForegroundService.cs`, `BootReceiver.cs`). Al agregar permisos/funcionalidades, editar ambos manifests.

## Archivos Clave que Ejemplifican Patrones

- **[MauiProgram.cs](MauiProgram.cs)**: DI setup, singleton scopes
- **[MainViewModel.cs](ViewModels/MainViewModel.cs)**: @ObservableProperty, @RelayCommand, event handlers
- **[GoveeService.cs](Services/GoveeService.cs)**: REST deserialization, conversiones (°F→°C)
- **[MerossService.cs](Services/MerossService.cs)**: HMAC-MD5 signing, dual strategy (local HTTP + cloud MQTT)
- **[MonitorService.cs](Services/MonitorService.cs)**: Async loop con histéresis, event aggregation
- **[Models.cs](Models/Models.cs)**: DTOs, JSON serialization, type-safe settings keys
- **[MainPage.xaml](Views/MainPage.xaml)**: Tema claro, data triggers, cabecera propia (sin barra de navegación)

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
