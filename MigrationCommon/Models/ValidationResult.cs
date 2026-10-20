namespace MigrationCommon.Models;

/// <summary>Outcome of a validation pass over all source rows.</summary>
public record ValidationResult(int TotalRows, int PassedRows, IReadOnlyList<RowError> Errors);
