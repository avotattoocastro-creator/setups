# AvoPerformanceSetupAI

A WinUI 3 (Windows App SDK) desktop application for AI-assisted motorsport setup management.

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
- **NLP**: Bag-of-words TF-IDF cosine similarity for intent detection (Spanish + English)
- **Machine Learning**: Feedforward neural network (MLP) with backpropagation for proposal scoring
  and online training from user feedback

## AI Features

The **🤖 IA Asistente** tab provides a chat-style interface powered by:

### Natural Language Processing (NLP)
- Detects the user's intent from free-form text in **Spanish or English**
- Supported intents: `oversteer_fix`, `understeer_fix`, `stability`, `downforce`,
  `mechanical_grip`, `balance`, `wet_setup`, `qualify`, `race`, `analyze`
- Uses bag-of-words tokenisation with TF-IDF cosine similarity against a curated
  motorsport vocabulary

### Neural Network (MLP)
- **Architecture**: 4 inputs → 8 hidden neurons (ReLU) → 1 output (Sigmoid)
- **Initialization**: Xavier / Glorot weights for stable gradient flow
- **Training**: Online stochastic gradient descent (SGD) with binary cross-entropy loss
- **Features**: normalised parameter value, section hash, key hash, delta direction
- The network learns individual driver preferences when you press ✅ / ❌ after a suggestion

### How to use
1. Select a car, track, and setup file in the **Sesiones** tab (optional but recommended)
2. Switch to **🤖 IA Asistente**
3. Type a natural-language command, for example:
   - *"El coche sobrevirá en las curvas lentas"*
   - *"Necesito más agarre mecánico"*
   - *"Optimiza el setup para lluvia"*
   - *"Analiza el setup actual"*
4. Review the generated proposals in the right panel
5. Press **✅ Útiles** or **❌ No útiles** to train the neural network with your feedback

## Localization

Resource files are in `AvoPerformanceSetupAI/Strings/`:
- `en-US/Resources.resw` — English strings
- `es-ES/Resources.resw` — Spanish strings

## Project Structure

```
AvoPerformanceSetupAI/
├── Models/           # Data models (SetupIteration, Proposal, ChatMessage, LogEntry)
├── ViewModels/       # MVVM ViewModels (MainViewModel, SessionsViewModel, TerminalViewModel, AiAssistantViewModel)
├── Views/            # Tab pages (Configuracion, Sesiones, Control, Terminal, AiAssistantPage)
├── Services/         # Backend services (AppLogger, SetupIniParser, SetupSettings, NlpService, MlSetupOptimizer)
├── Strings/          # Localization resources
└── Assets/           # App icons and images
```

## Notes

- The app uses a **dark theme** with teal accent colors.
- The **AI Assistant** tab uses NLP + a feedforward neural network trained online from user feedback.
- The neural network learns within the session; weights are not persisted between runs (by design for v1).
