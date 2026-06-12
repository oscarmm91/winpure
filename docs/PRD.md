# WinPure — Product Requirements Document
**Version:** 1.0  
**Status:** Draft  
**Repositorio:** github.com/[tu-usuario]/winpure  
**Licencia:** MIT (Open Source, gratuito)

---

## 1. Resumen del Producto

**WinPure** es una aplicación de escritorio para Windows 11 que permite a los usuarios limpiar, optimizar y tomar control de su sistema operativo a través de una interfaz moderna tipo Fluent Design. Agrupa tweaks de privacidad, telemetría, bloatware, servicios, rendimiento y UI en un solo lugar, con soporte para presets predefinidos, selección manual tweak-por-tweak, y una función de Restore/Undo siempre disponible.

---

## 2. Objetivos

- Ofrecer una alternativa moderna, segura y de código abierto a herramientas como Chris Titus Tech WinUtil, Sophia Script, CrapFixer y BloatyNosy.
- Ser fácil de usar para usuarios no técnicos, sin sacrificar profundidad para power users.
- Publicarse en GitHub como proyecto open source gratuito, mantenible por la comunidad.
- Nunca romper el sistema: toda acción es reversible.

---

## 3. Stack Tecnológico Recomendado

| Capa | Tecnología |
|---|---|
| Framework UI | WPF (.NET 8) |
| Lenguaje | C# |
| Estilos/Tema | WPF + estilos personalizados Fluent Design |
| Ejecución de tweaks | PowerShell scripts embebidos + llamadas a Registry vía código C# |
| Empaquetado | Ejecutable standalone (.exe), sin instalador requerido |
| Distribución | GitHub Releases (.exe + código fuente) |

---

## 4. Requisitos del Sistema

- Windows 11 (también compatible con Windows 10 22H2+)
- .NET 8 Runtime (o publicar como self-contained para evitar dependencia)
- Ejecutarse como Administrador (la app detecta automáticamente si no tiene permisos elevados y solicita UAC)

---

## 5. Arquitectura General de la Aplicación

```
WinPure.exe
│
├── MainWindow (Shell)
│   ├── Sidebar de navegación (iconos + etiquetas)
│   └── Área de contenido principal
│
├── Páginas / Tabs
│   ├── Dashboard (Home)
│   ├── Privacy & Telemetry
│   ├── Bloatware & Apps
│   ├── Services
│   ├── Performance
│   ├── UI & Personalization
│   ├── Context Menu
│   └── Restore / Backup
│
└── Motor de Tweaks
    ├── TweakEngine (ejecuta PowerShell / Registry)
    ├── BackupManager (guarda snapshots de registry antes de cada cambio)
    └── PresetEngine (Safe / Balanced / Aggressive)
```

---

## 6. Funcionalidades Principales

### 6.1 Presets Predefinidos

Tres modos de aplicación rápida:

| Preset | Descripción |
|---|---|
| **Safe** | Solo tweaks seguros y reversibles: deshabilita telemetría básica, quita publicidad del Start Menu, limpia accesos del menú contextual. |
| **Balanced** | Safe + deshabilitación de servicios opcionales, remoción de apps de terceros preinstaladas (Candy Crush, Netflix, TikTok, etc.), optimización de timeout de apagado. |
| **Aggressive** | Balanced + remoción de OneDrive, deshabilitación de Copilot/AI features, limpieza completa de Xbox services, deshabilitación de telemetría avanzada (DiagTrack), limpieza de tareas programadas innecesarias. |

### 6.2 Selección Manual (Tweak-by-Tweak)

- Cada tweak se muestra como una tarjeta con:
  - Nombre del tweak
  - Descripción breve de qué hace
  - Estado actual (✅ Ya optimizado / ⚠️ Pendiente / 🔴 Activo)
  - Toggle o checkbox para seleccionar
  - Botón de ayuda con explicación detallada (tooltip o modal)
- El usuario puede mezclar presets con selección manual libremente.

### 6.3 Restore / Undo (Siempre Disponible)

- Antes de aplicar **cualquier cambio**, WinPure genera automáticamente un snapshot del registro de Windows afectado.
- El snapshot se guarda localmente en `%AppData%\WinPure\Backups\`.
- En la página **Restore**, el usuario puede:
  - Ver el historial de backups con fecha/hora y tweaks aplicados
  - Restaurar backups individuales (un solo tweak) o completos (todos los cambios de una sesión)
  - Eliminar backups antiguos manualmente

### 6.4 Detección de Estado Actual

- Al cargar, WinPure escanea el sistema y muestra el estado real de cada tweak (aplicado / no aplicado / estado desconocido).
- Tweaks ya en estado óptimo se muestran en gris con etiqueta "Ya optimizado".

---

## 7. Categorías de Tweaks (extraídas de los repositorios)

### 7.1 Privacy & Telemetry
- Deshabilitar servicio DiagTrack (Connected User Experiences and Telemetry)
- Bloquear conexión saliente del Unified Telemetry Client
- Deshabilitar Bing Search en el Start Menu
- Deshabilitar instalación silenciosa de apps sugeridas
- Deshabilitar recopilación de datos de diagnóstico (AllowTelemetry → 0)
- Deshabilitar Activity History
- Deshabilitar Location Tracking
- Deshabilitar App Launch Tracking
- Deshabilitar anuncios personalizados (Advertising ID)
- Deshabilitar Windows Feedback / FeedbackHub

### 7.2 Bloatware & UWP Apps
**Apps de terceros preinstaladas:**
- Candy Crush (y variantes)
- Netflix, TikTok, Facebook, Twitter/X, Instagram, Spotify
- Skype
- Disney+, Amazon Prime

**Apps Microsoft opcionales:**
- OneDrive (uninstall completo + deshabilitar via GPO)
- Copilot / Windows AI
- Clipchamp
- Paint 3D / 3D Viewer
- Microsoft To Do
- Groove Music / Movies & TV (Zune)
- Solitaire Collection
- Wallet
- Whiteboard
- Xbox Gaming Overlay / Xbox App / Xbox TCUI / GamingServices
- YourPhone / Phone Link
- DevHome
- GetHelp / GetStarted

### 7.3 Services
- Deshabilitar SysMain (SuperFetch) — opcional, solo para SSDs
- Deshabilitar Windows Search Indexing — opcional
- Deshabilitar Print Spooler — si no se usa impresora
- Deshabilitar Remote Registry
- Deshabilitar Windows Error Reporting
- Deshabilitar Connected Devices Platform Service
- Deshabilitar Geolocation Service
- Deshabilitar Fax service
- Deshabilitar Bluetooth Support Service — si no se usa Bluetooth

### 7.4 Performance
- Reducir WaitToKillAppTimeout (5000ms → 2000ms)
- Reducir HungAppTimeout (5000ms → 1000ms)
- Reducir tiempo de apagado del sistema
- Deshabilitar animaciones de ventanas (para hardware bajo)
- Priorizar programas sobre servicios en background
- Deshabilitar Hibernación (libera espacio en disco)
- Configurar plan de energía en Alto Rendimiento o Equilibrado (recomendado)
- Deshabilitar Remote Desktop si no se usa

### 7.5 UI & Personalization
- Modo oscuro por defecto (AppColorMode → Dark)
- Deshabilitar Snap Assist Flyout
- Ocultar "New App Installed" indicator
- Ocultar "Suggested Apps" en Start
- Ocultar "Most Used Apps" en Start
- Ocultar "Recently Added Apps" en Start
- Mostrar extensiones de archivo en Explorer
- Mostrar archivos ocultos en Explorer
- Taskbar: eliminar Widget button, Task View button, Chat/Teams button
- Mover Start Menu a la izquierda (opción)
- Deshabilitar Aero Shake

### 7.6 Context Menu
- Remover "Edit with Clipchamp" del menú contextual
- Remover "Edit with Notepad" del menú contextual
- Remover "Edit with Photos" del menú contextual
- Remover "Ask Copilot" del menú contextual
- Remover "Share" del menú contextual
- Remover "Give access to" del menú contextual
- Restaurar menú contextual clásico de Windows 10

### 7.7 Scheduled Tasks (Tareas Programadas)
- Deshabilitar tarea de recopilación de compatibilidad de aplicaciones
- Deshabilitar Microsoft Compatibility Appraiser
- Deshabilitar Customer Experience Improvement Program tasks
- Deshabilitar tarea de feedback automático

---

## 8. UI/UX Design

### 8.1 Estilo Visual
- **Tema:** Fluent Design System, dark mode por defecto con opción light
- **Paleta base:**
  - Background principal: `#0F0F0F` / `#1A1A1A`
  - Surface cards: `#212121` / `#2A2A2A`
  - Accent color: Azul Fluent `#0078D4` (acción principal) con variante verde `#107C10` (estado "safe/optimizado")
  - Texto primario: `#FFFFFF` / `#F3F3F3`
  - Texto secundario: `#9D9D9D`
  - Peligro/warning: `#D13438`
- **Tipografía:** Segoe UI Variable (nativa de Windows 11)
- **Bordes:** Redondeados (8px), mismo estilo que Settings de Windows 11
- **Efectos:** Mica/Acrylic background opcional en la barra lateral

### 8.2 Layout
```
┌─────────────────────────────────────────────────────────┐
│  [🛡 WinPure]              [━ □ ✕]  (Title bar)         │
├──────────────┬──────────────────────────────────────────┤
│              │  Dashboard / Página activa               │
│  🏠 Home     │                                          │
│  🔒 Privacy  │  ┌────────────────────────────────────┐  │
│  📦 Apps     │  │ PRESET SELECTOR                    │  │
│  ⚙️ Services  │  │  [Safe]  [Balanced]  [Aggressive]  │  │
│  🚀 Perf.    │  └────────────────────────────────────┘  │
│  🎨 UI       │                                          │
│  🖱️ Context  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐   │
│  ♻️ Restore   │  │Tweak │ │Tweak │ │Tweak │ │Tweak │   │
│              │  │ card │ │ card │ │ card │ │ card │   │
│              │  └──────┘ └──────┘ └──────┘ └──────┘   │
│              │                                          │
│              │       [Apply Selected Tweaks]            │
└──────────────┴──────────────────────────────────────────┘
```

### 8.3 Flujo de Usuario Principal
1. Usuario abre WinPure → La app pide elevación UAC si no tiene permisos
2. Dashboard muestra resumen: X tweaks disponibles, Y ya optimizados, Z pendientes
3. Usuario elige: **Preset** (un clic) o navega por categorías manualmente
4. Al hacer clic en "Apply": WinPure crea backup automático → Aplica tweaks → Muestra log de resultados
5. En cualquier momento: ir a Restore → seleccionar backup → revertir

---

## 9. Requisitos No Funcionales

- **Seguridad:** No hace llamadas a servidores externos. Todo corre localmente. Sin telemetría propia.
- **Portabilidad:** Ejecutable standalone, sin instalador. Puede correrse desde USB.
- **Velocidad:** Escaneo inicial del sistema < 3 segundos. Aplicación de tweaks con feedback de progreso en tiempo real.
- **Logs:** Genera log en `%AppData%\WinPure\Logs\` de cada sesión con tweaks aplicados/revertidos y timestamps.
- **Idiomas:** English como idioma base. Estructura preparada para i18n (Spanish como segundo idioma en v1.1).
- **UAC:** Solicitud automática de permisos elevados al inicio si no se detectan.

---

## 10. Estructura del Repositorio en GitHub

```
winpure/
├── src/
│   ├── WinPure/              # Proyecto WPF principal
│   │   ├── Views/            # XAML pages
│   │   ├── ViewModels/       # MVVM ViewModels
│   │   ├── Models/           # TweakModel, BackupModel, etc.
│   │   ├── Services/         # TweakEngine, BackupManager, PresetEngine
│   │   ├── Resources/        # Estilos, diccionarios de recursos, íconos
│   │   └── Scripts/          # PowerShell scripts embebidos como recursos
├── docs/
│   ├── screenshots/
│   └── PRD.md
├── .github/
│   └── workflows/            # CI/CD build automatizado
├── README.md
├── LICENSE
└── CHANGELOG.md
```

---

## 11. Roadmap

| Versión | Features |
|---|---|
| **v1.0** | Core app: las 7 categorías de tweaks, 3 presets, sistema Restore/Undo, dark mode, UAC automático |
| **v1.1** | Soporte español, modo light, exportar/importar configuración como perfil JSON |
| **v1.2** | Plugin system (cargar tweaks custom desde .ps1 externos), búsqueda de tweaks |
| **v2.0** | App installer integrado (instalar apps como winget wrapper), detección de tweaks dañinos de terceros |

---

## 12. Prompt de Imagen (UI Mockup)

```
A high-fidelity UI mockup of a Windows desktop application called "WinPure", 
a Windows 11 debloater and optimizer tool. Fluent Design System aesthetic 
with dark theme. The window has a left sidebar with navigation icons and 
labels: Home, Privacy, Apps, Services, Performance, UI, Context Menu, Restore. 
The main content area shows a "Privacy & Telemetry" page with toggle switches 
for individual tweaks displayed as clean rounded cards with title, short 
description, and an on/off toggle. At the top there are three preset buttons: 
Safe (green), Balanced (blue), Aggressive (red/orange). An "Apply Changes" 
button with a blue accent is at the bottom right. The window uses Windows 11 
rounded corners, Mica background effect on the sidebar (semi-transparent 
blurred desktop), dark background #1A1A1A for the content area, white text, 
subtle card surfaces in #252525. The app icon is a minimalist blue shield with 
a sparkle. Clean, modern, professional. Style similar to Windows 11 Settings 
app but more powerful-looking. Ultra sharp 4K rendering.
```
