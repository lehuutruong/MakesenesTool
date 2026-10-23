using System;

namespace MakesenesTool.Models;

public class LocationMeasurement
{
    public long Id { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public double Value { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
