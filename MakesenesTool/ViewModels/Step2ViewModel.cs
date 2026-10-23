using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace MakesenesTool.ViewModels;

public partial class Step2ViewModel : ObservableObject
{
    public WizardState State { get; }

    public Step2ViewModel(WizardState state)
    {
        State = state;
    }

    [RelayCommand]
    private void SelectValidate()
    {
        State.SelectedMode = MigrationCommon.Models.RunMode.ValidateOnly;
    }

    [RelayCommand]
    private void SelectCalculate()
    {
        State.SelectedMode = MigrationCommon.Models.RunMode.ValidateAndImport;
    }

    [RelayCommand]
    private void BrowseReport()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        var result = dialog.ShowDialog();
        if (result != System.Windows.Forms.DialogResult.OK && result != System.Windows.Forms.DialogResult.Yes)
            return;

        var selectedFolder = dialog.SelectedPath;
        State.ReportOutputPath = selectedFolder;
        State.ReportPathOverridden = true;
    }
}
