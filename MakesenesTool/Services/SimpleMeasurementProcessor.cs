using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MakesenesTool.Abstractions;
using MakesenesTool.Models;

namespace MakesenesTool.Services;

public class SimpleMeasurementProcessor : DataProcessorBase
{
    private const double ThresholdValue = 1000;

    private bool _validateOnly = true;

    public SimpleMeasurementProcessor(string dbPath, string tableName) : base(dbPath, tableName)
    {
    }

    public override Task ProcessRowsAsync(bool validateOnly, Action? onRowProcessed)
    {
        _validateOnly = validateOnly;
        return base.ProcessRowsAsync(validateOnly, onRowProcessed);
    }

    protected override void ValidateRow(LocationMeasurement r)
    {
        if (double.IsNaN(r.Value))
        {
            r.Status = ProcessStatus.Failed;
            r.Message = "Value is not a number";
        }
        else if (r.Value < 0)
        {
            r.Status = ProcessStatus.Failed;
            r.Message = "Negative value";
        }
        else
        {
            r.Status = ProcessStatus.Passed;
            r.Message = string.Empty;
        }
    }

    protected override void CalculateRow(LocationMeasurement r)
    {
        if (double.IsNaN(r.Value) || double.IsInfinity(r.Value))
        {
            r.Status = ProcessStatus.Failed;
            r.Message = "Value is not a number";
        }
        else if (r.Value > ThresholdValue)
        {
            r.Status = ProcessStatus.Failed;
            r.Message = "Value exceeds threshold";
        }
        else if (r.Status != ProcessStatus.Failed)
        {
            r.Status = ProcessStatus.Passed;
            r.Message = string.Empty;
        }
    }

    public override async Task UpdateStatusAsync()
    {
        await base.UpdateStatusAsync();

        if (!_validateOnly)
            await Task.Run(ComputeTrendStatistics);
    }

    private void ComputeTrendStatistics()
    {
        var measurements = Rows
            .Where(r => r.Time != default && r.Status != ProcessStatus.Failed)
            .OrderBy(r => r.Id)
            .Select(r => (r.Time, r.Value))
            .ToList();

        if (measurements.Count == 0)
            return;

        var annual = MakesensCalculator.BuildAnnualTable(measurements);
        var trends = MakesensCalculator.ComputeAll(annual);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        WriteAnnualTable(conn, tx, annual);
        WriteTrendTable(conn, tx, trends);

        tx.Commit();
    }

    private void WriteAnnualTable(SqliteConnection conn, SqliteTransaction tx, Dictionary<int, double?[]> annual)
    {
        var table = $"Annual_{_table}";
        var names = MakesensCalculator.SeriesNames;

        Execute(conn, tx, $"DROP TABLE IF EXISTS \"{table}\";");
        Execute(conn, tx,
            $"CREATE TABLE \"{table}\" (Year INTEGER PRIMARY KEY, {string.Join(", ", names.Select(n => $"\"{n}\" REAL"))});");

        var columns = string.Join(", ", names.Select(n => $"\"{n}\""));
        var parameters = string.Join(", ", names.Select((_, i) => $"$v{i}"));

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO \"{table}\" (Year, {columns}) VALUES ($year, {parameters});";

        foreach (var (year, row) in annual.OrderBy(kv => kv.Key))
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$year", year);
            for (int i = 0; i < row.Length; i++)
                cmd.Parameters.AddWithValue($"$v{i}", (object?)row[i] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void WriteTrendTable(SqliteConnection conn, SqliteTransaction tx, List<MakesensCalculator.TrendResult> trends)
    {
        var table = $"MK_{_table}";

        Execute(conn, tx, $"DROP TABLE IF EXISTS \"{table}\";");
        Execute(conn, tx, $@"CREATE TABLE ""{table}"" (
            Series TEXT, FirstYear INTEGER, LastYear INTEGER, N INTEGER,
            S INTEGER, Z REAL, Signif TEXT,
            Q REAL, Qmin99 REAL, Qmax99 REAL, Qmin95 REAL, Qmax95 REAL,
            B REAL, Bmin99 REAL, Bmax99 REAL, Bmin95 REAL, Bmax95 REAL);");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"INSERT INTO ""{table}"" (Series, FirstYear, LastYear, N, S, Z, Signif,
            Q, Qmin99, Qmax99, Qmin95, Qmax95, B, Bmin99, Bmax99, Bmin95, Bmax95)
            VALUES ($series, $first, $last, $n, $s, $z, $signif,
            $q, $qmin99, $qmax99, $qmin95, $qmax95, $b, $bmin99, $bmax99, $bmin95, $bmax95);";

        for (int i = 0; i < trends.Count; i++)
        {
            var t = trends[i];
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$series", MakesensCalculator.SeriesNames[i]);
            cmd.Parameters.AddWithValue("$first", t.FirstYear);
            cmd.Parameters.AddWithValue("$last", t.LastYear);
            cmd.Parameters.AddWithValue("$n", t.N);
            cmd.Parameters.AddWithValue("$s", (object?)t.S ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$z", (object?)t.Z ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$signif", t.Signif);
            cmd.Parameters.AddWithValue("$q", (object?)t.Q ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qmin99", (object?)t.Qmin99 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qmax99", (object?)t.Qmax99 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qmin95", (object?)t.Qmin95 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qmax95", (object?)t.Qmax95 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$b", (object?)t.B ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bmin99", (object?)t.Bmin99 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bmax99", (object?)t.Bmax99 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bmin95", (object?)t.Bmin95 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bmax95", (object?)t.Bmax95 ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}