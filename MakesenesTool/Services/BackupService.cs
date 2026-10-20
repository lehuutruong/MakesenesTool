using ClosedXML.Excel;
using MakesenesTool.Models;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MakesenesTool.Services;

public sealed class YearStat
{
    public int Year { get; init; }

    public double?[] Monthly { get; } = new double?[12];

    public double? Kiet { get; set; }

    public double? Lu { get; set; }

    public double? TbNam { get; set; }
}

public class BackupService
{
    private const string StatsTablePrefix = "TB_";

    private const int FloodStartMonth = 9;

    public Task<string> BackupFolderAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException("Source folder not found.");

        return Task.Run(() => BackupFolder(folderPath), ct);
    }

    public static Dictionary<string, List<YearStat>> ReadSheet1Stats(string xlsxPath)
    {
        var collector = new StatsCollector();
        using var workbook = new XLWorkbook(xlsxPath);
        var sheet = workbook.Worksheet(1);

        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString().Trim()).ToList();
        var timeCol = headers.FindIndex(h => string.Equals(h, "Time", StringComparison.OrdinalIgnoreCase));
        var monthCol = headers.FindIndex(h => string.Equals(h, "Month", StringComparison.OrdinalIgnoreCase));
        var yearCol = headers.FindIndex(h => string.Equals(h, "Year", StringComparison.OrdinalIgnoreCase));
        if (timeCol < 0)
            throw new InvalidOperationException("Không tìm thấy cột 'Time' ở dòng đầu tiên.");

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            if (row.IsEmpty())
                continue;

            var timeCell = row.Cell(timeCol + 1);
            DateTime time;
            if (timeCell.DataType == XLDataType.DateTime)
                time = timeCell.GetDateTime();
            else if (!DateTime.TryParse(timeCell.GetFormattedString().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                continue;

            for (int c = 0; c < headers.Count; c++)
            {
                if (c == timeCol || c == monthCol || c == yearCol)
                    continue;
                var raw = row.Cell(c + 1).GetFormattedString().Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    continue;
                collector.Add(headers[c], time, value);
            }
        }
        return collector.BuildStats();
    }

    private sealed class StatsCollector
    {
        private readonly Dictionary<string, Dictionary<DateTime, double>> _daily =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(string location, DateTime time, double value)
        {
            if (!_daily.TryGetValue(location, out var days))
            {
                days = new Dictionary<DateTime, double>();
                _daily[location] = days;
            }
            days[time.Date] = value;
        }

        public Dictionary<string, List<YearStat>> BuildStats()
        {
            var result = new Dictionary<string, List<YearStat>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (location, days) in _daily)
                result[location] = Compute(days);
            return result;
        }

        private static List<YearStat> Compute(Dictionary<DateTime, double> daily)
        {
            var sums = new Dictionary<int, double[]>();
            var counts = new Dictionary<int, int[]>();

            foreach (var (date, value) in daily)
            {
                if (!sums.TryGetValue(date.Year, out var s))
                {
                    s = new double[12];
                    sums[date.Year] = s;
                    counts[date.Year] = new int[12];
                }
                s[date.Month - 1] += value;
                counts[date.Year][date.Month - 1]++;
            }

            var list = new List<YearStat>(sums.Count);
            foreach (var year in sums.Keys.OrderBy(y => y))
            {
                var s = sums[year];
                var c = counts[year];
                var stat = new YearStat { Year = year };

                for (int m = 0; m < 12; m++)
                    stat.Monthly[m] = c[m] > 0 ? s[m] / c[m] : null;

                int floodIdx = FloodStartMonth - 1;
                stat.Kiet = Mean(stat.Monthly, 0, floodIdx);
                stat.Lu = Mean(stat.Monthly, floodIdx, 12);
                stat.TbNam = Mean(stat.Monthly, 0, 12);
                list.Add(stat);
            }
            return list;
        }

        private static double? Mean(double?[] values, int from, int toExclusive)
        {
            double sum = 0;
            int n = 0;
            for (int i = from; i < toExclusive; i++)
            {
                if (values[i] is double v)
                {
                    sum += v;
                    n++;
                }
            }
            return n > 0 ? sum / n : null;
        }
    }

    private static string BackupFolder(string folderPath)
    {
        var files = Directory.GetFiles(folderPath, "*.xlsx");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dbFileName = $"backup_{timestamp}.db";
        var dbPath = Path.Combine(AppContext.BaseDirectory, dbFileName);
        var cs = $"Data Source={dbPath};Cache=Shared";
        using var connection = new SqliteConnection(cs);
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA synchronous = OFF;";
            cmd.ExecuteNonQuery();
        }

        var statsCollector = new StatsCollector();

        using var transaction = connection.BeginTransaction();
        using var writers = new LocationWriterCache(connection, transaction);

        foreach (var file in files)
        {
            using var workbook = new XLWorkbook(file);
            foreach (var sheet in workbook.Worksheets)
            {
                var headerRow = sheet.Row(1);
                var headers = headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();
                if (headers.Count < 4)
                    continue;

                var timeCol = headers.FindIndex(h => string.Equals(h, "Time", StringComparison.OrdinalIgnoreCase));
                var monthCol = headers.FindIndex(h => string.Equals(h, "Month", StringComparison.OrdinalIgnoreCase));
                var yearCol = headers.FindIndex(h => string.Equals(h, "Year", StringComparison.OrdinalIgnoreCase));
                if (timeCol < 0 || monthCol < 0 || yearCol < 0)
                    continue;

                var sourceFile = Path.GetFileName(file);
                for (int r = 2; r <= (sheet.LastRowUsed()?.RowNumber() ?? 1); r++)
                {
                    var row = sheet.Row(r);
                    if (row.IsEmpty())
                        continue;

                    var timeCell = row.Cell(timeCol + 1);
                    DateTime timeValue;
                    if (timeCell.DataType == XLDataType.DateTime)
                        timeValue = timeCell.GetDateTime();
                    else if (!DateTime.TryParse(timeCell.GetFormattedString().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out timeValue))
                        continue;

                    var monthCell = row.Cell(monthCol + 1).GetFormattedString().Trim();
                    var yearCell = row.Cell(yearCol + 1).GetFormattedString().Trim();
                    int.TryParse(monthCell, out var monthValue);
                    int.TryParse(yearCell, out var yearValue);

                    for (int c = 0; c < headers.Count; c++)
                    {
                        if (c == timeCol || c == monthCol || c == yearCol)
                            continue;
                        var locName = headers[c];
                        var cell = row.Cell(c + 1);
                        var raw = cell.GetFormattedString().Trim();
                        if (string.IsNullOrWhiteSpace(raw))
                            continue;
                        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                            continue;

                        writers.InsertMeasurement(sheet.Name, locName, timeValue, monthValue, yearValue, value, sourceFile);

                        statsCollector.Add(SanitizeTableName(locName), timeValue, value);
                    }
                }
            }
        }

        foreach (var (table, yearStats) in statsCollector.BuildStats())
            InsertStats(connection, transaction, StatsTablePrefix + table, yearStats);

        transaction.Commit();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA synchronous = NORMAL;";
            cmd.ExecuteNonQuery();
        }
        connection.Close();
        return dbPath;
    }

    private static void InsertStats(SqliteConnection conn, SqliteTransaction tx, string statsTable, List<YearStat> stats)
    {
        using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = $@"CREATE TABLE IF NOT EXISTS ""{statsTable}"" (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Year INTEGER,
                M01 REAL, M02 REAL, M03 REAL, M04 REAL, M05 REAL, M06 REAL,
                M07 REAL, M08 REAL, M09 REAL, M10 REAL, M11 REAL, M12 REAL,
                Kiet REAL,
                Lu REAL,
                TbNam REAL
            );";
            create.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"INSERT INTO ""{statsTable}""
            (Year, M01, M02, M03, M04, M05, M06, M07, M08, M09, M10, M11, M12, Kiet, Lu, TbNam)
            VALUES ($y, $m1, $m2, $m3, $m4, $m5, $m6, $m7, $m8, $m9, $m10, $m11, $m12, $kiet, $lu, $nam);";

        var pYear = cmd.Parameters.Add("$y", SqliteType.Integer);
        var pMonths = new SqliteParameter[12];
        for (int m = 0; m < 12; m++)
            pMonths[m] = cmd.Parameters.Add($"$m{m + 1}", SqliteType.Real);
        var pKiet = cmd.Parameters.Add("$kiet", SqliteType.Real);
        var pLu = cmd.Parameters.Add("$lu", SqliteType.Real);
        var pNam = cmd.Parameters.Add("$nam", SqliteType.Real);
        cmd.Prepare();

        foreach (var s in stats)
        {
            pYear.Value = s.Year;
            for (int m = 0; m < 12; m++)
                pMonths[m].Value = ToDb(s.Monthly[m]);
            pKiet.Value = ToDb(s.Kiet);
            pLu.Value = ToDb(s.Lu);
            pNam.Value = ToDb(s.TbNam);
            cmd.ExecuteNonQuery();
        }
    }

    private static object ToDb(double? value) => value.HasValue ? value.Value : DBNull.Value;

    private static string SanitizeTableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Location";
        var clean = System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9_]", "_");
        if (char.IsDigit(clean[0]))
            clean = "_" + clean;
        return clean;
    }

    private sealed class LocationWriterCache : IDisposable
    {
        private sealed class Writer
        {
            public required SqliteCommand Command { get; init; }
            public required SqliteParameter Sheet { get; init; }
            public required SqliteParameter Time { get; init; }
            public required SqliteParameter Month { get; init; }
            public required SqliteParameter Year { get; init; }
            public required SqliteParameter Value { get; init; }
            public required SqliteParameter File { get; init; }
        }

        private readonly SqliteConnection _conn;
        private readonly SqliteTransaction _tx;
        private readonly Dictionary<string, Writer> _writers = new(StringComparer.OrdinalIgnoreCase);

        public LocationWriterCache(SqliteConnection conn, SqliteTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public void InsertMeasurement(string sheet, string location, DateTime time, int month, int year, double value, string sourceFile)
        {
            var table = SanitizeTableName(location);
            if (!_writers.TryGetValue(table, out var w))
            {
                w = CreateWriter(table);
                _writers[table] = w;
            }

            w.Sheet.Value = sheet;
            w.Time.Value = time.ToString("o");
            w.Month.Value = month;
            w.Year.Value = year;
            w.Value.Value = value;
            w.File.Value = sourceFile;
            w.Command.ExecuteNonQuery();
        }

        private Writer CreateWriter(string table)
        {
            using (var create = _conn.CreateCommand())
            {
                create.Transaction = _tx;
                create.CommandText = $@"CREATE TABLE IF NOT EXISTS ""{table}"" (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SheetName TEXT,
                    Time TEXT,
                    Month INTEGER,
                    Year INTEGER,
                    Value REAL,
                    SourceFile TEXT
                );";
                create.ExecuteNonQuery();
            }

            var cmd = _conn.CreateCommand();
            cmd.Transaction = _tx;
            cmd.CommandText = $@"INSERT INTO ""{table}"" (SheetName, Time, Month, Year, Value, SourceFile) VALUES ($s, $t, $m, $y, $v, $f);";
            var w = new Writer
            {
                Command = cmd,
                Sheet = cmd.Parameters.Add("$s", SqliteType.Text),
                Time = cmd.Parameters.Add("$t", SqliteType.Text),
                Month = cmd.Parameters.Add("$m", SqliteType.Integer),
                Year = cmd.Parameters.Add("$y", SqliteType.Integer),
                Value = cmd.Parameters.Add("$v", SqliteType.Real),
                File = cmd.Parameters.Add("$f", SqliteType.Text),
            };
            cmd.Prepare();
            return w;
        }

        public void Dispose()
        {
            foreach (var w in _writers.Values)
                w.Command.Dispose();
            _writers.Clear();
        }
    }
}