namespace MigrationCommon.Models;

/// <summary>Describes one migration run: input, output and mode.</summary>
public class MigrationRunContext
{
    /// <summary>Full path of the source .xlsx file.</summary>
    public required string SourceFilePath { get; init; }

    /// <summary>Full path where the error report .xlsx is written.</summary>
    public required string ReportOutputPath { get; init; }

    /// <summary>Whether to validate only or validate and import.</summary>
    public required RunMode Mode { get; init; }

    /// <summary>Cancellation token for the whole run.</summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Run mode selected by the user.</summary>
public enum RunMode
{
    /// <summary>Validate and write the error report only.</summary>
    ValidateOnly,

    /// <summary>Validate, then import rows that passed.</summary>
    ValidateAndImport
}
