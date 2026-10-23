using ClosedXML.Excel;
using MakesenesTool.Models;
using System.IO;

namespace MakesenesTool.Services;

public class ExcelReaderService
{
    private static readonly string[] RequiredHeaders = ["DocumentDate", "ReferenceNo", "Amount", "DocumentType"];

    public Task<IReadOnlyList<SupportingDocumentRow>> ReadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Source file not found.", path);

        return Task.Run<IReadOnlyList<SupportingDocumentRow>>(() => ReadRows(path), ct);
    }

    private static IReadOnlyList<SupportingDocumentRow> ReadRows(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var columns = MapHeaders(sheet.Row(1));
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        var rows = new List<SupportingDocumentRow>();
        for (int r = 2; r <= lastRow; r++)
        {
            var excelRow = sheet.Row(r);
            if (excelRow.IsEmpty())
                continue;
            rows.Add(MapRow(excelRow, columns, r));
        }
        return rows;
    }

    private static SupportingDocumentRow MapRow(IXLRow excelRow, IReadOnlyDictionary<string, int> columns, int rowNumber)
    {
        return new SupportingDocumentRow
        {
            RowNumber = rowNumber,
            DocumentDate = ReadCell(excelRow, columns, "DocumentDate"),
            ReferenceNo = ReadCell(excelRow, columns, "ReferenceNo"),
            Amount = ReadCell(excelRow, columns, "Amount"),
            DocumentType = ReadCell(excelRow, columns, "DocumentType"),
        };
    }

    private static Dictionary<string, int> MapHeaders(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            map[cell.GetString().Trim()] = cell.Address.ColumnNumber;

        var missing = RequiredHeaders.Where(h => !map.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Missing required column(s): {string.Join(", ", missing)}.");

        return map;
    }

    private static string? ReadCell(IXLRow row, IReadOnlyDictionary<string, int> columns, string header)
        => columns.TryGetValue(header, out var column) ? row.Cell(column).GetFormattedString().Trim() : null;
}
