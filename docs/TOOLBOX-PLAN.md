<!-- Digest de minería (workflow wf_8053a493-941, 8 repos, 9 Sonnets) del 2026-09-13. ESTADO: las Fases A-E ya se construyeron y publicaron en v2.0.0 (ver CHANGELOG); lo que queda pendiente de decisión de Oscar es el bundling (~10%) y los ítems con puerta que se listan más abajo. -->

# Plan de integración — WinPure "todo en uno"

## La decisión grande

Hay una sola pregunta que decide casi todo lo demás: **¿WinPure sigue sin ejecutar código de terceros, o rompe esa regla por algunas piezas puntuales?**

La revisión de los 8 repos confirma que la regla se sostiene casi siempre. De **~70 features evaluadas**, la inmensa mayoría de lo que vale la pena son claves de registro, llamadas a APIs de Win32 documentadas (`shutdown.exe`, `SetSuspendState`, `mklink`, `robocopy.exe`, `ITaskbarList3`, WMI) o el propio `winget` — nada de eso pide descargar ni ejecutar un binario externo. Solo **dos features** sobreviven la poda y de verdad necesitan código de terceros (el runtime de DirectX 9 y el DLL de Everything), y en ambos casos el costo de seguridad es bajo y acotable. Todo lo demás que "descarga y ejecuta" (O&O ShutUp10, el removedor de Edge, el removedor de Defender, MAS, el instalador de Windows LTSC de 5GB) es exactamente el patrón que ya rechazaste con ViVeTool — a veces peor, porque además apaga el antivirus primero.

Conclusión: **el 90% del valor nuevo se gana sin tocar la filosofía.** El otro 10% (bundling) se hace solo si tú lo apruebas explícitamente, feature por feature, nunca por default en un preset.

---

## 1. Reimplementar nativo (fácil y en filosofía)

Deduplicado entre los 8 repos, ordenado de mayor a menor valor. "Ya existe" significa que no hay nada que construir — solo lo dejo para que no se repita el trabajo.

### Ya existe en WinPure (no reconstruir)
| Feature propuesta por un repo | Ya está como |
|---|---|
| Menú clásico Win10, quitar Share, quitar Include in Library, 300 vs 31 en el cap de selección, NTFS long paths | `ctx-classic-menu`, `ctx-share`, `ctx-include-in-library`, `ctx-multi-invoke`, perf long-paths |
| Game DVR/Xbox overlay, Dark Mode, Enhance Pointer Precision, Win32PrioritySeparation=38, MenuShowDelay=0, Remote Assistance | tweaks de Performance/UI/Privacy existentes |
| SFC+DISM, reset WU/red, temp cleanup, winget Install Apps | Repair tools + Install Apps |
| Auto-inicio vía Run/Run32/StartupApproved | Startup Apps |

### Nuevo, alto valor, bajo costo
| # | Feature | Mecanismo | Esfuerzo | Origen |
|---|---|---|---|---|
| 1 | **"Abrir CMD/PowerShell aquí"** en carpetas | Claves `Directory\shell\cmd` / `Background\shell\cmd`, additivas, reversibles | Pequeño | RCWM — el hueco más pedido, gratis |
| 2 | **Copiar/Mover a carpeta** vía el handler nativo de Windows | CLSIDs `{C2FBB630-...}`/`{C2FBB631-...}`, cero código propio | Pequeño | RCWM |
| 3 | **6 tweaks chicos de registro** verificados contra .admx: `FullPath` en barra de título, ocultar sugerencias de Quick Access, bloquear metadata de dispositivo por red, más pines en Start, mostrar todos los iconos de bandeja, desactivar auto-archivado de apps (23H2+), desactivar EcoQoS throttling | 6 `RegistryValueAction` independientes | Pequeño | WinKit |
| 4 | **Reversed scroll wheel fix** | `FlipFlopWheel` por dispositivo de mouse detectado | Pequeño | Win11 Customizer |
| 5 | **Quitar "3D Objects" de Este equipo** | Borrar CLSID `{0DB7E03F-...}` + espejo WOW6432 | Pequeño | WinKit |
| 6 | **Hardware-accelerated GPU Scheduling** | `HwSchMode` DWORD | Pequeño | WinKit |
| 7 | **GlobalTimerResolutionRequests** (SOLO la clave, nunca el servicio) | Un DWORD en Performance | Pequeño | WinKit |
| 8 | **VC++ Redistributables (2005-2022) en un botón** | Paquetes winget ya verificados, reusa la página Install | Pequeño | WinKit |
| 9 | **winget "actualizar todo"** (sin `--ignore-security-hash`) | Extensión de Install Apps | Pequeño | WinKit |
| 10 | **GodMode** en el fondo del escritorio | Una clave CLSID `{ED7BA470-...}` | Pequeño | RCWM |
| 11 | **Quitar "Anclar a Quick Access/Start"** | Borrar `pintohome` y equivalente | Pequeño | RCWM |
| 12 | **Re-registrar todos los paquetes AppX** (reparación Start/Store) | `Add-AppxPackage -Register` por manifiesto ya en disco | Pequeño | Win11 Customizer |
| 13 | **Reset del app Configuración** | `Reset-AppxPackage` sobre ImmersiveControlPanel | Pequeño | Win11 Customizer |
| 14 | **Reset de escala DPI** | Borrar `LogPixels` en `HKCU\Control Panel\Desktop` | Pequeño | Win11 Customizer |
| 15 | **Asegurar políticas de Startup Tasks habilitadas** (seguro de vida para la propia página Startup) | 4 DWORDs, verificar contra .admx primero | Pequeño | Win11 Customizer |
| 16 | **Arreglar asociación de .ps1** | Un valor bajo `Microsoft.PowerShellScript.1\Shell\Open\Command` | Pequeño | Win11 Customizer |
| 17 | **Countdown/apagado programado** (apagar, reiniciar, dormir, hibernar, bloquear, cerrar sesión — en N min o a una hora) | `shutdown.exe`, `SetSuspendState`, `LockWorkStation` — categoría NUEVA tipo Cleanup/Install (irreversible por naturaleza, con confirmación fuerte) | Mediano | TimedPower |
| 18 | **Barra de progreso en la taskbar + aviso toast + confirmación si el countdown es muy corto** | `ITaskbarList3` COM + Toast/WPF, mismo patrón que SystemGuards | Pequeño | TimedPower |
| 19 | **Tareas de apagado recurrentes** ("cada noche a las 23:30") | Task Scheduler NATIVO reusando `ScheduledTaskAction`/`ScanContext.WatchedTasks` — NUNCA el proceso-tray-residente-con-autostart de TimedPower | Mediano | TimedPure |
| 20 | **Menú "Apagar en 15s/1min" en el fondo del escritorio** (opt-in, apagado por default) | Claves HKCU bajo `Background\shell\` — nota: es la única entrada de este proyecto que AÑADE al menú contextual en vez de quitar | Pequeño | TimedPower |
| 21 | **"Run with Priority" por lanzamiento** en .exe (Realtime/High/.../Low) | Submenu que llama `start /priority-flag` | Pequeño | RCWM |
| 22 | **MSI Mode para interrupciones de GPU** | Clave por instancia de dispositivo, requiere resolver el InstanceId en cada Capture/Restore | Mediano | WinKit |
| 23 | **Toggle independiente de Game Mode/Game DVR** (hoy solo existe como side-effect de quitar Xbox) | 3 `RegistryValueAction` con backup real | Pequeño | VoltAir |
| 24 | **Limpiar la variable PATH** (quitar entradas muertas/duplicadas) | Captura PATH completo (Machine+User) antes de escribir, broadcast `WM_SETTINGCHANGE` extendido | Mediano | Win11 Customizer |
| 25 | **Desinstalador genérico de programas Win32** (además del catálogo curado de Quitar apps) | Enumera `Uninstall` registry keys, llama `UninstallString` — va en su propia sección irreversible, como Quitar apps | Pequeño | VoltAir |
| 26 | **Categorías de caché de launchers de juegos** en la página Limpieza (Steam, Epic, Battle.net) | Verificar rutas contra 26200 antes de agregar | Pequeño | ZenClean |
| 27 | **Aviso de disco casi lleno** (tray + toast) | Watcher en segundo plano, solo notifica, nunca limpia solo | Mediano | ZenClean |
| 28 | **Exportar diagnóstico** (zip de logs+scan+versión, con SID/usuario redactados) para soporte | Reusa `%AppData%\WinPure\Logs` | Pequeño | DeskBox |
| 29 | **Panel de info del sistema** (versión, edición, activación, uptime) | WMI/registro de solo lectura | Pequeño | VoltAir |
| 30 | **Take Ownership recursivo** (herramienta de técnico) | `takeown`+`icacls` — SIEMPRE a Administradores/usuario actual, NUNCA al SID `OWNER RIGHTS` (S-1-3-4) que ya tratas como falsificable en BackupStore | Mediano | RCWM |
| 31 | **Boot a Modo Seguro / Recovery (WinRE)** con "Modo normal" de vuelta | `bcdedit /set safeboot` + `shutdown /r`, encaja en el patrón `SystemStateAction` (captura real, restaura real) | Mediano | RCWM |
| 32 | **Crear symbolic link/hardlink/junction** desde el menú contextual | `mklink` nativo, necesita estado efímero ("origen recordado") | Mediano | RCWM |
| 33 | **Migración de carpetas de apps vía NTFS Junction** (Docker Desktop, Steam, VS Code, caches npm/pip a otro disco) | Máquina de 5 fases resumible (preflight→copiar→verificar→junction→listo), con rollback completo — el diseño de ZenClean es sólido, cópialo, no el catálogo de apps chinas | Grande | ZenClean |
| 34 | **RoboCopy multi-hilo para copiar/mover carpetas** desde el menú contextual | `robocopy.exe` nativo (Windows lo trae) — es una operación de archivos, no un toggle: va en página propia como Cleanup/Install, nunca en TweakEngine | Grande | RCWM |
| 35 | **Corrección de curva de mouse (linear, sin aceleración)** | REG_BINARY — primero hay que confirmar que el engine soporta ese tipo de valor, no solo DWORD/string | Mediano | WinKit |
| 36 | **Panel de hardware en vivo** (CPU/RAM/GPU/temps) | LibreHardwareMonitorLib (MIT, NuGet) — sería la PRIMERA dependencia NuGet del proyecto y trae su propio driver tipo WinRing0 que puede disparar el antivirus. **Pregúntale a Oscar antes de agregar la dependencia**, aunque técnicamente no viola "no descargar/ejecutar binarios" | Mediano | VoltAir |
| 37 | **Bin de cuarentena de 72h para Limpieza** (mover a staging en vez de borrar, auto-purgar) | Revierte una decisión que YA tomaste ("Limpieza es irreversible y está bien") — **pregunta antes de construir**, no lo metas solo | Mediano | ZenClean |

Nota de esfuerzo/valor: los items 1–16 son, literalmente, una tarde cada uno y suman el gap más visible que señalan los 8 repos juntos (menú contextual y reparación). Los items 33–36 son apuestas grandes de arquitectura nueva — no las mezcles con el resto del lote.

---

## 2. Empaquetar / enlazar (requiere código externo)

Solo dos casos sobreviven, y ambos son "user-initiated, con verificación, nunca en un preset":

### DirectX 9 End-User Runtime (legado)
- **Por qué**: hay juegos viejos que aún lo piden y no está en winget.
- **Cómo hacerlo seguro**: descargar SOLO el instalador oficial de `download.microsoft.com` (no el genérico, el de junio 2010), verificar HTTPS + hash/firma Authenticode antes de ejecutar, correrlo con sus propios flags silenciosos (`/Q`) — **nunca** el patrón de WinKit de descargar 7-Zip de un tercero solo para extraerlo. Botón separado, con texto explícito de "esto descarga un instalador de Microsoft.com y lo ejecuta" antes de dar clic.
- **Costo de seguridad**: bajo si se ancla al dominio de Microsoft y se verifica firma; sin eso, es exactamente el patrón ViVeTool.

### Búsqueda instantánea vía Everything (voidtools)
- **Por qué**: búsqueda de archivos por IPC es un feature real y voidtools permite redistribuir su SDK.
- **Cómo hacerlo seguro**: el DLL (`Everything64.dll`) solo habla IPC con una instancia de `Everything.exe` YA instalada por el usuario — no ejecuta nada nuevo. Si Everything no está instalado, **no** descargarlo en silencio (como hace VoltAir): mandar al usuario a la página Install Apps, que ya tiene `voidtools.Everything` como id conocido en winget.
- **Costo de seguridad**: bajo — es una dependencia binaria cerrada nueva (rompe "cero dependencias"), pero no ejecuta código descargado en runtime.

**Todo lo demás que "descarga y ejecuta" se rechaza** (ver sección 3) — no hay un tercer caso de bundling que valga la pena.

---

## 3. Rechazado (snake-oil o fuera de filosofía)

| Feature | Repo | Por qué |
|---|---|---|
| **Desactivar UAC** | RCWM | Quita una frontera de seguridad completa del SO por cero ganancia — lo más parecido a snake-oil de seguridad que existe |
| **Descargar y correr O&O ShutUp10** | Win11 Customizer | Descarga binario sin firma ni hash; redundante con 130+ tweaks de Privacy que ya tienen backup real |
| **"Diagnostic Reset" (enciende telemetría/ads)** | Win11 Customizer | Va en la dirección OPUESTA a la categoría Privacy de WinPure |
| **Reset de Group Policy (borra HKCU/HKLM\Policies + carpetas)** | Win11 Customizer | Irreversible, sin backup, se llevaría entre las patas las propias políticas de Edge de WinPure y GPOs corporativas legítimas |
| **Reset de Usuarios locales (desactiva cuentas, reactiva Administrador)** | Win11 Customizer | Bloquea otras cuentas sin consentimiento, reactiva una cuenta que Microsoft desactiva por diseño, y el script ni siquiera funciona sin interacción |
| **Cambiador de fuentes del sistema** | Win11 Customizer | Hack de substitución de fuentes no soportado, ya te mordió un bug real de fuentes corruptas |
| **Context menu para instalar/parar/quitar servicios desde cualquier .exe** | Win11 Customizer | Foot-gun: ejecuta InstallUtil/Start-Service sobre lo que sea que clic-derecho | 
| **Bundle de theming de terminal/VS Code, Chocolatey, Nerd Fonts, etc.** | Win11 Customizer | Personalización de dev-environment, no debloat/optimización; la inyección de CSS en VS Code además rompe con cada auto-update |
| **MMCSS "Games" priority tuning, network throttling disable, core-parking unpark, hack de prioridad de hilo del driver NVIDIA, forzar 100% DPI** | WinKit | Snake-oil clásico de "gamer booster" repetido hace 10 años sin benchmark que lo respalde; el de DPI además rompe pantallas 4K |
| **`winget upgrade --ignore-security-hash`** | WinKit | Desactiva la verificación de integridad de winget — nunca copiar esos flags |
| **Downgrade de ExecutionPolicy al arrancar** | WinKit | Debilita una config de seguridad de PowerShell sin preguntar |
| **Desinstalar Edge (apaga Defender + descarga .exe sin firmar de una rama mutable)** | VoltAir | Patrón de entrega de malware textual — apaga el antivirus para que pase la descarga |
| **Desinstalar Windows Defender (mismo patrón + auto-confirma con stdin)** | VoltAir | Igual de grave, sin ningún camino de reinstalación |
| **Activación de Windows (MAS/HWID)** | VoltAir | No es debloat, es evasión de licenciamiento — riesgo legal/reputacional para un proyecto público a tu nombre |
| **Instalar Windows 11 IoT LTSC vía ISO de 5GB desde URL firmada expirable** | VoltAir | Combina TODOS los anti-patrones: URL que caduca (como ViVeTool), reinstalación de SO completa sin backup ni revert |
| **Vaciar la carpeta Downloads del usuario** | VoltAir | Borra archivos reales del usuario, no caché regenerable — viola tu propia regla de Limpieza |
| **Clasificador de riesgo con IA en la nube (GLM-4-flash)** | ZenClean | Manda rutas/nombres de archivo a un API de terceros; sustituye tu catálogo determinista y verificado por una caja negra no auditable |
| **Borrado de `$PatchCache$` por "toma de control de msiserver"** | ZenClean | Heurística por antigüedad sin verificar si el producto dueño sigue instalado — puede romper reparación/desinstalación de software real |
| **Catálogo de apps chinas (WeChat, QQ, DingTalk, etc.)** | ZenClean | Fuera de la audiencia real de WinPure, solo ensucia el catálogo con "no aplica" |
| **Capa de licenciamiento comercial/DRM** | ZenClean | Contradice "Free for everyone, forever" |
| **Binarios helper precompilados de RCWM (rcwm-*.exe)** | RCWM | Trampa: aunque el código fuente viene incluido, es el mismo patrón de ViVeTool si se bundlean en vez de reimplementar en C# nativo |
| **Quitar "Scan with Defender" del menú, atajos de apagado/Panel de Control genéricos, GUI de asociación de pwsh, context menu de Terminal Canary, Set-ExecutionPolicy vía instalar RSAT-GPO** | Varios | Bajo valor, o dañan la percepción de confianza (ocultar el atajo de escaneo de Defender), o instalan una feature completa de Windows solo como side-effect |
| **Widgets de escritorio, todo-list, clima, control de música (DeskBox)** | DeskBox | Categoría de producto distinta (organizador de escritorio), scope-creep hacia el mismo tipo de bloat que WinPure existe para quitar |

---

## 4. Orden de construcción propuesto

Mismo formato que el mega-loop anterior: fases chicas, cada una con su prueba vista en rojo primero, docs sincronizadas, y exe verificado antes de pasar a la siguiente.

**Fase A — Menú contextual y registro, lote grande de wins baratos**
Items 1, 2, 3, 4, 5, 6, 7, 10, 11, 15. Todos son `RegistryKeyAction`/`RegistryValueAction` puros, mismo patrón que el catálogo ya tiene. Verificar cada clave contra la máquina real y contra `.admx` antes de comitear, como ya haces.

**Fase B — Repair tools, ronda 2**
Items 12, 13, 14, 16, 8, 9. Estas son adiciones directas a `RepairCatalog.cs`/Install Apps, sin categoría nueva.

**Fase C — Apagado programado (categoría nueva)**
Items 17, 18, 19, 20. Es la pieza de mayor esfuerzo de las "chicas": nueva página + servicio de countdown + integración con Task Scheduler reusando `ScheduledTaskAction`. Trátala como su propia fase porque introduce UI nueva, no solo un tweak.

**Fase D — Herramientas de técnico (Repair, Manual-only)**
Items 30, 31, 21, 22, 23. Cada una necesita su propio diseño de confirmación fuerte (Take Ownership y Safe Mode son las más delicadas).

**Fase E — Gestión de apps y PATH**
Items 24, 25, 36 (con aprobación previa de Oscar por la dependencia NuGet), 29.

**Fase F — Limpieza y espacio en disco**
Items 26, 27, 37 (pregunta primero — revierte una decisión tuya), 33 (la migración por Junction, en su propia sub-fase por ser grande).

**Fase G — Grandes construcciones nuevas**
Items 32, 34, 35 — cada una es suficientemente grande (nueva página, nuevo tipo de acción, soporte REG_BINARY) para ser su propia fase con su propio lote de pruebas.

**Fase H — Diagnóstico y soporte**
Item 28.

**Fase I (opcional, requiere tu aprobación explícita) — Bundling**
DirectX 9 runtime y Everything search. Van al final a propósito: son el único punto donde cambias de filosofía, así que no deben mezclarse con el resto del trabajo "seguro".

Cada fase sigue el patrón que ya usas: prueba vista en ROJO contra el código sin el fix, tabla de `docs/tweaks.md` y README sincronizados en el mismo commit, exe recompilado y copiado al Escritorio, y una revisión de "tweaks sin nada que hacer en esta máquina" antes de dar la fase por cerrada.