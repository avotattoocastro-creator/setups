using Microsoft.UI.Xaml.Controls;
using AvoPerformanceSetupAI.ViewModels;

namespace AvoPerformanceSetupAI.Views;

public sealed partial class SetupDiffPage : Page
{
    public SetupDiffViewModel ViewModel { get; } = new SetupDiffViewModel();

    public SetupDiffPage()
    {
        this.InitializeComponent();
    }
}
