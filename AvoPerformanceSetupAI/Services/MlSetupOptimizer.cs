using System;

namespace AvoPerformanceSetupAI.Services;

/// <summary>
/// A 2-layer feedforward neural network (multi-layer perceptron) that scores setup proposals.
/// <para>Architecture: 4 inputs → 8 hidden neurons (ReLU) → 1 output (Sigmoid).</para>
/// <para>
/// Weights are updated online via stochastic gradient descent (SGD) with binary cross-entropy
/// loss whenever the user accepts or rejects a proposal, allowing the network to learn individual
/// driving preferences over time.
/// </para>
/// </summary>
public sealed class MlSetupOptimizer
{
    private const int    InputSize    = 4;
    private const int    HiddenSize   = 8;
    private const double LearningRate = 0.01;

    // Layer 1 weights: W1 [HiddenSize × InputSize], biases b1 [HiddenSize]
    private readonly double[,] _w1 = new double[HiddenSize, InputSize];
    private readonly double[]  _b1 = new double[HiddenSize];

    // Layer 2 weights: W2 [HiddenSize], scalar bias b2
    private readonly double[] _w2 = new double[HiddenSize];
    private double _b2;

    // Cached activations for backpropagation
    private double[] _lastHidden = new double[HiddenSize];
    private double[] _lastInput  = new double[InputSize];

    public MlSetupOptimizer()
    {
        InitializeWeights();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Forward pass through the network.
    /// Returns a score in (0, 1): higher value means the network predicts that adjusting
    /// this parameter will improve lap performance.
    /// </summary>
    /// <param name="features">
    /// A 4-element feature vector:
    /// [0] normalised parameter value (tanh), [1] section hash, [2] key hash, [3] delta direction.
    /// </param>
    public double Score(double[] features)
    {
        _lastInput = (double[])features.Clone();
        _lastHidden = ForwardHidden(features);
        return Sigmoid(DotProduct(_w2, _lastHidden) + _b2);
    }

    /// <summary>
    /// Online SGD update using binary cross-entropy loss.
    /// Call with <paramref name="label"/> = 1.0 when the user accepts a proposal
    /// or 0.0 when the user rejects it.
    /// </summary>
    public void Train(double[] features, double label)
    {
        double score   = Score(features);          // forward pass
        double dOutput = score - label;            // dL/dz2 (BCE + sigmoid derivative)

        // Gradient for W2 and b2
        for (int j = 0; j < HiddenSize; j++)
            _w2[j] -= LearningRate * dOutput * _lastHidden[j];
        _b2 -= LearningRate * dOutput;

        // Backpropagation into hidden layer (ReLU gate)
        for (int j = 0; j < HiddenSize; j++)
        {
            double dHidden = dOutput * _w2[j] * (_lastHidden[j] > 0 ? 1.0 : 0.0);
            for (int i = 0; i < InputSize; i++)
                _w1[j, i] -= LearningRate * dHidden * _lastInput[i];
            _b1[j] -= LearningRate * dHidden;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Xavier / Glorot initialization for stable gradient flow at startup.</summary>
    private void InitializeWeights()
    {
        var rng    = new Random(42);
        double s1  = Math.Sqrt(2.0 / InputSize);
        double s2  = Math.Sqrt(2.0 / HiddenSize);

        for (int j = 0; j < HiddenSize; j++)
        {
            for (int i = 0; i < InputSize; i++)
                _w1[j, i] = (rng.NextDouble() * 2 - 1) * s1;
            _b1[j] = 0.0;
        }
        for (int j = 0; j < HiddenSize; j++)
            _w2[j] = (rng.NextDouble() * 2 - 1) * s2;
        _b2 = 0.0;
    }

    private double[] ForwardHidden(double[] x)
    {
        var h = new double[HiddenSize];
        for (int j = 0; j < HiddenSize; j++)
        {
            double sum = _b1[j];
            for (int i = 0; i < InputSize; i++)
                sum += _w1[j, i] * x[i];
            h[j] = Math.Max(0, sum);   // ReLU activation
        }
        return h;
    }

    private static double DotProduct(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

    // ── Weight persistence ────────────────────────────────────────────────────

    /// <summary>
    /// Saves all network weights and biases to a binary file so the trained
    /// model can be reloaded in a future session.
    /// </summary>
    public void SaveWeights(string path)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        using var bw = new System.IO.BinaryWriter(fs);
        for (int j = 0; j < HiddenSize; j++)
            for (int i = 0; i < InputSize; i++)
                bw.Write(_w1[j, i]);
        for (int j = 0; j < HiddenSize; j++)
            bw.Write(_b1[j]);
        for (int j = 0; j < HiddenSize; j++)
            bw.Write(_w2[j]);
        bw.Write(_b2);
    }

    /// <summary>
    /// Loads network weights and biases from a binary file previously created
    /// by <see cref="SaveWeights"/>.
    /// Throws <see cref="InvalidDataException"/> when the file size does not match
    /// the expected layout, which indicates a truncated or incompatible weight file.
    /// </summary>
    public void LoadWeights(string path)
    {
        // Each weight/bias is stored as a IEEE-754 double (8 bytes).
        // Layout: W1 [HiddenSize × InputSize] | b1 [HiddenSize] | W2 [HiddenSize] | b2 [1]
        const int ExpectedBytes = (HiddenSize * InputSize + HiddenSize + HiddenSize + 1) * sizeof(double);
        var info = new System.IO.FileInfo(path);
        if (info.Length != ExpectedBytes)
            throw new InvalidDataException(
                $"Archivo de pesos incompatible ({info.Length} bytes; esperado {ExpectedBytes}). " +
                "El archivo puede estar corrupto o pertenecer a una versión anterior.");

        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
        using var br = new System.IO.BinaryReader(fs);
        for (int j = 0; j < HiddenSize; j++)
            for (int i = 0; i < InputSize; i++)
                _w1[j, i] = br.ReadDouble();
        for (int j = 0; j < HiddenSize; j++)
            _b1[j] = br.ReadDouble();
        for (int j = 0; j < HiddenSize; j++)
            _w2[j] = br.ReadDouble();
        _b2 = br.ReadDouble();
    }
}
