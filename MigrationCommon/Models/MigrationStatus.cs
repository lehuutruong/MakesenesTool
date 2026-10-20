namespace MigrationCommon.Models;

/// <summary>Status of a single source row in the tracking store.</summary>
public enum MigrationStatus
{
    Pending,
    Passed,
    Failed,
    Imported,
    Skipped
}
