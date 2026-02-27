# AvoPerformanceSetupAI

A WinUI 3 (Windows App SDK) desktop application for AI-assisted motorsport setup management.

---

## 🔄 Sincronización automática PC ↔ GitHub / Automatic PC ↔ GitHub Sync

Esta función mantiene tu carpeta local y el repositorio de GitHub **siempre sincronizados** sin que tengas que hacer nada manualmente.  
This feature keeps your local folder and the GitHub repository **always in sync** without any manual steps.

### Requisitos previos / Prerequisites

- **Git** instalado y configurado con tus credenciales de GitHub (o SSH key).  
  Git installed and configured with your GitHub credentials (or SSH key).  
  → <https://git-scm.com/downloads>
- El repositorio debe estar **clonado** en tu PC (Opción 1 abajo).  
  The repo must be **cloned** to your PC (Option 1 below).

### Cómo funciona / How it works

| Dirección / Direction | Cómo / How |
|---|---|
| **PC → GitHub** | `scripts/sync.ps1` vigila la carpeta. Al detectar un cambio guarda un commit y hace `git push` automáticamente. |
| **GitHub → PC** | El mismo script comprueba el repositorio remoto cada 60 s y hace `git pull` si hay nuevas versiones. |

### Uso rápido / Quick start

1. Clona el repositorio (ver Opción 1 abajo) y abre **PowerShell** en la carpeta raíz.
2. Ejecuta el script de sincronización:

```powershell
.\scripts\sync.ps1
```

Deja la ventana de PowerShell abierta. Verás un log en tiempo real de cada push/pull.

3. (Opcional) Para que arranque **automáticamente al iniciar Windows**, ejecuta PowerShell **como Administrador** y registra la tarea programada:

```powershell
.\scripts\install-sync-task.ps1
```

Para iniciarla de inmediato sin reiniciar:

```powershell
Start-ScheduledTask -TaskName "AvoSetups-GitSync"
```

Para desinstalarla:

```powershell
.\scripts\install-sync-task.ps1 -Uninstall
```

> **Nota:** la primera vez que ejecutes scripts de PowerShell puede que necesites ajustar la política de ejecución:  
> `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`

---

### Quick start (English)

1. Clone the repository (see Option 1 below) and open **PowerShell** in the root folder.
2. Run the sync script:

```powershell
.\scripts\sync.ps1
```

Keep the PowerShell window open. You will see a real-time log of every push/pull.

3. (Optional) To start sync **automatically on Windows login**, open PowerShell **as Administrator** and register the scheduled task:

```powershell
.\scripts\install-sync-task.ps1
```

To start it immediately without restarting:

```powershell
Start-ScheduledTask -TaskName "AvoSetups-GitSync"
```

To remove it:

```powershell
.\scripts\install-sync-task.ps1 -Uninstall
```

> **Note:** the first time you run PowerShell scripts you may need to allow execution:  
> `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`

---

## ⬇️ Cómo descargar el proyecto / How to Download

### Opción 1 — Git clone (recomendado / recommended)

Abre una terminal (PowerShell, CMD o Git Bash) y ejecuta:

```bash
git clone https://github.com/avotattoocastro-creator/setups.git
cd setups
```

> Si no tienes Git instalado, descárgalo en <https://git-scm.com/downloads>.

---

### Option 1 — Git clone (recommended)

Open a terminal (PowerShell, CMD, or Git Bash) and run:

```bash
git clone https://github.com/avotattoocastro-creator/setups.git
cd setups
```

> If you don't have Git, download it at <https://git-scm.com/downloads>.

---

### Opción 2 — Descargar ZIP / Download ZIP

1. Ve a la página principal del repositorio:  
   <https://github.com/avotattoocastro-creator/setups>
2. Haz clic en el botón verde **`< > Code`**.
3. Selecciona **Download ZIP**.
4. Extrae el archivo descargado en la carpeta que prefieras.

---

### Option 2 — Download ZIP

1. Go to the repository home page:  
   <https://github.com/avotattoocastro-creator/setups>
2. Click the green **`< > Code`** button.
3. Select **Download ZIP**.
4. Extract the downloaded archive to any folder.

---

### Opción 3 — GitHub Desktop

1. Descarga [GitHub Desktop](https://desktop.github.com/).
2. En la página del repositorio haz clic en **`< > Code` → Open with GitHub Desktop**.
3. Elige la carpeta local y haz clic en **Clone**.

---

### Option 3 — GitHub Desktop

1. Download [GitHub Desktop](https://desktop.github.com/).
2. On the repository page click **`< > Code` → Open with GitHub Desktop**.
3. Choose a local folder and click **Clone**.

---

## Prerequisites

- **Windows 10/11** (version 1809 / build 17763 or later; build 19041 recommended)
- **Visual Studio 2022** (version 17.8+)
  - Workload: **.NET Desktop Development**
  - Workload: **Windows Application Development** (includes Windows App SDK)
- **Windows App SDK 1.5** (installed automatically via NuGet)

> **Nota / Note — Visual Studio 18 Insiders:**  
> Si tienes instalado Visual Studio 18 Preview / Insiders **sin** el componente de
> C++ (`VC\Tools\MSVC`), el build fallará con el error `GetLatestMSVCVersion /
> DirectoryNotFoundException`. El archivo `Directory.Build.props` incluido en este
> repositorio ya contiene un workaround automático que omite esas instalaciones.  
> If you have Visual Studio 18 Preview / Insiders installed **without** the C++ workload,
> the build fails with `GetLatestMSVCVersion / DirectoryNotFoundException`.
> The `Directory.Build.props` file included in this repo automatically works around
> the issue by skipping VS installations that do not have `VC\Tools\MSVC`.

## Opening the Solution

1. Download the repository using one of the methods in [⬇️ Cómo descargar el proyecto](#️-cómo-descargar-el-proyecto--how-to-download) above
2. Open `AvoPerformanceSetupAI.sln` with Visual Studio 2022
3. Restore NuGet packages (right-click solution → Restore NuGet Packages)
4. Set build platform to **x64**
5. Press **F5** to build and run

## Architecture

- **MVVM** pattern using [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **WinUI 3** with Windows App SDK 1.5 (packaged/MSIX)
- **DataGrid** via CommunityToolkit.WinUI.Controls.DataGrid
- **Localization**: Spanish (es-ES) and English (en-US) via `.resw` resource files

## Localization

Resource files are in `AvoPerformanceSetupAI/Strings/`:
- `en-US/Resources.resw` — English strings
- `es-ES/Resources.resw` — Spanish strings

## Project Structure

```
AvoPerformanceSetupAI/
├── Models/           # Data models (SetupIteration, Proposal)
├── ViewModels/       # MVVM ViewModels with CommunityToolkit.Mvvm
├── Views/            # Tab pages (Configuracion, Sesiones, Control)
├── Strings/          # Localization resources
└── Assets/           # App icons and images
```

## Notes

- The app uses a **dark theme** with teal accent colors.
- Mock data is pre-loaded in ViewModels for UI demonstration.
- No real telemetry or AI integration — UI shell only.
