using MigrationCommon.Models;

namespace MigrationCommon.Services;

/// <summary>Writes row errors to an Excel report.</summary>
public interface IReportExportService
{
    /// <summary>Writes the errors to outputPath. Writes a header-only file when the list is empty.</summary>
    Task ExportAsync(IEnumerable<RowError> errors, string outputPath);
}
