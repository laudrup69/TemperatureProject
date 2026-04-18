# Govee Temperature Control - .NET 9 MAUI

App multiplataforma C# (.NET 9 MAUI) que monitoriza la temperatura con un
sensor Govee y activa/desactiva automaticamente un enchufe WiFi Meross.

Funciona en: Android y Windows — mismo codigo, misma app

---

## Requisitos

- Visual Studio 2022 v17.12 o superior
- Workload ".NET Multi-platform App UI development" instalado
  Visual Studio -> Tools -> Get Tools and Features -> marcar .NET MAUI
- .NET 9 SDK instalado

---

## Primeros pasos

1. Descomprime el ZIP y abre GoveeMAUI.csproj con Visual Studio
2. Haz click derecho en el proyecto -> Restore NuGet Packages
3. Selecciona plataforma destino en la barra superior:
   - "Windows Machine" para ejecutar en PC
   - Tu movil Android conectado por USB (activa Depuracion USB en el movil)
4. Pulsa F5

---

## Configuracion en la app

Pulsa "Ajustes" (arriba a la derecha) y rellena:

  Govee API Key     -> La que recibiste por email de Govee
  Email Meross      -> Email de tu cuenta en la app Meross
  Contrasena Meross -> Contrasena de la app Meross
  Umbral (C)        -> Temperatura minima antes de encender el enchufe
  Intervalo (seg)   -> Cada cuantos segundos comprobar la temperatura

Pulsa "Detectar dispositivos automaticamente" para que la app encuentre
tu sensor Govee sin copiar IDs a mano.

Pulsa "Guardar ajustes".

Vuelve a la pantalla principal y pulsa "Iniciar monitorizacion".

---

## Servidor europeo Meross

Si en Espana falla la conexion con Meross, edita Services/MerossService.cs:

  // Comenta esta linea:
  private const string CloudBaseUrl = "https://iot.meross.com";

  // Descomenta esta:
  // private const string CloudBaseUrl = "https://iotx-eu.meross.com";

---

## Estructura

  GoveeMAUI/
  +-- Models/Models.cs               Modelos de datos
  +-- Services/
  |   +-- GoveeService.cs            API REST de Govee
  |   +-- MerossService.cs           MQTT de Meross
  |   +-- MonitorService.cs          Bucle de monitorizacion
  +-- ViewModels/
  |   +-- MainViewModel.cs           Logica del dashboard
  |   +-- SettingsViewModel.cs       Logica de ajustes
  +-- Views/
  |   +-- MainPage.xaml              Pantalla principal
  |   +-- SettingsPage.xaml          Pantalla de ajustes
  +-- Platforms/
  |   +-- Android/                   Archivos especificos Android
  |   +-- Windows/                   Archivos especificos Windows
  +-- MauiProgram.cs                 Arranque e inyeccion de dependencias
