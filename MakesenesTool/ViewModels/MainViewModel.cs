using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MakesenesTool.Models;
using MakesenesTool.Services;
using System.IO;
using MigrationCommon.Abstractions;

namespace MakesenesTool.ViewModels;

/// <summary>Owns the wizard: which step is shown, navigation and the header stepper state.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly WizardState _state;
    private readonly Step1ViewModel _step1;
    private readonly Step2ViewModel _step2;
    private readonly IServiceProvider _services;
    private Step3ViewModel? _step3;

    /// <summary>The step content currently shown.</summary>
    [ObservableProperty]
    private ObservableObject _currentView;

    /// <summary>Current step number: 1, 2 or 3.</summary>
    [ObservableProperty]
    private int _currentStep = 1;

    /// <param name="state">Shared wizard values.</param>
    /// <param name="step1">Step 1 view model.</param>
    /// <param name="step2">Step 2 view model.</param>
    /// <param name="services">Used to resolve keyed handlers and Step 3 at run time.</param>
    public MainViewModel(WizardState state, Step1ViewModel step1, Step2ViewModel step2, IServiceProvider services)
    {
        _state = state;
        _step1 = step1;
        _step2 = step2;
        _services = services;
        _currentView = step1;
        _state.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>Stepper state of step 1.</summary>
    public StepState Step1State => StateOf(1);

    /// <summary>Stepper state of step 2.</summary>
    public StepState Step2State => StateOf(2);

    /// <summary>Stepper state of step 3.</summary>
    public StepState Step3State => StateOf(3);

    /// <summary>Back and Next are hidden on the results step.</summary>
    public bool ShowNavigation => CurrentStep < 3;

    /// <summary>Back is available on step 2.</summary>
    public bool CanGoBack => CurrentStep == 2;

    /// <summary>Next is enabled once the current step is complete.</summary>
    public bool CanGoNext => CurrentStep switch
    {
        1 => _state.Step1Complete,
        // On step 2 allow Next when a report output folder is set, or when a source folder and an action are selected
        2 => _state.Step2Complete || (!string.IsNullOrWhiteSpace(_state.SourceFilePath) && !string.IsNullOrWhiteSpace(_state.SelectedMode.ToString())),
        _ => false,
    };

    /// <summary>Label of the forward button: "Next" on step 1, "Run" on step 2.</summary>
    public string NextLabel => CurrentStep == 2 ? "Run" : "Next";

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(Step1State));
        OnPropertyChanged(nameof(Step2State));
        OnPropertyChanged(nameof(Step3State));
        OnPropertyChanged(nameof(ShowNavigation));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextLabel));
    }

    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
            GoToStep(1);
    }

    [RelayCommand]
    private async Task Next()
    {
        if (!CanGoNext)
            return;

        if (CurrentStep == 1)
            GoToStep(2);
        else
            await StartRunAsync();
    }

    /// <summary>Header home button: returns to step 1 and clears values. Ignored while a run is in progress.</summary>
    [RelayCommand]
    private void Home()
    {
        if (_step3 is { IsRunning: true })
            return;

        _state.Reset();
        _step3 = null;
        GoToStep(1);
    }

    private StepState StateOf(int step) =>
        step == CurrentStep ? StepState.Current : step < CurrentStep ? StepState.Done : StepState.Future;

    private void GoToStep(int step)
    {
        CurrentStep = step;
        CurrentView = step == 1 ? _step1 : _step2;
    }

    private async Task StartRunAsync()
    {
        // Prepare Step 3 view immediately so UI navigates while backup runs in background
        _step3 = _services.GetRequiredService<Step3ViewModel>();
        CurrentStep = 3;
        CurrentView = _step3;

        // Run backup and processing in background so UI remains responsive and navigation already happened
        _ = Task.Run(async () =>
        {
            string? dbPath = null;
            try
            {
                var backup = _services.GetService<BackupService>();
                if (backup is not null && !string.IsNullOrWhiteSpace(_state.SourceFilePath))
                {
                    // update status shown in Step3
                    _step3.StatusLabel = "Creating backup...";
                    dbPath = await backup.BackupFolderAsync(_state.SourceFilePath);
                }
            }
            catch (Exception ex)
            {
                // set status on step3 if available
                try { _step3.StatusLabel = $"Backup failed: {ex.Message}"; } catch { }
            }

            if (!string.IsNullOrWhiteSpace(dbPath))
            {
                var validate = _state.SelectedMode == MigrationCommon.Models.RunMode.ValidateOnly;
                var outputFolder = string.IsNullOrWhiteSpace(_state.ReportOutputPath) ? AppContext.BaseDirectory : _state.ReportOutputPath;
                await _step3.RunProcessorsAsync(dbPath, validate, outputFolder);
            }
            else
            {
                // fallback to existing handler run
                try
                {
                    var handler = _services.GetRequiredKeyedService<IMigrationHandler>(_state.SelectedCategory);
                    await _step3.RunAsync(handler, _state.BuildRunContext());
                }
                catch (Exception ex)
                {
                    try { _step3.StatusLabel = $"Run failed: {ex.Message}"; } catch { }
                }
            }
        });
    }
}
