# WinPure — Plan "todo en uno" (all-in-one)

> Objetivo de Oscar (13-sep-2026): que WinPure sea **la** app definitiva de Windows —
> velocidad, rendimiento, privacidad, limpieza, debloat, instalación— todo junto, en una sola app.
> Este documento es el PLAN. El **mega-loop** (Opus orquesta, muchos Sonnets investigan/revisan/
> implementan) lo dispara Oscar DESPUÉS de aprobar este plan. Nada de esto está construido aún.

## Cómo leerlo si llegas nuevo (o se fue la luz)

- Lo construido hasta hoy vive en la rama `fix/engine-scan-catalog` (PR #1, CI verde, sin merge a `main`).
  El estado real está en `git log --oneline` y en `docs/ROADMAP.md` (sección *Status*).
- El `CLAUDE.md` de la carpeta padre es la fuente de verdad de arquitectura y trampas. Empieza por ahí.
- Este plan es lo NUEVO: convertir el debloater actual en un "todo en uno". Aún no aprobado del todo:
  faltan las respuestas de Oscar en *Decisiones abiertas* (abajo).

## Guardarraíles (no negociables, salen del CLAUDE.md)

1. **Los tweaks se respaldan y se deshacen.** Eso se mantiene para lo que es reversible. Pero **la
   limpieza borra, y de eso se trata** (decisión de Oscar, 13-sep-2026): no hay que contorsionar nada para
   hacerla "reversible" ni tratarla como si rompiera una promesa. Va en su propia sección junto a *Quitar
   apps* / *Instalar*, con vista previa de tamaño y un "¿seguro?" antes de borrar —eso es buena UX, no una
   disculpa—. El texto del Dashboard ("cada cambio se respalda… salvo las apps que quitas") se amplía para
   nombrar también la limpieza.
2. **Cero dependencias NuGet, WPF artesanal.** Todo lo nuevo se porta como código propio (P/Invoke,
   PowerShell por `PowerShellRunner`), no como un binario de terceros embebido.
3. **Nada de placebo ni de riesgo por "completar la lista".** Los limpiadores de registro, los tweaks
   "gamer" de baja latencia sin fundamento y los instaladores desde dominios opacos **se rechazan**, con
   su razón escrita. "Todo en uno" no significa "todo lo que existe": significa "todo lo que sirve y es
   seguro". Un Sonnet revisor decide incluir / ya-lo-tenemos / descartar, y otro lo ataca.
4. **Una prueba nueva no vale hasta verla en ROJO** contra el bug que dice cubrir. Toda acción nueva
   captura el estado real antes de tocar nada (invariante del motor).
5. **Se mide en esta máquina** (es-MX, 26H2 26200); las referencias envejecen.

## Estado actual (lo que YA hay)

- **111 tweaks** en 9 categorías: Privacy 21, UI 19, Performance 15, Features 8, ContextMenu 8, Edge 7,
  Apps 6, RemoveApps (16 desinstalaciones + xbox + onedrive), Services 1.
- Páginas: Dashboard, las 9 categorías, **Arranque**, **Instalar** (winget, catálogo curado),
  **Reparar** (5 herramientas one-shot, incluida *Clean Temporary Files* — semilla del limpiador),
  **Quitar apps**, **Restaurar**, **Edge**. Español completo.
- Motor con respaldo antes de cada acción, restauración al valor real, store de respaldos protegido por
  dueño, y "aplicar a usuarios futuros".

## Repos fuente — veredicto por repo

Los 5 de referencia que ya teníamos (Win11Debloat, Sophia, winutil, CrapFixer, Win-Debloat-Tools) ya
dieron el subconjunto curado de 111 tweaks. **Falta una auditoría sistemática** (ver bucket H) porque se
tomó un subconjunto a propósito, no todo. Los 8 nuevos:

| Repo | Qué es | Veredicto |
|---|---|---|
| **darkmatter2048/WindowsCleaner** | Limpiador de disco (Tauri/TS, 4.7k★): C:, cachés, temp. Borra archivos. | **Adoptar** el concepto de limpieza con **vista previa de tamaño**. Irreversible → sección aparte. |
| **thedogecraft/sparkle** | "Todo en uno" (Electron): 40 tweaks, limpieza de 6 categorías con tamaño, 15 utilidades, DNS, instalador 156 apps, dashboard en vivo. | **Mapa de referencia** del all-in-one. Adoptar: limpieza-con-tamaño, DNS, más utilidades, dashboard en vivo, catálogo más grande. |
| **hellzerg/optimizer** | Optimizador maduro (.NET 4.8): privacidad, servicios, limpieza, quitar UWP, **editor HOSTS**, **DNS**, **editor de PATH**, info de hardware, ping/latencia. | **Adoptar** HOSTS, DNS, editor de PATH (nuevas capacidades). El resto (privacidad/servicios) mayormente ya lo tenemos → auditar. |
| **IgorMundstein/WinMemoryCleaner** | Limpiador de RAM (C#/WPF) por API nativa: 7 áreas de memoria. One-shot o servicio. | **Adoptar con honestidad**: herramienta manual. En Windows moderno el beneficio es discutible (vaciar la standby list puede *empeorar*). Ver decisión. |
| **microsoft/winget-cli** | winget en sí. `winget search` hace match de subcadena en nombre/id/tags. | **Adoptar**: caja de búsqueda en *Instalar* para instalar apps arbitrarias (lo pidió Oscar). |
| **ScalarLinkAxe/Optimizer-Toolkit** | PowerShell: latencia, timers (HPET), prioridad CPU/RAM, efectos visuales. Instalador desde `true-soft.su`, sin revert, dispara antivirus. | **Cautela**: cherry-pick solo lo verificado y reversible (algo ya lo tenemos: HPET, efectos visuales). Rechazar lo opaco/irreversible. |
| **YSGStudyHards/Awesome-Tools** | Lista curada de herramientas de desarrollo (links), no código. | **Descartar como integración.** A lo sumo, minar ideas de apps para el catálogo de *Instalar*. |
| **microsoft/coreutils** | Utilidades POSIX en Rust (ls, cat, grep…) para Windows. | **Descartar**: no tiene que ver con debloat/optimización. |

## Mapa de huecos — capacidades NUEVAS a construir

Cada una dice si es reversible y dónde vive en la UI. Las reversibles pasan por el motor (respaldo antes
de tocar); las irreversibles van en secciones marcadas, sin falso deshacer.

- **A. Limpieza de disco / basura** (WindowsCleaner, sparkle, optimizer) — temp, prefetch, papelera, caché
  de Windows Update, miniaturas, reportes de error, cachés de navegador. Borra archivos, y está bien: es una
  sección de limpieza. Nueva página *Limpieza* con **vista previa de tamaño por categoría** y un "¿seguro?"
  antes de borrar. Amplía la *Clean Temporary Files* actual a algo serio. (Datos del usuario —Descargas,
  etc.— solo si Oscar lo pide y con aviso claro.)
- **B. Limpiador de memoria/RAM** (WinMemoryCleaner) — vaciar working sets / standby list por API nativa.
  One-shot. Mayormente seguro (reasigna caché). Herramienta en *Reparar* o una acción de *Rendimiento*.
  Framing honesto (no es magia). *(Decisión abierta.)*
- **C. Gestor de DNS** (optimizer, sparkle) — cambiar a presets (Cloudflare, Google, Quad9…) + flush.
  Reversible (guarda el DNS previo). Nueva herramienta/sección.
- **D. Editor de HOSTS / bloqueo por hosts** (optimizer) — bloquear dominios de telemetría vía hosts.
  Reversible (respaldar hosts). Complementa el bloqueo por firewall que ya hace el preset Agresivo.
- **E. Buscar e instalar apps de winget** (winget-cli) — caja de búsqueda en *Instalar*. **Lo pidió Oscar.**
- **F. Dashboard en vivo** (sparkle) — uso de CPU/GPU/RAM/disco en el panel. Cosmético, nice-to-have.
- **G. Más utilidades one-shot** (sparkle, optimizer) — chkdsk, reiniciar driver de GPU, Storage Sense,
  editor de PATH, etc. Amplía *Reparar*.
- **H. Auditoría de tweaks** — para responder "¿incluimos TODO de los repos?": comparar CADA tweak de cada
  repo de referencia contra los 111 de WinPure y decidir incluir / ya-lo-tengo / descartar, **con razón por
  cada uno**. La respuesta honesta hoy: WinPure tomó un subconjunto verificado a propósito (rechazó cosas
  por UCPD, ms-gamebar, políticas inexistentes en 26H2, placebo). Esta auditoría lo hace explícito y
  encuentra los huecos reales.
- **I. Catálogo de instalación más grande** (sparkle 156, optimizer) — más apps populares en *Instalar*.

## Arreglos de UX que pidió Oscar (13-sep-2026)

1. **Buscador en *Instalar*** — caja para buscar cualquier app de winget, no solo el catálogo curado (bucket E).
2. **Tarjetas del Dashboard clicables** — al hacer clic en *Ya optimizados* (83) debe mostrar QUÉ se
   optimizó; en *Pendientes* (28) debe abrir una vista con todo lo pendiente ("mira, todo esto no has puesto,
   por si lo quieres poner"). Hoy las 4 tarjetas de conteo no hacen nada. → hacerlas navegar/filtrar a una lista.
3. **Arranque en lote, no al instante** — hoy *Arranque* aplica y respalda en CADA toggle, así que apagar
   varias apps crea varios respaldos viejos (Oscar vio 2 respaldos de "Opera GX" a las 11:10). Debe: marcar
   todas las que quiera y **un** botón *Aplicar cambios* abajo → **un** respaldo. 🔴 **Esto revierte una
   decisión previa** (el CLAUDE.md decía "aplica al instante porque es lo que espera quien viene del
   Administrador de tareas"); Oscar prefiere el lote. Documentar el porqué del cambio.

## Fases propuestas (orden sugerido)

- **Fase 0 — arreglos de UX chicos y claros** (rápido, no necesita el mega-loop): (1) buscador winget,
  (2) tarjetas del dashboard clicables → lista, (3) arranque en lote. *(Oscar: ¿los hago ya, antes del loop?)*
- **Fase 1 — Limpieza** (bucket A): página *Limpieza* con vista previa de tamaño, categorías seguras primero.
- **Fase 2 — Auditoría de tweaks** (bucket H): Sonnets minan cada repo, proponen tweaks nuevos; revisor
  adversario; los aprobados se portan con prueba en rojo. Sube el catálogo de 111 a lo que aguante la verdad.
- **Fase 3 — Red/DNS/HOSTS** (buckets C, D).
- **Fase 4 — Memoria + utilidades** (buckets B, G).
- **Fase 5 — Catálogo de apps más grande + dashboard en vivo** (buckets I, F).

Cada fase: un commit "Phase N", Sonnets revisores antes de aplicar, pruebas vistas en rojo, docs (tweaks.md,
README, ROADMAP) y CLAUDE.md actualizados, y verificación real donde se pueda.

## Cómo corre el mega-loop

- **Opus (yo) orquesta**; los Sonnets hacen el trabajo pesado en paralelo. Roles:
  - *Minero de repo*: descarga y lee UN repo, extrae features/tweaks concretos con su clave/comando real.
  - *Auditor de tweaks*: cruza lo minado contra el catálogo actual (incluir / ya-lo-tengo / descartar + razón).
  - *Revisor adversario*: ataca cada propuesta (¿existe en 26H2? ¿reversible? ¿placebo? ¿rompe algo instalado?).
  - *Implementador*: porta lo aprobado con su prueba en rojo (en worktree aislado para no chocar).
- **Su única escritura es su archivo de hallazgos**; no editan el proyecto salvo el implementador, y ese en
  worktree. Verifico cada propuesta antes de aplicar. Nunca 3+ agentes en el navegador a la vez.
- El loop escribe su avance a disco a media tarea, para sobrevivir a un corte.

## Decisiones abiertas (pre-vuelo)

Resueltas por Oscar el 13-sep-2026:
- **Limpieza**: borra, de eso se trata; sin preocuparse por la reversibilidad. Vista previa de tamaño + "¿seguro?".
- **Resto del plan**: aprobado ("todo lo demás está bien").

Pendientes, chicas, no bloquean el plan:
1. **Fase 0 (los 3 arreglos de UX): ¿ya, antes del mega-loop, o dentro del loop?** (El de *Arranque* en lote
   conviene pronto: hoy genera respaldos de más.)
2. **Snake-oil**: salvo que Oscar diga lo contrario, se **rechaza** limpiador de registro, tweaks "gamer"
   opacos sin revert e instaladores de dominios raros (no romper máquinas ni engañar).
3. **Limpiador de RAM**: por defecto entra como **herramienta manual con explicación honesta** (en Windows
   moderno suele ser cosmético); sin agendado automático salvo que Oscar lo pida.

## Riesgos

- **Alcance**: "todo en uno" es enorme; el riesgo es construir mucho y que se archive (ya pasó). Por eso va
  por fases con tu visto bueno y pruebas.
- **Marca/confianza**: meter limpieza/placebo mal hecho rompe la promesa de reversibilidad. Mitigado con
  secciones irreversibles marcadas y rechazo de placebo.
- **Antivirus**: limpiadores de memoria y algunos tweaks disparan AV; hay que firmar o documentar.
- **Referencias que envejecen**: todo tweak se comprueba contra la máquina real, no contra el repo.
