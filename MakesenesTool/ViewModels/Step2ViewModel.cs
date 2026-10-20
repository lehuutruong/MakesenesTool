using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
// Using System.Windows.Forms types via full qualification to avoid ambiguous references with WPF types.

namespace MakesenesTool.ViewModels;

/// <summary>Step 2: Running Settings (report path and run mode).</summary>
public partial class Step2ViewModel : ObservableObject
{
    /// <summary>Shared wizard values.</summary>
    public WizardState State { get; }

    /// <param name="state">Shared wizard values.</param>
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
        // Let user pick a folder; set ReportOutputPath to the selected folder (do not create a file)
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        var result = dialog.ShowDialog();
        if (result != System.Windows.Forms.DialogResult.OK && result != System.Windows.Forms.DialogResult.Yes)
            return;

        var selectedFolder = dialog.SelectedPath;
        State.ReportOutputPath = selectedFolder;
        State.ReportPathOverridden = true;
    }
}
