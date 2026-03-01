using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvoPerformanceSetupAI.Models;
using AvoPerformanceSetupAI.Services;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class SetupDiffViewModel : ObservableObject
{
    private static readonly SetupDiffEngine DiffEngine = new();

    // ── File selection ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDiffCommand))]
    private string? _baselineFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDiffCommand))]
    private string? _candidateFile;

    // ── Results ───────────────────────────────────────────────────────────────

    public ObservableCollection<SetupDiffEntry> DiffEntries { get; } = new();
    public ObservableCollection<GroupSummaryRow> GroupSummaries { get; } = new();

    [ObservableProperty] private string _statusText = "Select two setup files and press Run Diff.";
    [ObservableProperty] private bool _isBusy;

    // ── File list (mirrors SessionsViewModel so the ComboBoxes stay in sync) ──

    /// <summary>Files available for selection — reflects the currently loaded car/track.</summary>
    public ObservableCollection<string> SetupFiles => SessionsViewModel.Shared.SetupFiles;

    // ── Command ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunDiff))]
    private async System.Threading.Tasks.Task RunDiffAsync()
    {
        DiffEntries.Clear();
        GroupSummaries.Clear();
        StatusText = "Loading…";
        IsBusy = true;

        try
        {
            var vm       = SessionsViewModel.Shared;
            var carId    = vm.CarId;
            var trackId  = vm.TrackId;
            var provider = vm.CreateProviderPublic();

            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(trackId))
            {
                StatusText = "Select a car and track first.";
                return;
            }

            // Load and parse both files in parallel
            var taskA = provider.ReadSetupTextAsync(carId, trackId, BaselineFile!);
            var taskB = provider.ReadSetupTextAsync(carId, trackId, CandidateFile!);
            await System.Threading.Tasks.Task.WhenAll(taskA, taskB);

            var aParams = ParseAndClassify(taskA.Result);
            var bParams = ParseAndClassify(taskB.Result);

            var diff = DiffEngine.Diff(aParams, bParams);

            foreach (var entry in diff)
                DiffEntries.Add(entry);

            foreach (var (group, count) in DiffEngine.GroupSummary(diff))
                GroupSummaries.Add(new GroupSummaryRow(group, count));

            StatusText = diff.Count == 0
                ? "No differences found."
                : $"{diff.Count} difference(s) — "
                  + $"{diff.Count(e => e.Kind == SetupDiffKind.Changed)} changed, "
                  + $"{diff.Count(e => e.Kind == SetupDiffKind.Added)} added, "
                  + $"{diff.Count(e => e.Kind == SetupDiffKind.Removed)} removed.";

            AppLogger.Instance.Data(
                $"Setup Diff: {BaselineFile} vs {CandidateFile} — {diff.Count} diff(s)");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            AppLogger.Instance.Error($"Setup Diff error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunDiff() =>
        !string.IsNullOrEmpty(BaselineFile) &&
        !string.IsNullOrEmpty(CandidateFile) &&
        BaselineFile != CandidateFile;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<SetupParameter> ParseAndClassify(string iniText)
    {
        var entries = SetupIniParser.ParseText(iniText);
        var parameters = entries
            .Select(SetupParameter.FromIniEntry)
            .Where(p => p is not null)
            .Cast<SetupParameter>()
            .ToList();
        SetupParameterClassifier.ClassifyAll(parameters);
        return parameters;
    }
}

/// <summary>One row in the group-summary panel.</summary>
public sealed record GroupSummaryRow(string Group, int Count)
{
    public string Display => $"{Group}: {Count}";
}
