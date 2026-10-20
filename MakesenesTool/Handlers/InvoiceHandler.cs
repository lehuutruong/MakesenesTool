using MigrationCommon.Data;
using MigrationCommon.Models;
using MakesenesTool.Abstractions;
using MakesenesTool.Models;
using MakesenesTool.Services;

namespace MakesenesTool.Handlers;

public class InvoiceHandler : SupportingDocumentHandlerBase
{
    private const string ExpectedType = "Invoice";

    public InvoiceHandler(MigrationDbContext portalDb, MigrationTrackingContext trackingContext, ExcelReaderService excelReader)
        : base(portalDb, trackingContext, excelReader)
    {
    }

    protected override string CategoryName => "Invoice";

    protected override Task InitCategoryAsync(MigrationRunContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override Task<ValidationResult> ValidateAsync(IProgress<int> progress, CancellationToken ct = default)
        => RunValidationAsync(ValidateRow, progress, ct);

    public override Task<ImportResult> ImportAsync(IProgress<int> progress, CancellationToken ct = default)
        => RunImportAsync(progress, ct);

    protected override Task PersistRowAsync(SupportingDocumentRow row, CancellationToken ct)
        => throw new NotImplementedException("Invoice import is not mapped to a Portal table yet.");

    private List<RowError> ValidateRow(SupportingDocumentRow row)
    {
        var errors = ValidateCommonFields(row);
        if (!string.Equals(row.DocumentType?.Trim(), ExpectedType, StringComparison.OrdinalIgnoreCase))
            errors.Add(new RowError(row.RowNumber, nameof(row.DocumentType), $"Expected '{ExpectedType}'.", row.DocumentType));
        return errors;
    }
}
