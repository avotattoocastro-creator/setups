using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvoPerformanceSetupAI.Models;

namespace AvoPerformanceSetupAI.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    // ── Config fields ────────────────────────────────────────────────────────
    [ObservableProperty] private string _carId = "ks_porsche_911_gt3_r";
    [ObservableProperty] private string _trackId = "monza";
    [ObservableProperty] private string _setupSource = "Local File";
    [ObservableProperty] private string _mode = "Hotlap";

    // ── Status ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = "● READY";
    [ObservableProperty] private string _brainInfo = "Python | latency: 12 ms";
    [ObservableProperty] private string _riskLevel = "LOW";
    [ObservableProperty] private bool _isRunning;

    // ── Collections ──────────────────────────────────────────────────────────
    public ObservableCollection<SetupIteration> Iterations { get; } = new();
    public ObservableCollection<Proposal> LastProposals { get; } = new();
    public ObservableCollection<string> SetupSources { get; } = new() { "Local File", "Server", "Git Repo" };
    public ObservableCollection<string> Modes { get; } = new() { "Hotlap", "Race", "Qualify" };

    public SessionsViewModel()
    {
        LoadMockData();
    }

    private void LoadMockData()
    {
        Iterations.Add(new SetupIteration { Setup = "baseline_monza_911.ini", BestLap = "1:45.321", Iter = 0, Exported = true });
        Iterations.Add(new SetupIteration { Setup = "iter_001_hotlap.ini",     BestLap = "1:44.987", Iter = 1, Exported = true });
        Iterations.Add(new SetupIteration { Setup = "iter_002_hotlap.ini",     BestLap = "1:44.512", Iter = 2, Exported = false });
        Iterations.Add(new SetupIteration { Setup = "iter_003_hotlap.ini",     BestLap = "1:44.201", Iter = 3, Exported = false, IsSelected = true });
        Iterations.Add(new SetupIteration { Setup = "iter_004_hotlap.ini",     BestLap = "1:44.890", Iter = 4, Exported = false });

        LastProposals.Add(new Proposal { Parameter = "FrontSuspension",  From = "4.2",  To = "3.8",  Delta = "-0.4" });
        LastProposals.Add(new Proposal { Parameter = "RearAntiRollBar",  From = "6",    To = "7",    Delta = "+1"   });
        LastProposals.Add(new Proposal { Parameter = "BrakeBias",        From = "56.0", To = "55.5", Delta = "-0.5" });
        LastProposals.Add(new Proposal { Parameter = "FrontTyrePressure",From = "27.5", To = "27.2", Delta = "-0.3" });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        IsRunning = true;
        StatusText = "● RUNNING";
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsRunning = false;
        StatusText = "● READY";
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void Apply()
    {
        StatusText = "● APPLYING...";
    }

    [RelayCommand]
    private void ApplyProposal()
    {
        StatusText = "● PROPOSAL APPLIED";
    }

    [RelayCommand]
    private void Rollback()
    {
        StatusText = "● ROLLED BACK";
    }

    [RelayCommand]
    private void Connect()
    {
        StatusText = "● CONNECTED";
    }
}
