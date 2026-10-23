using MigrationCommon.Data;
using MigrationCommon.Models;

namespace MigrationCommon.Abstractions;

public abstract class MigrationHandlerBase : IMigrationHandler
{
    protected readonly MigrationTrackingContext TrackingContext;

    protected MigrationHandlerBase(MigrationTrackingContext trackingContext)
    {
        TrackingContext = trackingContext;
    }

    public abstract int RowCount { get; }

    public abstract Task InitAsync(MigrationRunContext context, CancellationToken ct = default);

    public abstract Task<ValidationResult> ValidateAsync(IProgress<int> progress, CancellationToken ct = default);

    public abstract Task<ImportResult> ImportAsync(IProgress<int> progress, CancellationToken ct = default);

    public abstract Task UpdateMigrationStatusAsync(int rowId, MigrationStatus status, string? errorMessage = null);
}
