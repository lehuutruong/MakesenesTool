namespace MakesenesTool.Models;

/// <summary>A single entry in the Screen 2 log panel.</summary>
public record LogLine(string Timestamp, string Message, LogLevel Level);
