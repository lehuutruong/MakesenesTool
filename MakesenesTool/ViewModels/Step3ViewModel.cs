using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MakesenesTool.Models;
using MakesenesTool.Services;
using MigrationCommon.Abstractions;
using MigrationCommon.Models;
using MigrationCommon.Services;
using Microsoft.Data.Sqlite;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;

namespace MakesenesTool.ViewModels;

public partial class Step3ViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private const int TrendParametersStartColumn = 15; // O
    private const int ChartDataStartColumn = 18; // R

    private readonly IReportExportService _reportExporter;
    private string? _reportPath;
    private string? _logFilePath;
    private readonly object _logFileLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _currentRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _totalRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenReport))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private NoticeBannerViewModel? _noticeBanner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenReport))]
    private bool _reportReady;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassedLabel))]
    [NotifyPropertyChangedFor(nameof(FailedLabel))]
    private RunMode _mode = RunMode.ValidateOnly;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _passedCount;

    [ObservableProperty]
    private int _failedCount;

    public ObservableCollection<LogLine> LogLines { get; } = [];

    public double ProgressPercent => TotalRows == 0 ? 0 : CurrentRow * 100.0 / TotalRows;

    public bool CanOpenReport => ReportReady;

    public bool HasNotice => NoticeBanner is not null;

    public string PassedLabel => Mode == RunMode.ValidateOnly ? "Successful" : "Imported";

    public string FailedLabel => Mode == RunMode.ValidateOnly ? "Failed" : "Skipped";

    public Step3ViewModel(IReportExportService reportExporter)
    {
        _reportExporter = reportExporter;
    }

    private sealed record TrendLine(
        int? FirstYear,
        int? LastYear,
        int? N,
        int? S,
        double? Z,
        string? Signif,
        double? Q,
        double? Qmin99,
        double? Qmax99,
        double? Qmin95,
        double? Qmax95,
        double? B,
        double? Bmin99,
        double? Bmax99,
        double? Bmin95,
        double? Bmax95);

    private async Task ExportReportsFromDbAsync(string dbPath, string outputFolder)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(outputFolder);

            using var conn = new SqliteConnection(
                $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared;Default Timeout=10");
            conn.Open();

            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name NOT LIKE 'sqlite_%'
                    ORDER BY name;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tableNames.Add(reader.GetString(0));
            }

            var annualTables = tableNames
                .Where(name => name.StartsWith("Annual_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (annualTables.Count == 0)
            {
                Log(LogLevel.Warning,
                    "No Annual_<Location> tables found. Run calculation mode first to generate annual and trend tables.");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            foreach (var annualTableName in annualTables)
            {
                try
                {
                    var location = annualTableName["Annual_".Length..];
                    var trendTableName = $"MK_{location}";
                    var hasTrendTable = tableNames.Contains(trendTableName);

                    using var package = new ExcelPackage();

                    var annualWs = package.Workbook.Worksheets.Add("Annual");
                    CopySqliteTableToWorksheet(conn, annualTableName, annualWs);

                    var trendWs = package.Workbook.Worksheets.Add("Trend Statistics");
                    if (hasTrendTable)
                    {
                        CopySqliteTableToWorksheet(conn, trendTableName, trendWs, orderTrendStatistics: true);
                    }
                    else
                    {
                        WriteEmptyTrendStatisticsHeader(trendWs);
                        trendWs.Cells[3, 1].Value =
                            $"No trend results were generated for location '{location}'.";
                    }

                    var trendResults = hasTrendTable
                        ? ReadTrendResults(conn, trendTableName)
                        : new Dictionary<string, TrendLine>(StringComparer.OrdinalIgnoreCase);

                    for (int month = 1; month <= 12; month++)
                    {
                        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
                        var monthlyWs = package.Workbook.Worksheets.Add(monthName);

                        CreateMonthlyWorksheet(
                            monthlyWs,
                            annualWs,
                            month,
                            monthName,
                            location,
                            trendResults);
                    }

                    var safeLocation = MakeSafeFileName(location);
                    var filePath = Path.Combine(
                        outputFolder,
                        $"{safeLocation}_{timestamp}_report.xlsx");

                    package.SaveAs(new FileInfo(filePath));
                    Log(LogLevel.Ok,
                        $"Exported location {location}: {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error,
                        $"Failed to export location table {annualTableName}: {ex.Message}");
                }
            }
        });
    }

    private static void CreateMonthlyWorksheet(
        ExcelWorksheet ws,
        ExcelWorksheet annualWs,
        int month,
        string monthName,
        string location,
        IReadOnlyDictionary<string, TrendLine> trendResults)
    {
        var annualColumn = FindMonthColumn(annualWs, month);

        WriteMonthlyDataHeaders(ws, ChartDataStartColumn);

        string seriesName = annualColumn == 0
            ? monthName
            : Convert.ToString(
                annualWs.Cells[1, annualColumn].Value,
                CultureInfo.InvariantCulture) ?? monthName;

        trendResults.TryGetValue(NormalizeSeriesName(seriesName), out var trend);
        WriteTrendParameters(ws, TrendParametersStartColumn, seriesName, trend);

        if (annualColumn == 0)
        {
            ws.Cells[3, ChartDataStartColumn].Value =
                $"No matching column for {monthName} was found in the Annual sheet.";
            ws.Cells[1, ChartDataStartColumn, 1, ChartDataStartColumn + 6].Style.Font.Bold = true;
            FormatMonthlyWorksheet(ws);
            return;
        }

        var lastAnnualRow = annualWs.Dimension?.End.Row ?? 1;
        if (lastAnnualRow < 2)
        {
            ws.Cells[3, ChartDataStartColumn].Value = "The Annual sheet contains no yearly data.";
            FormatMonthlyWorksheet(ws);
            return;
        }

        int? firstAvailableYear = null;
        for (int sourceRow = 2; sourceRow <= lastAnnualRow; sourceRow++)
        {
            if (TryGetInt(annualWs.Cells[sourceRow, 1].Value, out var year))
            {
                firstAvailableYear = year;
                break;
            }
        }

        if (firstAvailableYear is null)
        {
            ws.Cells[3, ChartDataStartColumn].Value = "Could not read the Year column from Annual.";
            FormatMonthlyWorksheet(ws);
            return;
        }

        var yearColumn = ChartDataStartColumn;
        var dataColumn = ChartDataStartColumn + 1;
        var senColumn = ChartDataStartColumn + 2;
        var min99Column = ChartDataStartColumn + 3;
        var max99Column = ChartDataStartColumn + 4;
        var min95Column = ChartDataStartColumn + 5;
        var max95Column = ChartDataStartColumn + 6;

        int outputRow = 2;
        for (int sourceRow = 2; sourceRow <= lastAnnualRow; sourceRow++)
        {
            if (!TryGetInt(annualWs.Cells[sourceRow, 1].Value, out var year))
                continue;

            ws.Cells[outputRow, yearColumn].Value = year;

            var actualValue = annualWs.Cells[sourceRow, annualColumn].Value;
            if (actualValue is not null && actualValue != DBNull.Value)
                ws.Cells[outputRow, dataColumn].Value = actualValue;

            if (trend is not null)
            {
                var trendYear = year - (trend.FirstYear ?? firstAvailableYear.Value);
                ws.Cells[outputRow, senColumn].Value = EvaluateTrend(trend.Q, trend.B, trendYear);
                ws.Cells[outputRow, min99Column].Value = EvaluateTrend(trend.Qmin99, trend.Bmin99, trendYear);
                ws.Cells[outputRow, max99Column].Value = EvaluateTrend(trend.Qmax99, trend.Bmax99, trendYear);
                ws.Cells[outputRow, min95Column].Value = EvaluateTrend(trend.Qmin95, trend.Bmin95, trendYear);
                ws.Cells[outputRow, max95Column].Value = EvaluateTrend(trend.Qmax95, trend.Bmax95, trendYear);
            }

            outputRow++;
        }

        var lastDataRow = outputRow - 1;
        if (lastDataRow < 2)
        {
            ws.Cells[3, ChartDataStartColumn].Value = "No valid annual rows were found for this month.";
            FormatMonthlyWorksheet(ws);
            return;
        }

        FormatMonthlyWorksheet(ws, lastDataRow);

        var chart = (ExcelScatterChart)ws.Drawings.AddChart(
            $"MonthlyChart{month:00}",
            eChartType.XYScatterLines);

        chart.Title.Text = $"{location} - {monthName} ({seriesName})";
        chart.Style = eChartStyle.Style13;
        chart.DisplayBlanksAs = eDisplayBlanksAs.Gap;
        chart.EditAs = OfficeOpenXml.Drawing.eEditAs.OneCell;
        chart.SetPosition(0, 0, 0, 0);
        chart.SetSize(850, 430);
        chart.XAxis.Title.Text = "Year";
        chart.YAxis.Title.Text = "Value";
        chart.XAxis.Format = "0";
        chart.YAxis.Format = "0.00";
        chart.Legend.Position = eLegendPosition.Bottom;

        var yearRange = ws.Cells[2, yearColumn, lastDataRow, yearColumn];
        var dataRange = ws.Cells[2, dataColumn, lastDataRow, dataColumn];

        var actualSeries = (ExcelScatterChartSerie)chart.Series.Add(dataRange, yearRange);
        actualSeries.Header = "Observed data";
        actualSeries.Marker = eMarkerStyle.Circle;
        actualSeries.MarkerSize = 6;
        actualSeries.MarkerColor = Color.FromArgb(31, 78, 121);
        actualSeries.MarkerLineColor = Color.White;
        actualSeries.LineColor = Color.FromArgb(31, 78, 121);
        actualSeries.Border.Width = 0;

        AddTrendChartSeries(chart, ws, yearRange, senColumn, lastDataRow, "Sen's estimate");
        AddTrendChartSeries(chart, ws, yearRange, min99Column, lastDataRow, "99% conf. min");
        AddTrendChartSeries(chart, ws, yearRange, max99Column, lastDataRow, "99% conf. max");
        AddTrendChartSeries(chart, ws, yearRange, min95Column, lastDataRow, "95% conf. min");
        AddTrendChartSeries(chart, ws, yearRange, max95Column, lastDataRow, "95% conf. max");
    }

    private static void WriteMonthlyDataHeaders(ExcelWorksheet ws, int startColumn)
    {
        string[] headers =
        [
            "Year",
            "Data",
            "Sen's estimate",
            "99% conf. min",
            "99% conf. max",
            "95% conf. min",
            "95% conf. max"
        ];

        for (int i = 0; i < headers.Length; i++)
            ws.Cells[1, startColumn + i].Value = headers[i];

        var headerRange = ws.Cells[1, startColumn, 1, startColumn + headers.Length - 1];
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.Color.SetColor(Color.White);
        headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(31, 78, 121));
        headerRange.Style.WrapText = true;
        headerRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
    }

    private static void WriteTrendParameters(
        ExcelWorksheet ws,
        int startColumn,
        string seriesName,
        TrendLine? trend)
    {
        ws.Cells[1, startColumn].Value = "Trend parameter";
        ws.Cells[1, startColumn + 1].Value = "Value";
        var headerRange = ws.Cells[1, startColumn, 1, startColumn + 1];
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.Color.SetColor(Color.White);
        headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(31, 78, 121));
        headerRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        var parameters = new (string Name, object? Value)[]
        {
            ("Series", seriesName),
            ("FirstYear", trend?.FirstYear),
            ("LastYear", trend?.LastYear),
            ("N", trend?.N),
            ("S", trend?.S),
            ("Z", trend?.Z),
            ("Signif", trend?.Signif),
            ("Q", trend?.Q),
            ("Qmin99", trend?.Qmin99),
            ("Qmax99", trend?.Qmax99),
            ("Qmin95", trend?.Qmin95),
            ("Qmax95", trend?.Qmax95),
            ("B", trend?.B),
            ("Bmin99", trend?.Bmin99),
            ("Bmax99", trend?.Bmax99),
            ("Bmin95", trend?.Bmin95),
            ("Bmax95", trend?.Bmax95)
        };

        int row = 2;
        foreach (var (name, value) in parameters)
        {
            ws.Cells[row, startColumn].Value = name;
            ws.Cells[row, startColumn + 1].Value = value;
            row++;
        }

        if (row > 2)
        {
            var parameterLabels = ws.Cells[2, startColumn, row - 1, startColumn];
            parameterLabels.Style.Font.Bold = true;
            parameterLabels.Style.Fill.PatternType = ExcelFillStyle.Solid;
            parameterLabels.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 246, 250));

            var parameterTable = ws.Cells[1, startColumn, row - 1, startColumn + 1];
            parameterTable.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            parameterTable.Style.Border.Bottom.Color.SetColor(Color.FromArgb(217, 225, 232));
        }
    }

    private static void FormatMonthlyWorksheet(ExcelWorksheet ws, int lastDataRow = 1)
    {
        int dataStart = ChartDataStartColumn;
        int dataEnd = ChartDataStartColumn + 6;
        int parametersStart = TrendParametersStartColumn;

        ws.Column(dataStart).Width = 12;
        ws.Column(dataStart + 1).Width = 16;
        ws.Column(dataStart + 2).Width = 18;
        ws.Column(dataStart + 3).Width = 16;
        ws.Column(dataStart + 4).Width = 16;
        ws.Column(dataStart + 5).Width = 16;
        ws.Column(dataStart + 6).Width = 16;
        ws.Column(parametersStart).Width = 22;
        ws.Column(parametersStart + 1).Width = 18;
        ws.View.ShowGridLines = false;

        if (lastDataRow >= 2)
        {
            ws.Cells[1, dataStart, lastDataRow, dataEnd].Style.VerticalAlignment =
                OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
            ws.Cells[2, dataStart + 1, lastDataRow, dataEnd].Style.Numberformat.Format = "0.0000";
            ws.Cells[2, dataStart, lastDataRow, dataStart].Style.Numberformat.Format = "0";
            ws.Cells[1, dataStart, lastDataRow, dataEnd].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            ws.Cells[2, dataStart, lastDataRow, dataEnd].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
    }

    private static double? EvaluateTrend(double? q, double? b, int t)
    {
        if (!q.HasValue || !b.HasValue)
            return null;

        return q.Value * t + b.Value;
    }

    private static void AddTrendChartSeries(
        ExcelScatterChart chart,
        ExcelWorksheet ws,
        ExcelRangeBase yearRange,
        int valueColumn,
        int lastRow,
        string title)
    {
        bool hasValues = false;
        for (int row = 2; row <= lastRow; row++)
        {
            if (ws.Cells[row, valueColumn].Value is not null)
            {
                hasValues = true;
                break;
            }
        }

        if (!hasValues)
            return;

        var values = ws.Cells[2, valueColumn, lastRow, valueColumn];
        var series = (ExcelScatterChartSerie)chart.Series.Add(values, yearRange);
        series.Header = title;
        series.Marker = eMarkerStyle.None;

        series.LineColor = title switch
        {
            "Sen's estimate" => Color.FromArgb(192, 0, 0),       // red, central estimate
            "99% conf. min" => Color.FromArgb(237, 125, 49),    // orange
            "99% conf. max" => Color.FromArgb(112, 48, 160),    // purple
            "95% conf. min" => Color.FromArgb(0, 128, 96),      // teal-green
            "95% conf. max" => Color.FromArgb(0, 112, 192),     // blue
            _ => Color.FromArgb(89, 89, 89)
        };
        series.LineWidth = title == "Sen's estimate" ? 2.75 : 1.75;
    }

    private static readonly string[] TrendStatisticsSeriesOrder =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
        "ANNUAL", "III-V", "VI-VIII", "IX-XI", "XII-II"
    ];

    private static void CopySqliteTableToWorksheet(
        SqliteConnection conn,
        string tableName,
        ExcelWorksheet ws,
        bool orderTrendStatistics = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = orderTrendStatistics
            ? $"SELECT * FROM {QuoteIdentifier(tableName)};"
            : $"SELECT * FROM {QuoteIdentifier(tableName)} ORDER BY 1;";

        using var reader = cmd.ExecuteReader();
        var columnCount = reader.FieldCount;

        for (int column = 0; column < columnCount; column++)
            ws.Cells[1, column + 1].Value = reader.GetName(column);

        int row = 2;
        if (orderTrendStatistics)
        {
            var records = new List<(object?[] Values, int OriginalIndex)>();
            int originalIndex = 0;

            while (reader.Read())
            {
                var values = new object?[columnCount];
                for (int column = 0; column < columnCount; column++)
                    values[column] = reader.IsDBNull(column) ? null : reader.GetValue(column);

                records.Add((values, originalIndex++));
            }

            var seriesOrder = TrendStatisticsSeriesOrder
                .Select((name, index) => (Key: NormalizeSeriesName(name), Index: index))
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.OrdinalIgnoreCase);

            var seriesOrdinal = -1;
            for (int column = 0; column < columnCount; column++)
            {
                if (string.Equals(reader.GetName(column), "Series", StringComparison.OrdinalIgnoreCase))
                {
                    seriesOrdinal = column;
                    break;
                }
            }

            records = records
                .OrderBy(record =>
                {
                    var seriesName = seriesOrdinal >= 0
                        ? Convert.ToString(record.Values[seriesOrdinal], CultureInfo.InvariantCulture) ?? string.Empty
                        : string.Empty;
                    return seriesOrder.TryGetValue(NormalizeSeriesName(seriesName), out var index)
                        ? index
                        : int.MaxValue;
                })
                .ThenBy(record => record.OriginalIndex)
                .ToList();

            foreach (var record in records)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    if (record.Values[column] is not null)
                        ws.Cells[row, column + 1].Value = record.Values[column];
                }

                row++;
            }
        }
        else
        {
            while (reader.Read())
            {
                for (int column = 0; column < columnCount; column++)
                {
                    if (!reader.IsDBNull(column))
                        ws.Cells[row, column + 1].Value = reader.GetValue(column);
                }

                row++;
            }
        }

        if (columnCount > 0)
        {
            ws.Cells[1, 1, 1, columnCount].Style.Font.Bold = true;
            ws.View.FreezePanes(2, 1);
            ws.Cells[1, 1, Math.Max(1, row - 1), columnCount].AutoFilter = true;

            for (int column = 1; column <= columnCount; column++)
                ws.Column(column).Width = 16;
        }
    }


    private static void WriteEmptyTrendStatisticsHeader(ExcelWorksheet ws)
    {
        string[] headers =
        [
            "Series", "FirstYear", "LastYear", "N", "S", "Z", "Signif",
            "Q", "Qmin99", "Qmax99", "Qmin95", "Qmax95",
            "B", "Bmin99", "Bmax99", "Bmin95", "Bmax95"
        ];

        for (int i = 0; i < headers.Length; i++)
            ws.Cells[1, i + 1].Value = headers[i];

        ws.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
    }

    private static Dictionary<string, TrendLine> ReadTrendResults(
        SqliteConnection conn,
        string tableName)
    {
        var results = new Dictionary<string, TrendLine>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)};";

        using var reader = cmd.ExecuteReader();

        int seriesIndex = reader.GetOrdinal("Series");
        int firstYearIndex = reader.GetOrdinal("FirstYear");
        int lastYearIndex = reader.GetOrdinal("LastYear");
        int nIndex = reader.GetOrdinal("N");
        int sIndex = reader.GetOrdinal("S");
        int zIndex = reader.GetOrdinal("Z");
        int signifIndex = reader.GetOrdinal("Signif");
        int qIndex = reader.GetOrdinal("Q");
        int qmin99Index = reader.GetOrdinal("Qmin99");
        int qmax99Index = reader.GetOrdinal("Qmax99");
        int qmin95Index = reader.GetOrdinal("Qmin95");
        int qmax95Index = reader.GetOrdinal("Qmax95");
        int bIndex = reader.GetOrdinal("B");
        int bmin99Index = reader.GetOrdinal("Bmin99");
        int bmax99Index = reader.GetOrdinal("Bmax99");
        int bmin95Index = reader.GetOrdinal("Bmin95");
        int bmax95Index = reader.GetOrdinal("Bmax95");

        while (reader.Read())
        {
            if (reader.IsDBNull(seriesIndex))
                continue;

            var name = NormalizeSeriesName(reader.GetString(seriesIndex));
            results[name] = new TrendLine(
                ReadNullableInt(reader, firstYearIndex),
                ReadNullableInt(reader, lastYearIndex),
                ReadNullableInt(reader, nIndex),
                ReadNullableInt(reader, sIndex),
                ReadNullableDouble(reader, zIndex),
                reader.IsDBNull(signifIndex) ? null : Convert.ToString(reader.GetValue(signifIndex), CultureInfo.InvariantCulture),
                ReadNullableDouble(reader, qIndex),
                ReadNullableDouble(reader, qmin99Index),
                ReadNullableDouble(reader, qmax99Index),
                ReadNullableDouble(reader, qmin95Index),
                ReadNullableDouble(reader, qmax95Index),
                ReadNullableDouble(reader, bIndex),
                ReadNullableDouble(reader, bmin99Index),
                ReadNullableDouble(reader, bmax99Index),
                ReadNullableDouble(reader, bmin95Index),
                ReadNullableDouble(reader, bmax95Index));
        }

        return results;
    }

    private static int? ReadNullableInt(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static double? ReadNullableDouble(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        return Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static int FindMonthColumn(ExcelWorksheet ws, int month)
    {
        string[] fullNames =
        [
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        ];
        string[] shortNames =
        [
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        ];

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeSeriesName(fullNames[month - 1]),
            NormalizeSeriesName(shortNames[month - 1]),
            $"month{month}",
            $"month{month:00}",
            $"monthly{month}",
            $"monthly{month:00}",
            $"m{month}",
            $"m{month:00}",
            $"thang{month}",
            $"thang{month:00}",
            month.ToString(CultureInfo.InvariantCulture),
            month.ToString("00", CultureInfo.InvariantCulture)
        };

        int lastColumn = ws.Dimension?.End.Column ?? 0;
        for (int column = 2; column <= lastColumn; column++)
        {
            var header = Convert.ToString(ws.Cells[1, column].Value, CultureInfo.InvariantCulture);
            if (header is not null && aliases.Contains(NormalizeSeriesName(header)))
                return column;
        }

        return 0;
    }

    private static bool TryGetInt(object? value, out int result)
    {
        if (value is null || value == DBNull.Value)
        {
            result = 0;
            return false;
        }

        return int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static string NormalizeSeriesName(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Location" : safe;
    }

    public async Task RunProcessorsAsync(string dbPath, bool validateOnly, string outputFolder)
    {
        BeginRun(Mode);
        try
        {
            var tables = new List<string>();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));
                reader.Close();

                int total = 0;
                foreach (var table in tables)
                {
                    using var countCmd = conn.CreateCommand();
                    countCmd.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)};";
                    total += Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                try
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => TotalRows = total);
                }
                catch
                {
                    TotalRows = total;
                }
            }

            CurrentRow = 0;
            StatusLabel = validateOnly ? "Validating data…" : "Calculating…";
            const int uiBatch = 100;

            for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
            {
                var table = tables[tableIndex];
                Log(LogLevel.Info, $"Processing table {table} ({tableIndex + 1}/{tables.Count})...");

                var processor = new SimpleMeasurementProcessor(dbPath, table);
                await processor.InitAsync();

                int processed = 0;
                int pendingUi = 0;

                void OnRow()
                {
                    Interlocked.Increment(ref processed);
                    var newPending = Interlocked.Increment(ref pendingUi);
                    if (newPending >= uiBatch)
                    {
                        var toAdd = Interlocked.Exchange(ref pendingUi, 0);
                        try
                        {
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                                new Action(() => CurrentRow += toAdd));
                        }
                        catch
                        {
                            CurrentRow += toAdd;
                        }
                    }
                }

                await processor.ProcessRowsAsync(validateOnly, OnRow);

                var remaining = Interlocked.Exchange(ref pendingUi, 0);
                if (remaining > 0)
                {
                    try
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(
                            new Action(() => CurrentRow += remaining));
                    }
                    catch
                    {
                        CurrentRow += remaining;
                    }
                }

                await processor.UpdateStatusAsync();
                var (total, passed, failed) = processor.GetCounts();

                try
                {
                    var cs = $"Data Source={dbPath};Cache=Shared";
                    using var sumConn = new SqliteConnection(cs);
                    sumConn.Open();

                    using (var pragma = sumConn.CreateCommand())
                    {
                        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
                        pragma.ExecuteNonQuery();
                    }

                    using var create = sumConn.CreateCommand();
                    create.CommandText = @"CREATE TABLE IF NOT EXISTS ProcessingSummaries (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TableName TEXT,
                        Total INTEGER,
                        Successful INTEGER,
                        Failed INTEGER,
                        Timestamp TEXT
                    );";
                    create.ExecuteNonQuery();

                    using var insert = sumConn.CreateCommand();
                    insert.CommandText = @"INSERT INTO ProcessingSummaries
                        (TableName, Total, Successful, Failed, Timestamp)
                        VALUES ($tname, $total, $succ, $fail, $ts);";
                    insert.Parameters.AddWithValue("$tname", table);
                    insert.Parameters.AddWithValue("$total", total);
                    insert.Parameters.AddWithValue("$succ", passed);
                    insert.Parameters.AddWithValue("$fail", failed);
                    insert.Parameters.AddWithValue("$ts", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    insert.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Could not write summary for {table}: {ex.Message}");
                }

                TotalCount += total;
                PassedCount += passed;
                FailedCount += failed;
                Log(LogLevel.Ok, $"Finished {table}: {processed} rows, {passed} success, {failed} failed");
            }

            StatusLabel = "Completed.";
            IsCompleted = true;
            _reportPath = outputFolder;

            try
            {
                await ExportReportsFromDbAsync(dbPath, outputFolder);
                ReportReady = Directory.Exists(outputFolder);
                Log(LogLevel.Ok, "Location reports exported.");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Failed to export reports: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Processing failed: {ex.Message}");
            StatusLabel = "Stopped.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    public async Task RunAsync(IMigrationHandler handler, MigrationRunContext context)
    {
        BeginRun(context.Mode);
        var ct = context.CancellationToken;
        try
        {
            await InitializeAsync(handler, context, ct);
            var validation = await ValidateStepAsync(handler, ct);

            if (context.Mode == RunMode.ValidateOnly)
            {
                await ExportReportAsync(context, validation.Errors);
                ShowSummary(validation.TotalRows, validation.PassedRows, validation.TotalRows - validation.PassedRows);
            }
            else
            {
                var import = await ImportStepAsync(handler, ct);
                if (import.SkippedRows > 0)
                    await ExportReportAsync(context, import.SkippedErrors);
                ShowSummary(import.TotalRows, import.ImportedRows, import.SkippedRows);
            }

            StatusLabel = "Completed.";
            IsCompleted = true;
        }
        catch (FileNotFoundException ex)
        {
            Log(LogLevel.Error, ex.Message);
            StatusLabel = "Stopped.";
        }
        catch (IOException)
        {
            Log(LogLevel.Error, "Cannot open source file — it may be open in another application.");
            StatusLabel = "Stopped.";
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, "Run cancelled.");
            StatusLabel = "Cancelled.";
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Unexpected error: {ex.Message}");
            StatusLabel = "Stopped with errors.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void OpenReport()
    {
        if (_reportPath is null)
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _reportPath,
            UseShellExecute = true
        });
    }

    private void BeginRun(RunMode mode)
    {
        Mode = mode;
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            _logFilePath = Path.Combine(AppContext.BaseDirectory, $"runlog_{stamp}.txt");
            File.WriteAllText(_logFilePath, $"Run started at {DateTime.Now:O}{Environment.NewLine}");
        }
        catch
        {
            _logFilePath = null;
        }

        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ResetRunState());
        }
        catch
        {
            ResetRunState();
        }
    }

    private void ResetRunState()
    {
        IsRunning = true;
        IsCompleted = false;
        ReportReady = false;
        _reportPath = null;
        NoticeBanner = null;
        CurrentRow = 0;
        TotalRows = 0;
        TotalCount = 0;
        PassedCount = 0;
        FailedCount = 0;
        LogLines.Clear();
        StatusLabel = "Reading source file…";
    }

    private async Task InitializeAsync(IMigrationHandler handler, MigrationRunContext context, CancellationToken ct)
    {
        Log(LogLevel.Info, $"Opening {Path.GetFileName(context.SourceFilePath)}");
        await handler.InitAsync(context, ct);
        TotalRows = handler.RowCount;
        Log(LogLevel.Ok, $"Loaded {TotalRows} rows.");
    }

    private async Task<ValidationResult> ValidateStepAsync(IMigrationHandler handler, CancellationToken ct)
    {
        CurrentRow = 0;
        StatusLabel = "Validating rows…";
        var result = await handler.ValidateAsync(new Progress<int>(n => CurrentRow = n), ct);

        foreach (var error in result.Errors)
            Log(LogLevel.Error, $"Row {error.RowNumber} · {error.FieldName}: {error.Message}");

        Log(LogLevel.Ok, $"Validation complete: {result.PassedRows} of {result.TotalRows} rows passed.");
        return result;
    }

    private async Task<ImportResult> ImportStepAsync(IMigrationHandler handler, CancellationToken ct)
    {
        CurrentRow = 0;
        StatusLabel = "Importing records…";
        var result = await handler.ImportAsync(new Progress<int>(n => CurrentRow = n), ct);

        foreach (var error in result.SkippedErrors)
            Log(LogLevel.Error, $"Row {error.RowNumber} · {error.FieldName}: {error.Message}");

        Log(LogLevel.Ok, $"Import complete: {result.ImportedRows} imported, {result.SkippedRows} skipped.");
        return result;
    }

    private async Task ExportReportAsync(MigrationRunContext context, IReadOnlyList<RowError> errors)
    {
        try
        {
            StatusLabel = "Writing report…";
            await _reportExporter.ExportAsync(errors, context.ReportOutputPath);
            _reportPath = context.ReportOutputPath;
            ReportReady = true;
            Log(LogLevel.Info, $"Report written to {context.ReportOutputPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Error, $"Cannot write report: {ex.Message}");
        }
    }

    private void ShowSummary(int total, int passed, int failed)
    {
        TotalCount = total;
        PassedCount = passed;
        FailedCount = failed;

        if (failed > 0)
        {
            var action = Mode == RunMode.ValidateOnly ? "validated" : "imported";
            NoticeBanner = new NoticeBannerViewModel(
                $"{failed} row(s) were not {action}. Open the report for details.");
        }
    }

    private void Log(LogLevel level, string message)
    {
        var time = DateTime.Now;
        var line = new LogLine(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message, level);

        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(line);
                while (LogLines.Count > MaxLogLines)
                    LogLines.RemoveAt(0);
            });
        }
        catch
        {
            LogLines.Add(line);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                var text = $"[{time:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                lock (_logFileLock)
                    File.AppendAllText(_logFilePath, text);
            }
        }
        catch
        {
        }
    }
}
