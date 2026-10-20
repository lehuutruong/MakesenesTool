using MigrationCommon.Data;
using MigrationCommon.Models;

namespace MigrationCommon.Abstractions;

/// <summary>Generic base for all migration handlers. Knows nothing about specific tools or row types.</summary>
public abstract class MigrationHandlerBase : IMigrationHandler
{
    /// <summary>Tracking store shared by all handlers.</summary>
    protected readonly MigrationTrackingContext TrackingContext;

    /// <param name="trackingContext">SQLite tracking context.</param>
    protected MigrationHandlerBase(MigrationTrackingContext trackingContext)
    {
        TrackingContext = trackingContext;
    }

    /// <inheritdoc/>
    public abstract int RowCount { get; }

    /// <inheritdoc/>
    public abstract Task InitAsync(MigrationRunContext context, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task<ValidationResult> ValidateAsync(IProgress<int> progress, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task<ImportResult> ImportAsync(IProgress<int> progress, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task UpdateMigrationStatusAsync(int rowId, MigrationStatus status, string? errorMessage = null);
}
