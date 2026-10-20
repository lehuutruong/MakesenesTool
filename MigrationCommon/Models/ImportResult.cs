namespace MigrationCommon.Models;

/// <summary>Outcome of an import pass. Skipped rows carry the reason in SkippedErrors.</summary>
public record ImportResult(int TotalRows, int ImportedRows, int SkippedRows, IReadOnlyList<RowError> SkippedErrors);
