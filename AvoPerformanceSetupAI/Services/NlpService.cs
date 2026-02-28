using System;
using System.Collections.Generic;
using System.Linq;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// Natural language processing service for motorsport setup management.
/// <para>
/// Detects the user's intent from free-form text (Spanish or English) using a
/// bag-of-words TF-IDF cosine similarity approach against a curated motorsport
/// vocabulary, then maps the detected intent to concrete setup recommendations.
/// </para>
/// </summary>
public static class NlpService
{
    // ── Intent keyword vocabulary (Spanish + English) ─────────────────────────

    private static readonly Dictionary<string, string[]> IntentKeywords =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["oversteer_fix"]   = ["oversteer", "sobreviraje", "snap", "spin", "loose",
                               "rotate", "gira", "demasiado", "cola", "rear", "trasera",
                               "oversteers", "spins"],
        ["understeer_fix"]  = ["understeer", "subviraje", "push", "plow", "empuja",
                               "no gira", "no rota", "frontal", "nose", "morro", "wash",
                               "front", "delantera", "pushes"],
        ["stability"]       = ["stability", "estabilidad", "stable", "estable",
                               "consistent", "consistente", "predictable", "predecible",
                               "nervous", "nervioso", "jittery"],
        ["downforce"]       = ["downforce", "aerodynamic", "aero", "carga",
                               "aerodinamica", "wing", "aleron", "drag", "resistencia",
                               "high speed", "alta velocidad"],
        ["mechanical_grip"] = ["mechanical", "grip", "traccion", "traction",
                               "agarre", "slow speed", "baja velocidad", "curva lenta",
                               "low speed", "aceleracion", "acceleration"],
        ["balance"]         = ["balance", "balanceo", "equilibrio", "neutral",
                               "front rear", "delantera trasera", "50 50", "balanced",
                               "balanced setup", "equilibrado"],
        ["wet_setup"]       = ["wet", "lluvia", "rain", "aquaplaning", "mojado",
                               "humid", "humedo", "slippery", "resbaladizo",
                               "wet conditions", "condiciones lluvia"],
        ["qualify"]         = ["qualify", "clasificacion", "qualifying",
                               "hotlap", "vuelta rapida", "lap time", "vuelta",
                               "one lap", "single lap", "pole"],
        ["race"]            = ["race", "carrera", "degradation", "degradacion",
                               "tire wear", "desgaste", "consumption", "consumo",
                               "long run", "stint", "race pace"],
        ["analyze"]         = ["analyze", "analyse", "analisis", "analiza",
                               "review", "revisar", "check", "comprobar",
                               "show", "muestra", "ver", "que pasa", "what"],
    };

    // ── Intent → parameter recommendation rules ───────────────────────────────

    /// <summary>
    /// A recommended adjustment: which section/parameter to change, by what
    /// proportional delta, and why.
    /// </summary>
    public record IntentRecommendation(
        string Section,
        string Parameter,
        double DeltaFactor,
        string Reason);

    private static readonly Dictionary<string, List<IntentRecommendation>> IntentActions = new()
    {
        ["oversteer_fix"] =
        [
            new("REAR",      "TOE",      +0.005, "Toe trasero positivo reduce sobreviraje en aceleración"),
            new("REAR",      "CAMBER",   -0.020, "Reducir camber trasero mejora tracción y reduce sobreviraje"),
            new("ARB",       "REAR",     -0.030, "Barra anti-roll trasera más suave controla el sobreviraje"),
        ],
        ["understeer_fix"] =
        [
            new("FRONT",     "TOE",      -0.005, "Toe delantero negativo mejora la respuesta de dirección"),
            new("FRONT",     "CAMBER",   -0.020, "Mayor camber delantero aumenta el grip en curva"),
            new("ARB",       "FRONT",    -0.030, "Barra anti-roll delantera más suave reduce el subviraje"),
        ],
        ["stability"] =
        [
            new("AERO",      "REAR_WING",  +0.030, "Mayor carga trasera aporta estabilidad a alta velocidad"),
            new("ALIGNMENT", "TOE_REAR",   +0.004, "Toe trasero positivo da estabilidad en recta"),
            new("SUSPENSION","REAR",       +0.020, "Suspensión trasera más firme mejora la consistencia"),
        ],
        ["downforce"] =
        [
            new("AERO",      "REAR_WING",  +0.050, "Aumentar ángulo del alerón trasero incrementa downforce"),
            new("AERO",      "FRONT_WING", +0.050, "Aumentar ángulo del alerón delantero equilibra la carga"),
        ],
        ["mechanical_grip"] =
        [
            new("SPRINGS",   "FRONT",     -0.040, "Muelles delanteros más blandos mejoran el grip mecánico"),
            new("DAMPERS",   "BUMP",      -0.030, "Reducir amortiguación bump suaviza la absorción de impactos"),
            new("TYRES",     "PRESSURE_LF",-0.020,"Menor presión del neumático amplía la huella de contacto"),
        ],
        ["balance"] =
        [
            new("ALIGNMENT", "CAMBER_FRONT",-0.010,"Ajuste de camber delantero para equilibrar el balance"),
            new("BRAKE",     "BALANCE",      0.000,"Revisar el balance de frenos delantero/trasero"),
        ],
        ["wet_setup"] =
        [
            new("TYRES",     "PRESSURE_LF", -0.030,"Menor presión amplía la huella de contacto en mojado"),
            new("TYRES",     "PRESSURE_RF", -0.030,"Menor presión en neumático derecho para lluvia"),
            new("AERO",      "REAR_WING",   +0.050,"Mayor downforce da estabilidad aerodinámica en lluvia"),
            new("SUSPENSION","FRONT",       -0.030,"Suspensión más suave absorbe mejor la pista mojada"),
        ],
        ["qualify"] =
        [
            new("TYRES",     "PRESSURE_LF", +0.020,"Mayor presión para respuesta más directa en vuelta rápida"),
            new("AERO",      "REAR_WING",   -0.040,"Menor resistencia aerodinámica para mayor velocidad punta"),
            new("SUSPENSION","FRONT",       +0.030,"Suspensión dura para mejor aprovechamiento aerodinámico"),
        ],
        ["race"] =
        [
            new("TYRES",     "PRESSURE_LF", -0.015,"Menor presión reduce la degradación del neumático en carrera"),
            new("SUSPENSION","REAR",        -0.020,"Suspensión trasera suave mejora el desgaste en el stint"),
            new("FUEL",      "FUEL",         0.000,"Revisar cantidad de combustible para la estrategia"),
        ],
        ["analyze"] = [],   // handled dynamically by the optimizer
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects the best-matching intent for <paramref name="userText"/> using
    /// bag-of-words cosine similarity against the motorsport intent vocabulary.
    /// </summary>
    /// <returns>
    /// A tuple of (intent key, confidence score ∈ [0, 1]).
    /// Falls back to <c>"analyze"</c> when no clear intent is detected.
    /// </returns>
    public static (string Intent, double Confidence) DetectIntent(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return ("analyze", 0.0);

        var tokens = Tokenize(userText);
        if (tokens.Count == 0)
            return ("analyze", 0.0);

        double bestScore  = -1;
        string bestIntent = "analyze";

        foreach (var (intent, keywords) in IntentKeywords)
        {
            double score = CosineSimilarity(tokens, keywords);
            if (score > bestScore)
            {
                bestScore  = score;
                bestIntent = intent;
            }
        }

        return (bestIntent, bestScore);
    }

    /// <summary>
    /// Returns a plain-language description of the detected intent for display in the chat.
    /// </summary>
    public static string DescribeIntent(string intent, double confidence) => intent switch
    {
        "oversteer_fix"   => $"🔄 Intención detectada: Corregir sobreviraje (confianza: {confidence:P0})",
        "understeer_fix"  => $"🔄 Intención detectada: Corregir subviraje (confianza: {confidence:P0})",
        "stability"       => $"⚖️ Intención detectada: Mejorar estabilidad (confianza: {confidence:P0})",
        "downforce"       => $"🌬️ Intención detectada: Ajustar carga aerodinámica (confianza: {confidence:P0})",
        "mechanical_grip" => $"🔧 Intención detectada: Mejorar grip mecánico (confianza: {confidence:P0})",
        "balance"         => $"⚖️ Intención detectada: Equilibrar el balance (confianza: {confidence:P0})",
        "wet_setup"       => $"🌧️ Intención detectada: Optimizar para lluvia (confianza: {confidence:P0})",
        "qualify"         => $"🏎️ Intención detectada: Vuelta rápida / Clasificación (confianza: {confidence:P0})",
        "race"            => $"🏁 Intención detectada: Configuración de carrera (confianza: {confidence:P0})",
        _                 => $"🔍 Intención detectada: Análisis general del setup (confianza: {confidence:P0})",
    };

    /// <summary>
    /// Generates a list of setup proposals for the given intent, optionally constrained to the
    /// parameters that are actually present in <paramref name="iniEntries"/>.
    /// The <paramref name="optimizer"/> is used to score and fine-tune each proposal delta.
    /// </summary>
    public static List<Proposal> GetProposals(
        string intent,
        IReadOnlyList<IniEntry> iniEntries,
        MlSetupOptimizer optimizer)
    {
        var proposals = new List<Proposal>();

        if (!IntentActions.TryGetValue(intent, out var recommendations) ||
            recommendations.Count == 0)
        {
            // For "analyze" (or unknown) intent, let the neural network rank all parameters
            return GenerateAnalysisProposals(iniEntries, optimizer);
        }

        foreach (var rec in recommendations)
        {
            if (rec.DeltaFactor == 0.0)
            {
                // Informational recommendation — no numeric change to apply
                proposals.Add(new Proposal
                {
                    Section   = rec.Section,
                    Parameter = rec.Parameter,
                    From      = "—",
                    To        = "—",
                    Delta     = "—",
                    Reason    = rec.Reason,
                });
                continue;
            }

            // Find the parameter in the loaded INI entries
            var entry = iniEntries.FirstOrDefault(e =>
                e.Section.Equals(rec.Section, StringComparison.OrdinalIgnoreCase) &&
                e.Key.Equals(rec.Parameter, StringComparison.OrdinalIgnoreCase));

            if (entry is null) continue;

            if (!double.TryParse(entry.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double current))
                continue;

            // Build feature vector and ask the neural network to score this adjustment
            double[] features  = BuildFeatures(entry, rec.DeltaFactor);
            double   mlScore   = optimizer.Score(features);       // ∈ (0, 1)
            double   scaledFactor = rec.DeltaFactor * (0.9 + mlScore * 0.2); // ±10 % ML modulation
            double   nudge     = current * scaledFactor;
            double   proposed  = Math.Round(current + nudge, 4);
            string   deltaStr  = nudge >= 0
                ? $"+{nudge.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}"
                : $"{nudge.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";

            proposals.Add(new Proposal
            {
                Section   = rec.Section,
                Parameter = rec.Parameter,
                From      = entry.Value,
                To        = proposed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Delta     = deltaStr,
                Reason    = rec.Reason,
            });
        }

        return proposals;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// When the intent is "analyze", use the neural network to score every numeric parameter
    /// and return the top-ranked candidates.
    /// </summary>
    private static List<Proposal> GenerateAnalysisProposals(
        IReadOnlyList<IniEntry> iniEntries, MlSetupOptimizer optimizer)
    {
        var proposals = new List<Proposal>();

        var numericEntries = iniEntries
            .Where(e => double.TryParse(e.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            .ToList();

        // Score every parameter and take the top 5
        var scored = numericEntries
            .Select(e => (Entry: e, Score: optimizer.Score(BuildFeatures(e, 0))))
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();

        foreach (var (entry, score) in scored)
        {
            double.TryParse(entry.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val);

            double nudge    = Math.Round(val * 0.02, 4);
            double proposed = Math.Round(val - nudge, 4);

            proposals.Add(new Proposal
            {
                Section   = entry.Section,
                Parameter = entry.Key,
                From      = entry.Value,
                To        = proposed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Delta     = $"-{nudge.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                Reason    = $"Red neuronal: parámetro candidato a optimización (puntuación: {score:F2})",
            });
        }

        return proposals;
    }

    /// <summary>
    /// Builds a 4-element feature vector for a given INI entry and adjustment direction.
    /// </summary>
    internal static double[] BuildFeatures(IniEntry entry, double direction)
    {
        double.TryParse(entry.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double val);

        double normalizedVal  = Math.Tanh(val / 100.0);                        // map to (-1, 1)
        double sectionFeature = (Math.Abs(entry.Section.GetHashCode()) % 256) / 255.0;
        double keyFeature     = (Math.Abs(entry.Key.GetHashCode())     % 256) / 255.0;
        double dir            = Math.Clamp(direction, -1.0, 1.0);

        return [normalizedVal, sectionFeature, keyFeature, dir];
    }

    /// <summary>Lowercases and tokenises text into a set of words.</summary>
    private static HashSet<string> Tokenize(string text)
    {
        var tokens  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new System.Text.StringBuilder();

        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
                current.Append(c);
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>
    /// Binary bag-of-words cosine similarity between the query token set
    /// and an array of keyword strings.
    /// </summary>
    private static double CosineSimilarity(HashSet<string> queryTokens, string[] keywords)
    {
        int intersection = keywords.Count(kw =>
            queryTokens.Contains(kw) ||
            queryTokens.Any(t =>
                t.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                kw.Contains(t,  StringComparison.OrdinalIgnoreCase)));

        if (intersection == 0) return 0.0;
        return intersection / Math.Sqrt((double)queryTokens.Count * keywords.Length);
    }
}
