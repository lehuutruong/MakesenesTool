using MigrationCommon.Models;

namespace MigrationCommon.Abstractions;

/// <summary>Contract for all migration handlers across all tools. ViewModels depend on this interface only.</summary>
public interface IMigrationHandler
{
    /// <summary>Number of source rows loaded by InitAsync.</summary>
    int RowCount { get; }

    /// <summary>Prepare before processing: read the source and load lookups.</summary>
    Task InitAsync(MigrationRunContext context, CancellationToken ct = default);

    /// <summary>Validate rows and return the full result including per-row errors.</summary>
    Task<ValidationResult> ValidateAsync(IProgress<int> progress, CancellationToken ct = default);

    /// <summary>Import rows that passed validation. Failed rows are skipped.</summary>
    Task<ImportResult> ImportAsync(IProgress<int> progress, CancellationToken ct = default);

    /// <summary>Write migration status for a single row to the SQLite tracking store.</summary>
    Task UpdateMigrationStatusAsync(int rowId, MigrationStatus status, string? errorMessage = null);
}
