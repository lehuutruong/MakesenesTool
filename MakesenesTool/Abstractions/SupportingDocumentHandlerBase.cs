using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MigrationCommon.Abstractions;
using MigrationCommon.Data;
using MigrationCommon.Models;
using MakesenesTool.Models;
using MakesenesTool.Services;

namespace MakesenesTool.Abstractions;

public abstract class SupportingDocumentHandlerBase : MigrationHandlerBase
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    private readonly HashSet<int> _passedRowNumbers = [];
    private readonly List<RowError> _validationErrors = [];

    protected readonly MigrationDbContext PortalDb;

    protected readonly ExcelReaderService ExcelReader;

    protected MigrationRunContext? RunContext;

    protected IReadOnlyList<SupportingDocumentRow> Rows = [];

    protected SupportingDocumentHandlerBase(
        MigrationDbContext portalDb,
        MigrationTrackingContext trackingContext,
        ExcelReaderService excelReader)
        : base(trackingContext)
    {
        PortalDb = portalDb;
        ExcelReader = excelReader;
    }

    protected abstract string CategoryName { get; }

    public override int RowCount => Rows.Count;

    public override async Task InitAsync(MigrationRunContext context, CancellationToken ct = default)
    {
        RunContext = context;
        Rows = await ExcelReader.ReadAsync(context.SourceFilePath, ct);
        _passedRowNumbers.Clear();
        _validationErrors.Clear();
        await InitCategoryAsync(context, ct);
    }

    protected abstract Task InitCategoryAsync(MigrationRunContext context, CancellationToken ct);

    protected abstract Task PersistRowAsync(SupportingDocumentRow row, CancellationToken ct);

    protected async Task<ValidationResult> RunValidationAsync(
        Func<SupportingDocumentRow, List<RowError>> rules,
        IProgress<int> progress,
        CancellationToken ct)
    {
        _passedRowNumbers.Clear();
        _validationErrors.Clear();

        for (int i = 0; i < Rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = Rows[i];
            var errors = EvaluateRow(rules, row);

            if (errors.Count == 0)
                _passedRowNumbers.Add(row.RowNumber);
            else
                _validationErrors.AddRange(errors);

            var status = errors.Count == 0 ? MigrationStatus.Passed : MigrationStatus.Failed;
            await UpdateMigrationStatusAsync(row.RowNumber, status, errors.FirstOrDefault()?.Message);
            progress.Report(i + 1);
        }

        return new ValidationResult(Rows.Count, _passedRowNumbers.Count, _validationErrors.ToList());
    }

    protected async Task<ImportResult> RunImportAsync(IProgress<int> progress, CancellationToken ct)
    {
        var skippedErrors = new List<RowError>(_validationErrors);
        int imported = 0;

        for (int i = 0; i < Rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = Rows[i];
            if (_passedRowNumbers.Contains(row.RowNumber))
                imported += await TryImportRowAsync(row, skippedErrors, ct);
            progress.Report(i + 1);
        }

        return new ImportResult(Rows.Count, imported, Rows.Count - imported, skippedErrors);
    }
    public override async Task UpdateMigrationStatusAsync(int rowId, MigrationStatus status, string? errorMessage = null)
    {
        var sourceFile = RunContext?.SourceFilePath ?? string.Empty;
        var record = await TrackingContext.MigrationRecords.FirstOrDefaultAsync(r =>
            r.Category == CategoryName && r.RowNumber == rowId && r.SourceFile == sourceFile);

        if (record is null)
        {
            record = new MigrationTrackingRecord { Category = CategoryName, RowNumber = rowId, SourceFile = sourceFile };
            TrackingContext.MigrationRecords.Add(record);
        }

        record.Status = status;
        record.ErrorMessage = errorMessage;
        record.ProcessedAt = DateTime.UtcNow;
        await TrackingContext.SaveChangesAsync();
    }

    protected static List<RowError> ValidateCommonFields(SupportingDocumentRow row)
    {
        var errors = new List<RowError>();

        if (!DateOnly.TryParseExact(row.DocumentDate, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add(new RowError(row.RowNumber, nameof(row.DocumentDate), "Invalid date. Expected dd/MM/yyyy.", row.DocumentDate));

        if (string.IsNullOrWhiteSpace(row.ReferenceNo))
            errors.Add(new RowError(row.RowNumber, nameof(row.ReferenceNo), "Reference number is required.", row.ReferenceNo));

        if (!decimal.TryParse(row.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            errors.Add(new RowError(row.RowNumber, nameof(row.Amount), "Amount must be a positive number.", row.Amount));

        return errors;
    }

    private async Task<int> TryImportRowAsync(SupportingDocumentRow row, List<RowError> skippedErrors, CancellationToken ct)
    {
        try
        {
            await PersistRowAsync(row, ct);
            await UpdateMigrationStatusAsync(row.RowNumber, MigrationStatus.Imported);
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            skippedErrors.Add(new RowError(row.RowNumber, "(import)", ex.Message, null));
            await UpdateMigrationStatusAsync(row.RowNumber, MigrationStatus.Failed, ex.Message);
            return 0;
        }
    }

    private static List<RowError> EvaluateRow(Func<SupportingDocumentRow, List<RowError>> rules, SupportingDocumentRow row)
    {
        try
        {
            return rules(row);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [new RowError(row.RowNumber, "(row)", $"Unexpected error: {ex.Message}", null)];
        }
    }
}
