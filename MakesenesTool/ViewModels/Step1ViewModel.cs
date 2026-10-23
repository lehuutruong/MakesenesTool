using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MakesenesTool.ViewModels;

public partial class Step1ViewModel : ObservableObject
{
    public WizardState State { get; }

    public Step1ViewModel(WizardState state)
    {
        State = state;
    }

    [RelayCommand]
    private void BrowseSource()
    {
        using var dialog = new FolderBrowserDialog();
        var result = dialog.ShowDialog();
        if (result == DialogResult.OK)
            State.SourceFilePath = dialog.SelectedPath;
    }
}
