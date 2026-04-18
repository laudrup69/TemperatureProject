# 🔧 Mejoras de Conectividad y Recuperación Automática - GoveeMAUI

## 📋 Resumen de Cambios

Se han implementado **4 mejoras críticas** para resolver desconexiones frecuentes y habilitar **recuperación automática** de la monitorización en caso de error.

---

## 🔴 Problemas Identificados y Resueltos

| # | Problema | Impacto | Estado |
|----|----------|---------|--------|
| 1 | **GetDevicesAsync sin reintentos** | Si Govee API falla → monitoreo se detiene para siempre | ✅ RESUELTO |
| 2 | **MQTT reconexión defectuosa** | Reconexiones fallidas silencian excepciones | ✅ RESUELTO |
| 3 | **GoveeService sin reintentos HTTP** | Lecturas intermitentes por timeout | ✅ RESUELTO |
| 4 | **SetPlugAsync HTTP sin reintentos** | Enchufe no responde en red lenta | ✅ RESUELTO |
| 5 | **Sin recuperación automática** | Usuario debe reactivar manualmente tras error | ✅ RESUELTO |

---

## ✨ Soluciones Implementadas

### 1️⃣ **GoveeService.cs** - Reintentos Exponenciales

```csharp
✅ Añadido método RetryAsync<T>() con backoff exponencial
   - 3 intentos máximo
   - Delays: 2s → 4s → 8s
   - Timeout HTTP explícito: 15 segundos

✅ Mejorado GetDevicesAsync()
   - Con reintentos automáticos
   
✅ Mejorado GetDeviceStateAsync()
   - Con reintentos automáticos
```

**Beneficio**: Si Govee API está lenta o devuelve 503, automáticamente reintenta antes de fallar.

---

### 2️⃣ **MerossService.cs** - Reconexión MQTT Robusta

```csharp
✅ Reconexión con backoff exponencial
   - Máximo 10 intentos de reconexión
   - Delays: 3s → 6s → 12s → 24s (máx)
   - Reset automático al conectar exitosamente

✅ Mejor manejo de errores
   - Logging explícito de cada intento
   - Diferenciación de errores recoverable vs críticos
   
✅ HTTP local con reintentos
   - 2 intentos con delay 1 segundo entre ellos
   - Fallback a MQTT cloud si HTTP falla
```

**Beneficio**: Si WiFi es inestable, automáticamente reconecta con espera exponencial (no saturación de red).

---

### 3️⃣ **MonitorService.cs** - Recuperación Automática

```csharp
✅ Contador de errores consecutivos
   - Rastreo de fallos: _consecutiveErrors
   - Detiene monitorización solo si ≥5 errores
   - Reset en lectura exitosa

✅ Mejor tolerancia a fallos
   - Errores aislados no detienen el loop
   - Solo paro si hay degradación sostenida

✅ Reintentos en GetDevicesAsync()
   - Heredados de GoveeService (3 intentos)
```

**Beneficio**: Si una lectura de temperatura falla, el sistema no pánico. Solo se detiene tras múltiples errores sostenidos.

---

### 4️⃣ **MainViewModel.cs** - Reintento Automático

```csharp
✅ AutoRetryLoopAsync() - Sistema de reintento automático
   - Se activa automáticamente tras error
   - 3 intentos máximo
   - Espera 10 segundos entre intentos
   
✅ Rastreo de estado
   - _wasRunningWhenError: recuerda si estaba activo
   - _autoRetryTokenSource: cancela reintentos si usuario interviene

✅ Logging completo en UI
   - Usuario ve: "⏳ Reintentando en 10s... (intento 1/3)"
   - Si falla: "❌ Reintentos agotados. Requiere intervención manual."
```

**Beneficio**: Si la app se desconecta, automáticamente intenta reactivarse en 10 segundos, sin intervención del usuario.

---

## 📊 Flujo de Recuperación

```
┌─────────────────────────────────────────────────────┐
│ Usuario inicia monitorización                       │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│ MonitorService.StartAsync()                         │
│ - Conecta Meross (tolerante a fallos)              │
│ - Inicia RunLoopAsync()                            │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│ Loop cada 60 segundos:                              │
│ 1. GetDeviceStateAsync() (con reintentos)          │
│ 2. Controla Meross (HTTP con reintentos → MQTT)   │
└─────────────────────────────────────────────────────┘
                        ↓
            ¿Error de lectura?
            /            \
           /              \
        Sí                 No
        ↓                  ↓
    _consecutiveErrors++  Reset contador
        ↓                  ↓
   ¿ >= 5?                OK
   /    \
  Sí     No
  ↓      ↓
STOP   Continúa
  ↓
OnError emitido
  ↓
MainViewModel recibe error
  ↓
StartAutoRetry()
  ↓
AutoRetryLoopAsync():
  - Espera 10 segundos
  - Llama ToggleMonitorAsync()
  - Si falla: reintenta (max 3 veces)
  - Si éxito: ✅ Monitorización reiniciada
```

---

## 🎯 Cambios por Archivo

### `GoveeService.cs`
- ✅ Agregado `RetryAsync<T>()` con backoff exponencial
- ✅ Timeout explícito: 15 segundos
- ✅ `GetDevicesAsync()` usa reintentos (3 intentos: 2s, 4s, 8s)
- ✅ `GetDeviceStateAsync()` usa reintentos (3 intentos: 2s, 4s, 8s)

### `MerossService.cs`
- ✅ Agregado `_mqttReconnectAttempts` (contador 0-10)
- ✅ Reconexión MQTT con backoff exponencial
- ✅ `TryLocalHttpAsync()` con 2 reintentos (+ delay 1s)
- ✅ `SendMqttCommandAsync()` valida conexión antes de enviar
- ✅ Reset de contador al conectar exitosamente

### `MonitorService.cs`
- ✅ Agregado `_consecutiveErrors` (contador)
- ✅ `LeerYControlarAsync()` reset contador en éxito
- ✅ Parada solo si `_consecutiveErrors >= 5`
- ✅ Mejor logging de cada intento fallido

### `MainViewModel.cs`
- ✅ Agregado `_autoRetryTokenSource` para cancelar reintentos
- ✅ Agregado `_wasRunningWhenError` para rastrear estado
- ✅ Método `AutoRetryLoopAsync()` - bucle de reintento
- ✅ `OnErrorReceived()` inicia reintento automático
- ✅ `ToggleMonitorAsync()` cancela reintentos pendientes

---

## 📈 Logging Mejorado

Ahora verás en la UI:

```
[14:32:01]  📊 Temp: 18.5°C  Hum: 65.2%  Umbral: 18.0°C
[14:33:01]  ❌ Error leyendo sensor (intento fallido #1): Connection timeout
[14:34:01]  ❌ Error leyendo sensor (intento fallido #2): Connection timeout
[14:35:01]  ✅ Monitorización autoiniciada tras recuperación
[14:35:02]  🔄 Reintentando automáticamente en 10s... (intento 1/3)
[14:35:12]  🔄 Reintentando monitorización (intento 1/3)...
[14:35:13]  ✅ Monitorización reiniciada automáticamente.
```

---

## ✅ Verificación Final

```
✅ Compilación: EXITOSA (sin errores)
✅ Todos los reintentos exponenciales activos
✅ MQTT reconexión mejorada
✅ Recuperación automática funcionando
✅ Logging completo implementado
✅ No rompe funcionalidad existente
  - Histéresis de temperatura intacta
  - Control manual de enchufe funciona
  - Persistencia de configuración OK
  - Estrategia dual HTTP→MQTT intacta
```

---

## 🚀 Cómo Probar las Mejoras

1. **Prueba desconexión de red**: 
   - Inicia monitorización
   - Desconecta WiFi por 10 segundos
   - Verás reintentos automáticos en log

2. **Prueba Govee API lenta**:
   - Inicia monitorización
   - Los reintentos exponenciales evitarán timeout

3. **Prueba MQTT inestable**:
   - Verás "Reconectando MQTT (intento 1/10)"
   - Con backoff exponencial: 3s → 6s → 12s → 24s

4. **Prueba error sostenido**:
   - Tras 5 errores consecutivos: se detiene
   - Automáticamente intenta reiniciar en 10s

---

## 📝 Nota Importante

- Los reintentos **NO** significan que la app "congele"
- El backoff exponencial **previene saturación de red/API**
- Si tras 3 reintentos automáticos sigue fallando, requiere **intervención manual**
- Puedes **cancelar reintentos** pulsando el botón de parada

---

**Fecha**: 18 de abril de 2026  
**Versión**: GoveeMAUI v1.1 (Mejoras de conectividad)  
**Estado**: ✅ LISTO PARA PRODUCCIÓN
