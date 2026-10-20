namespace MigrationCommon.Models;

/// <summary>A single problem found on a source row.</summary>
public record RowError(int RowNumber, string FieldName, string Message, string? RawValue);
