namespace MakesenesTool.ViewModels;

public sealed class NoticeBannerViewModel(string message)
{
    public string Message { get; } = message;
}
