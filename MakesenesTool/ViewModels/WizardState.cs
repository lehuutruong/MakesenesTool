using CommunityToolkit.Mvvm.ComponentModel;
using MigrationCommon.Models;
using System.IO;

namespace MakesenesTool.ViewModels;

public partial class WizardState : ObservableObject
{
    private const string ReportSuffix = "_report";

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private string _reportOutputPath = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = string.Empty;

    [ObservableProperty]
    private RunMode _selectedMode = RunMode.ValidateOnly;

    public IReadOnlyList<string> Categories { get; } = ["Invoice", "CreditNote"];

    public bool ReportPathOverridden { get; set; }

    public bool Step1Complete => !string.IsNullOrWhiteSpace(SourceFilePath);

    public bool Step2Complete => !string.IsNullOrWhiteSpace(ReportOutputPath);

    partial void OnSourceFilePathChanged(string value)
    {
        if (!ReportPathOverridden && !string.IsNullOrWhiteSpace(value))
        {
            var dir = Directory.Exists(value) ? value : Path.GetDirectoryName(value) ?? string.Empty;
            ReportOutputPath = dir;
        }
    }

    public MigrationRunContext BuildRunContext() => new()
    {
        SourceFilePath = SourceFilePath,
        ReportOutputPath = ReportOutputPath,
        Mode = SelectedMode,
    };

    public void Reset()
    {
        ReportPathOverridden = false;
        SourceFilePath = string.Empty;
        ReportOutputPath = string.Empty;
        SelectedCategory = string.Empty;
        SelectedMode = RunMode.ValidateOnly;
    }

    private static string BuildReportPath(string sourcePath)
    {
        return Directory.Exists(sourcePath) ? sourcePath : (Path.GetDirectoryName(sourcePath) ?? string.Empty);
    }
}
