using MigrationCommon.Models;

namespace MigrationCommon.Services;

public interface IReportExportService
{
    Task ExportAsync(IEnumerable<RowError> errors, string outputPath);
}
