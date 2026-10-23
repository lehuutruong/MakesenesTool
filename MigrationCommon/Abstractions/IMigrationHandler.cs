using MigrationCommon.Models;

namespace MigrationCommon.Abstractions;

public interface IMigrationHandler
{
    int RowCount { get; }

    Task InitAsync(MigrationRunContext context, CancellationToken ct = default);

    Task<ValidationResult> ValidateAsync(IProgress<int> progress, CancellationToken ct = default);

    Task<ImportResult> ImportAsync(IProgress<int> progress, CancellationToken ct = default);

    Task UpdateMigrationStatusAsync(int rowId, MigrationStatus status, string? errorMessage = null);
}
