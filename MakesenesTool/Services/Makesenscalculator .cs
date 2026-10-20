using System;
using System.Collections.Generic;
using System.Linq;

namespace MakesenesTool.Services;

/// <summary>
/// Port of MAKESENS 1.0 (Finnish Meteorological Institute, 2002): Mann-Kendall test and Sen's slope
/// with confidence intervals. Rules are taken from the Excel template "MAKESENS_Temperatuur2.xls"
/// (sheet "Annual data", sheet "Trend Statistics", VBA CB_CalculateStatistics_Click and friends).
/// </summary>
public static class MakesensCalculator
{
    /// <summary>Column order of the template: 12 months, ANNUAL, III-V, VI-VIII, IX-XI, XII-II.</summary>
    public static readonly string[] SeriesNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
        "ANNUAL", "III-V", "VI-VIII", "IX-XI", "XII-II",
    };

    public const int SeriesCount = 17;

    /// <summary>Relative tolerance used to decide that two values are equal (ties).</summary>
    private const double Tolerance = 1e-9;

    private const int MinMannKendNorm = 10; // n below this: S compared with tables
    private const int MinSenConf = 10;      // n at least this: confidence intervals for Sen's slope
    private const int NoCritical = 9999;    // "level cannot be reached with this n" (VBA fillS)

    // Critical values of |S| for n = 4..10 (two-sided), from Gilbert (1987) Table A18, as in fillS.
    private static readonly int[] S001 = Table((7, 21), (8, 26), (9, 30), (10, 35));
    private static readonly int[] S01 = Table((6, 15), (7, 19), (8, 22), (9, 26), (10, 29));
    private static readonly int[] S05 = Table((5, 10), (6, 11), (7, 15), (8, 18), (9, 20), (10, 23));
    private static readonly int[] S1 = Table((4, 6), (5, 8), (6, 11), (7, 13), (8, 16), (9, 18), (10, 21));

    public sealed class TrendResult
    {
        public int FirstYear { get; init; }
        public int LastYear { get; init; }
        public int N { get; set; }

        // Mann-Kendall
        public int? S { get; set; }
        public double? Z { get; set; }
        public string Signif { get; set; } = string.Empty;

        // Sen's slope and its confidence limits
        public double? Q { get; set; }
        public double? Qmin99 { get; set; }
        public double? Qmax99 { get; set; }
        public double? Qmin95 { get; set; }
        public double? Qmax95 { get; set; }

        // Intercept B of the line f(t) = Q*(t - baseYear) + B
        public double? B { get; set; }
        public double? Bmin99 { get; set; }
        public double? Bmax99 { get; set; }
        public double? Bmin95 { get; set; }
        public double? Bmax95 { get; set; }
    }

    // ------------------------------------------------------------------
    // 1. Annual data: monthly means -> ANNUAL and seasons (template formulas)
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the 17 columns of one year from its 12 monthly values (index 0 = January).
    /// Same as the template: ANNUAL = AVERAGE(B:M), III-V = AVERAGE(D:F), VI-VIII = AVERAGE(G:I),
    /// IX-XI = AVERAGE(J:L), XII-II = AVERAGE(B:C, M). Empty months are skipped, as AVERAGE does.
    /// </summary>
    public static double?[] BuildAnnualRow(double?[] monthly)
    {
        var m = monthly;
        var row = new double?[SeriesCount];
        Array.Copy(m, row, 12);
        row[12] = Mean(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11]);
        row[13] = Mean(m[2], m[3], m[4]);   // III-V  (Mar-May)
        row[14] = Mean(m[5], m[6], m[7]);   // VI-VIII (Jun-Aug)
        row[15] = Mean(m[8], m[9], m[10]);  // IX-XI  (Sep-Nov)
        row[16] = Mean(m[0], m[1], m[11]);  // XII-II (Jan, Feb, Dec of the same year)
        return row;
    }

    /// <summary>
    /// Turns daily measurements into the template's "Annual data" table: year -> 17 values.
    /// Monthly value = mean of the daily values in that month (same as BackupService).
    /// If the same date appears twice, the later value wins (same as BackupService).
    /// </summary>
    public static Dictionary<int, double?[]> BuildAnnualTable(IEnumerable<(DateTime Date, double Value)> daily)
    {
        var byDate = new SortedDictionary<DateTime, double>();
        foreach (var (date, value) in daily)
            byDate[date.Date] = value;

        var monthSums = new Dictionary<(int Year, int Month), (double Sum, int Count)>();
        foreach (var (date, value) in byDate)
        {
            var key = (date.Year, date.Month);
            monthSums.TryGetValue(key, out var acc);
            monthSums[key] = (acc.Sum + value, acc.Count + 1);
        }

        var table = new Dictionary<int, double?[]>();
        foreach (var year in monthSums.Keys.Select(k => k.Year).Distinct().OrderBy(y => y))
        {
            var monthly = new double?[12];
            for (int month = 1; month <= 12; month++)
            {
                if (monthSums.TryGetValue((year, month), out var acc))
                    monthly[month - 1] = acc.Sum / acc.Count;
            }
            table[year] = BuildAnnualRow(monthly);
        }
        return table;
    }

    // ------------------------------------------------------------------
    // 2. Trend statistics for all 17 series
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the trend analysis for all 17 series. The years used are fromYear..toYear
    /// (default: first and last year present in the table). Index i of the result = SeriesNames[i].
    /// </summary>
    public static List<TrendResult> ComputeAll(Dictionary<int, double?[]> annualTable, int? fromYear = null, int? toYear = null)
    {
        if (annualTable.Count == 0)
            return new List<TrendResult>();

        int baseYear = annualTable.Keys.Min();
        int first = fromYear ?? baseYear;
        int last = toYear ?? annualTable.Keys.Max();

        var results = new List<TrendResult>(SeriesCount);
        for (int c = 0; c < SeriesCount; c++)
        {
            var series = new List<double?>();
            for (int year = first; year <= last; year++)
                series.Add(annualTable.TryGetValue(year, out var row) ? row[c] : null);

            results.Add(Analyze(series, first, baseYear));
        }
        return results;
    }

    /// <summary>
    /// Mann-Kendall test and Sen's slope for one series.
    /// series[i] is the value of year firstYear + i (null = missing). baseYear is the zero point of time for B.
    /// </summary>
    public static TrendResult Analyze(IReadOnlyList<double?> series, int firstYear, int baseYear)
    {
        if (firstYear < baseYear)
            throw new ArgumentException("First year is earlier than the base year.");

        int nYears = series.Count;
        var x = new double[nYears];
        int n = 0;
        for (int i = 0; i < nYears; i++)
        {
            if (series[i].HasValue)
            {
                x[i] = series[i]!.Value;
                n++;
            }
            else
            {
                x[i] = double.NaN;
            }
        }

        var result = new TrendResult
        {
            FirstYear = firstYear,
            LastYear = firstYear + nYears - 1,
            N = n,
        };
        if (n < 2)
            return result;

        // --- Mann-Kendall statistic S = sum over k<j of sgn(x_j - x_k) ---
        int s = 0;
        for (int k = 0; k < nYears - 1; k++)
        {
            if (double.IsNaN(x[k])) continue;
            for (int j = k + 1; j < nYears; j++)
            {
                if (double.IsNaN(x[j])) continue;
                s += Sign(x[j], x[k]);
            }
        }
        result.S = s;

        // VAR(S) = [n(n-1)(2n+5) - sum t(t-1)(2t+5)] / 18
        double varS = (n * (n - 1.0) * (2.0 * n + 5) - TiedSum(x)) / 18.0;

        if (n >= 4 && n < MinMannKendNorm)
        {
            // Small sample: compare |S| with the tables
            result.Signif = TableSignif(n, Math.Abs(s));
        }
        else if (n >= MinMannKendNorm)
        {
            // Normal approximation with continuity correction
            double z = s > 0 ? (s - 1) / Math.Sqrt(varS)
                     : s < 0 ? (s + 1) / Math.Sqrt(varS)
                     : 0.0;
            result.Z = z;
            result.Signif = NormalSignif(Math.Abs(z));
        }

        // --- Sen's slope: median of (x_j - x_k) / (j - k) over all pairs ---
        var slopes = new List<double>();
        for (int k = 0; k < nYears - 1; k++)
        {
            if (double.IsNaN(x[k])) continue;
            for (int j = k + 1; j < nYears; j++)
            {
                if (double.IsNaN(x[j])) continue;
                slopes.Add((x[j] - x[k]) / (j - k));
            }
        }

        double q = Median(slopes);
        result.Q = q;
        result.B = CalcB(x, firstYear, baseYear, q);

        // --- Confidence limits of Sen's slope (n >= 10) ---
        if (n >= MinSenConf)
        {
            double sd = Math.Sqrt(varS);
            var (qMin99, qMax99) = ConfidenceInterval(slopes, 2.576 * sd);
            var (qMin95, qMax95) = ConfidenceInterval(slopes, 1.96 * sd);

            result.Qmin99 = qMin99;
            result.Qmax99 = qMax99;
            result.Qmin95 = qMin95;
            result.Qmax95 = qMax95;

            result.Bmin99 = CalcB(x, firstYear, baseYear, qMin99);
            result.Bmax99 = CalcB(x, firstYear, baseYear, qMax99);
            result.Bmin95 = CalcB(x, firstYear, baseYear, qMin95);
            result.Bmax95 = CalcB(x, firstYear, baseYear, qMax95);
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Helpers (ports of the VBA functions)
    // ------------------------------------------------------------------

    /// <summary>Sign of a - b; values equal within tolerance give 0 (VBA Sgn with exact-tie handling).</summary>
    private static int Sign(double a, double b)
    {
        if (AreEqual(a, b)) return 0;
        return a > b ? 1 : -1;
    }

    private static bool AreEqual(double a, double b) =>
        Math.Abs(a - b) <= Tolerance * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

    /// <summary>Correction for ties: sum over groups of equal values of t(t-1)(2t+5), groups with t > 1.</summary>
    private static double TiedSum(double[] x)
    {
        var values = x.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        double sum = 0;
        int i = 0;
        while (i < values.Length)
        {
            int j = i;
            while (j + 1 < values.Length && AreEqual(values[j + 1], values[i]))
                j++;

            int t = j - i + 1;
            if (t > 1)
                sum += t * (t - 1.0) * (2.0 * t + 5);
            i = j + 1;
        }
        return sum;
    }

    /// <summary>B = median of (x_i - Q * (year_i - baseYear)) over the non-missing values.</summary>
    private static double CalcB(double[] x, int firstYear, int baseYear, double q)
    {
        var values = new List<double>(x.Length);
        for (int i = 0; i < x.Length; i++)
        {
            if (!double.IsNaN(x[i]))
                values.Add(x[i] - q * ((firstYear + i) - baseYear));
        }
        return Median(values);
    }

    /// <summary>
    /// Confidence limits of Sen's slope: M1 = (N - Ca)/2, M2 = (N + Ca)/2 on the sorted slopes,
    /// linearly interpolated (same indexing and interpolation as CalculateConfidenceInterval in VBA).
    /// </summary>
    private static (double Lower, double Upper) ConfidenceInterval(List<double> slopes, double calpha)
    {
        var sorted = slopes.OrderBy(v => v).ToArray();
        int nq = sorted.Length;
        double At(int i) => sorted[i - 1]; // 1-based, as in the VBA code

        double m1 = (nq - calpha) / 2.0;
        double m2 = (nq + calpha) / 2.0;

        double lower;
        if (m1 > 1)
        {
            int i1 = (int)Math.Floor(m1);
            lower = At(i1) + (m1 - i1) * (At(i1 + 1) - At(i1));
        }
        else
        {
            lower = At(1);
        }

        double upper;
        if (m2 < nq - 1)
        {
            int i2 = (int)Math.Floor(m2 + 1);
            upper = At(i2) + (m2 + 1 - i2) * (At(i2 + 1) - At(i2));
        }
        else
        {
            upper = At(nq);
        }

        return (lower, upper);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int m = sorted.Length;
        return m % 2 == 0
            ? (sorted[m / 2 - 1] + sorted[m / 2]) / 2.0
            : sorted[m / 2];
    }

    private static string TableSignif(int n, int absS) =>
        absS >= S001[n] ? "***" :
        absS >= S01[n] ? "**" :
        absS >= S05[n] ? "*" :
        absS >= S1[n] ? "+" :
        string.Empty;

    private static string NormalSignif(double absZ) =>
        absZ > 3.292 ? "***" :
        absZ > 2.576 ? "**" :
        absZ > 1.96 ? "*" :
        absZ > 1.645 ? "+" :
        string.Empty;

    /// <summary>Mean of the non-empty values (like Excel AVERAGE); null if there are none.</summary>
    private static double? Mean(params double?[] values)
    {
        double sum = 0;
        int count = 0;
        foreach (var v in values)
        {
            if (v.HasValue)
            {
                sum += v.Value;
                count++;
            }
        }
        return count > 0 ? sum / count : null;
    }

    private static int[] Table(params (int N, int Critical)[] entries)
    {
        var t = new int[11]; // index = n (4..10 used)
        Array.Fill(t, NoCritical);
        foreach (var (n, critical) in entries)
            t[n] = critical;
        return t;
    }
}