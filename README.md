# AvoPerformanceSetupAI

A WinUI 3 (Windows App SDK) desktop application for AI-assisted motorsport setup management.

## Prerequisites

- **Windows 10/11** (version 1809 or later, 19041 recommended)
- **Visual Studio 2022** (version 17.8+)
  - Workload: **.NET Desktop Development**
  - Workload: **Windows Application Development** (includes Windows App SDK)
- **Windows App SDK 1.5** (installed automatically via NuGet)

## Opening the Solution

1. Clone or download this repository
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
