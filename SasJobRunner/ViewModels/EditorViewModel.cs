namespace SasJobRunner.ViewModels;

public class EditorViewModel
{
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public string InitialCode { get; init; } = string.Empty;
}
