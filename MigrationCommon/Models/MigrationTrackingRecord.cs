namespace MigrationCommon.Models;

/// <summary>Persisted per-row migration status in the local SQLite tracking database.</summary>
public class MigrationTrackingRecord
{
    public int Id { get; set; }

    /// <summary>Category key, e.g. "Invoice" or "CreditNote".</summary>
    public string Category { get; set; } = string.Empty;

    public int RowNumber { get; set; }

    public string SourceFile { get; set; } = string.Empty;

    public MigrationStatus Status { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime ProcessedAt { get; set; }
}
