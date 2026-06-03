# 📱 Instalación del APK - GoveeMAUI v2.0.0

## 📦 Archivo APK
- **Nombre**: `GoveeMAUI_v2.0.0.apk`
- **Tamaño**: 27.8 MB
- **Versión**: 2.0.0
- **Formato**: APK firmado (listo para instalación)

## 🚀 Instalación en Android

### Opción 1: Instalación Manual USB
1. **Conecta el teléfono** con cable USB a la computadora
2. **Activa USB Debugging** en el teléfono:
   - Ve a Ajustes → Información del dispositivo
   - Toca "Número de compilación" 7 veces (para activar modo desarrollador)
   - Regresa a Ajustes → Opciones de desarrollador
   - Activa "Depuración USB"
3. **Copia el APK al teléfono** o usa ADB:
   ```bash
   adb install GoveeMAUI_v2.0.0.apk
   ```
4. **Espera** a que se complete la instalación
5. **Abre la app**: "Govee Temp Control"

### Opción 2: Instalación por Archivo (Sin USB)
1. **Copia el archivo APK** al teléfono (por email, cloud, WhatsApp, etc.)
2. **Abre el Explorador de Archivos** en el teléfono
3. **Navega a la carpeta** donde descargaste el APK
4. **Toca el archivo** `GoveeMAUI_v2.0.0.apk`
5. **Permite la instalación** desde "Fuentes desconocidas" si aparece el aviso:
   - Ajustes → Aplicaciones → Fuentes desconocidas → Permitir
6. **Instala la app**

## ⚙️ Configuración Inicial

### Primera Vez
1. **Abre Govee Temp Control**
2. **Ve a Ajustes** (⚙️ icono)
3. **Configura credenciales**:
   - **Govee API Key**: Obtén de email oficial de Govee
   - **Meross Email**: Email registrado en app Meross
   - **Meross Contraseña**: Contraseña de app Meross (no web)
   - **Dispositivo Meross**: Nombre exacto del enchufe WiFi
4. **Establece umbral de temperatura**: Ej: 18°C
5. **Configura intervalo**: Ej: 60 segundos
6. **Vuelve a Principal** y toca "MONITORIZAR" para iniciar

## 📋 Requisitos
- **Android**: 7.0 o superior (API 21+)
- **Red WiFi**: Conexión a internet estable
- **Dispositivos**:
  - Sensor Govee compatibles (ej: H5054, H5074)
  - Enchufe Meross (ej: MSS110, MSS210)

## 🔑 Permisos Requeridos
La app solicita:
- ✅ **INTERNET** - Para conectarse a APIs
- ✅ **ACCESS_NETWORK_STATE** - Para detectar cambios de red
- ✅ **WAKE_LOCK** - Para mantener monitorización en background
- ✅ **CHANGE_NETWORK_STATE** - Para gestionar conectividad
- ✅ **SCHEDULE_EXACT_ALARM** - Para timers en background

## 🐛 Troubleshooting

### "No se puede instalar el APK"
- Verifica que **Fuentes desconocidas** está permitido
- Intenta desinstalar una versión anterior primero
- Comprueba que hay **mínimo 100 MB libres**

### "Error de conectividad"
- Verifica la **API Key de Govee** (debe ser válida)
- Comprueba **credenciales Meross** (email/contraseña correctos)
- Asegúrate de que **sensor y enchufe están online**
- Reinicia la app

### "Monitorización se detiene al minimizar"
- **Versión 2.0.0** corrige este problema
- La app ahora pausa/reanuda monitorización correctamente
- Comprueba que los permisos WAKE_LOCK están habilitados

### "La app va lenta"
- Reduce el intervalo si está en <10 segundos
- Cierra otras apps
- Reinicia el teléfono

## 📊 Información de Compilación
```
Nombre: GoveeMAUI
Versión: 2.0.0
Código de versión: 2
ID de aplicación: com.personal.goveetempcontrol
Target SDK: Android 35
Min SDK: Android 7.0 (API 21)
Fecha compilación: 03/06/2026
Firmante: Keystore privado
```

## 🔄 Actualizaciones Futuras
Para futuras versiones, reemplaza el APK con la nueva versión y:
1. Desinstala la versión anterior
2. Instala la nueva versión
3. La configuración se preserva automáticamente

## 📞 Soporte
Si encuentras problemas:
1. Verifica los logs en la app (sección Logs de Monitorización)
2. Revisa la consola de debug si compilaste desde VS 2022
3. Asegúrate de tener la última versión del APK

---
**Generado**: 03/06/2026
**Estado**: ✅ Listo para producción
