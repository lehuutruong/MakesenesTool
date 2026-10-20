namespace MakesenesTool.ViewModels;

/// <summary>Message shown in the warning banner on Screen 2.</summary>
public sealed class NoticeBannerViewModel(string message)
{
    /// <summary>Banner text.</summary>
    public string Message { get; } = message;
}
