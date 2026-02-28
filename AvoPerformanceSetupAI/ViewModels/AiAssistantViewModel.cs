using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.ViewModels;

/// <summary>
/// ViewModel for the AI Assistant tab.
/// Combines NLP intent detection, the MLP neural network scorer, and
/// the chat-history display to let users control setup adjustments with
/// free-form Spanish or English text commands.
/// </summary>
public partial class AiAssistantViewModel : ObservableObject
{
    // Shared neural-network optimizer (persists across queries within a session)
    private readonly MlSetupOptimizer _optimizer = new();

    // Path where trained weights are stored between sessions
    private static readonly string WeightsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AvoPerformanceSetupAI",
        "ml_weights.bin");

    // ── Bindable properties ───────────────────────────────────────────────────

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "● LISTO — escribe un comando y pulsa Enviar";

    /// <summary>Conversation history shown in the chat panel.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>AI-generated proposals based on the latest NLP query.</summary>
    public ObservableCollection<Proposal> AiProposals { get; } = new();

    public AiAssistantViewModel()
    {
        // Restore previously trained weights (if available)
        try
        {
            if (File.Exists(WeightsPath))
            {
                _optimizer.LoadWeights(WeightsPath);
                AppLogger.Instance.Ai("Pesos de la red neuronal restaurados desde sesión anterior.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"No se pudieron cargar los pesos guardados: {ex.Message}");
        }

        // Welcome message
        AddAssistantMessage(
            "👋 Hola. Soy tu asistente de setup IA.\n" +
            "Cuéntame qué problema tienes con el coche, por ejemplo:\n" +
            "  • \"El coche sobrevira mucho en las curvas lentas\"\n" +
            "  • \"Necesito más agarre mecánico\"\n" +
            "  • \"Optimiza el setup para lluvia\"\n" +
            "  • \"Analiza el setup actual\"\n\n" +
            "Usaré procesamiento del lenguaje natural y la red neuronal para generarte propuestas.\n" +
            "Si tienes un archivo de setup cargado las propuestas incluirán valores concretos;\n" +
            "si no, recibirás recomendaciones generales que puedes aplicar manualmente.");
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Processes the user's natural-language input.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Add user message to chat
        Messages.Add(new ChatMessage { Role = MessageRole.User, Text = text });
        UserInput = string.Empty;
        IsProcessing = true;
        StatusText = "● PROCESANDO...";
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            ProcessQuery(text);
        }
        finally
        {
            IsProcessing = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSend() => !IsProcessing && !string.IsNullOrWhiteSpace(UserInput);

    partial void OnUserInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    /// <summary>Clears the conversation history and proposals.</summary>
    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        AiProposals.Clear();
        StatusText = "● LISTO — escribe un comando y pulsa Enviar";
        AppLogger.Instance.Info("Chat IA limpiado por el usuario.");
    }

    /// <summary>
    /// Positive reinforcement: tells the neural network this proposal set was useful.
    /// </summary>
    [RelayCommand]
    private void AcceptProposals()
    {
        TrainOnCurrentProposals(label: 1.0);
        SaveOptimizer();
        AddAssistantMessage("✅ Gracias por el feedback positivo. La red neuronal ha actualizado sus pesos.");
        AppLogger.Instance.Ai("Retroalimentación positiva aplicada al optimizador ML.");
    }

    /// <summary>
    /// Negative reinforcement: tells the neural network these proposals were unhelpful.
    /// </summary>
    [RelayCommand]
    private void RejectProposals()
    {
        TrainOnCurrentProposals(label: 0.0);
        SaveOptimizer();
        AddAssistantMessage("❌ Entendido. La red neuronal ha aprendido de este rechazo y ajustará futuras propuestas.");
        AppLogger.Instance.Ai("Retroalimentación negativa aplicada al optimizador ML.");
    }

    // ── Core NLP + ML logic ───────────────────────────────────────────────────

    private void ProcessQuery(string userText)
    {
        // Step 1 — NLP: detect intent via bag-of-words cosine similarity
        var (intent, confidence) = NlpService.DetectIntent(userText);
        var intentDescription    = NlpService.DescribeIntent(intent, confidence);

        AppLogger.Instance.Ai($"NLP → intención: '{intent}' | confianza: {confidence:P0}");

        // Step 2 — Load INI entries from the currently selected setup file (if any)
        var setupPath = SetupSettings.Instance.CurrentSetupPath;
        var iniEntries = new System.Collections.Generic.List<IniEntry>();

        if (!string.IsNullOrEmpty(setupPath) && File.Exists(setupPath))
        {
            try
            {
                iniEntries = SetupIniParser.Parse(setupPath);
                AppLogger.Instance.Data($"Setup cargado para análisis NLP: {Path.GetFileName(setupPath)}");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"Error al leer el setup: {ex.Message}");
            }
        }

        // Step 3 — Neural network: generate and score proposals
        var proposals = NlpService.GetProposals(intent, iniEntries, _optimizer);

        AppLogger.Instance.Ai($"Red neuronal: {proposals.Count} propuesta(s) generada(s) para intención '{intent}'.");

        // Step 4 — Update observable proposals collection
        AiProposals.Clear();
        foreach (var p in proposals)
            AiProposals.Add(p);

        // Step 5 — Build reply message
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(intentDescription);

        if (proposals.Count == 0)
        {
            sb.AppendLine("\nℹ️ No se encontraron parámetros numéricos en el setup activo para el análisis. " +
                          "Selecciona un archivo de setup en la pestaña Sesiones.");
        }
        else
        {
            sb.AppendLine($"\n🔧 {proposals.Count} propuesta(s) generada(s):");
            foreach (var p in proposals.Take(3))
            {
                if (p.From == "—")
                    sb.AppendLine($"  • [{p.Section}] {p.Parameter} — {p.Reason}");
                else
                    sb.AppendLine($"  • [{p.Section}] {p.Parameter}: {p.From} → {p.To}  (Δ {p.Delta})");
            }
            if (proposals.Count > 3)
                sb.AppendLine($"  … y {proposals.Count - 3} propuesta(s) más en el panel de la derecha.");
            sb.AppendLine("\nUsa ✅ / ❌ para entrenar la red neuronal con tu feedback.");
        }

        AddAssistantMessage(sb.ToString().TrimEnd());
        StatusText = $"● {proposals.Count} propuesta(s) | intención: {intent}";
    }

    private void TrainOnCurrentProposals(double label)
    {
        // Retrieve the setup path used for the last query to rebuild features
        var setupPath = SetupSettings.Instance.CurrentSetupPath;
        if (string.IsNullOrEmpty(setupPath) || !File.Exists(setupPath)) return;

        try
        {
            var iniEntries = SetupIniParser.Parse(setupPath);
            foreach (var proposal in AiProposals)
            {
                if (proposal.From == "—") continue;
                var entry = iniEntries.FirstOrDefault(e =>
                    e.Section.Equals(proposal.Section, StringComparison.OrdinalIgnoreCase) &&
                    e.Key.Equals(proposal.Parameter, StringComparison.OrdinalIgnoreCase));

                if (entry is null) continue;

                double deltaFactor = 0;
                if (double.TryParse(proposal.Delta.TrimStart('+'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double nudge) &&
                    double.TryParse(proposal.From,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double fromVal) &&
                    fromVal != 0)
                {
                    deltaFactor = nudge / fromVal;
                }

                double[] features = NlpService.BuildFeatures(entry, deltaFactor);
                _optimizer.Train(features, label);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Error al entrenar el optimizador: {ex.Message}");
        }
    }

    private void AddAssistantMessage(string text) =>
        Messages.Add(new ChatMessage { Role = MessageRole.Assistant, Text = text });

    /// <summary>Persists the neural-network weights to disk so they survive across sessions.</summary>
    private void SaveOptimizer()
    {
        try
        {
            var dir = Path.GetDirectoryName(WeightsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _optimizer.SaveWeights(WeightsPath);
            AppLogger.Instance.Ai("Pesos de la red neuronal guardados.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"No se pudieron guardar los pesos: {ex.Message}");
        }
    }
}
