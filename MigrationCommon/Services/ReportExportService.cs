using ClosedXML.Excel;
using MigrationCommon.Models;

namespace MigrationCommon.Services;

public class ReportExportService : IReportExportService
{
    private const string SheetName = "Validation errors";
    private static readonly string[] Headers = ["Row number", "Field", "Error message", "Source value"];

    public Task ExportAsync(IEnumerable<RowError> errors, string outputPath)
    {
        var snapshot = errors.ToList();
        return Task.Run(() => Write(snapshot, outputPath));
    }

    private static void Write(IReadOnlyList<RowError> errors, string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        WriteHeader(sheet);
        for (int i = 0; i < errors.Count; i++)
            WriteError(sheet, i + 2, errors[i]);

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(outputPath);
    }

    private static void WriteHeader(IXLWorksheet sheet)
    {
        for (int c = 0; c < Headers.Length; c++)
            sheet.Cell(1, c + 1).Value = Headers[c];
        sheet.Row(1).Style.Font.Bold = true;
    }

    private static void WriteError(IXLWorksheet sheet, int row, RowError error)
    {
        sheet.Cell(row, 1).Value = error.RowNumber;
        sheet.Cell(row, 2).Value = error.FieldName;
        sheet.Cell(row, 3).Value = error.Message;
        sheet.Cell(row, 4).Value = error.RawValue ?? string.Empty;
    }
}
