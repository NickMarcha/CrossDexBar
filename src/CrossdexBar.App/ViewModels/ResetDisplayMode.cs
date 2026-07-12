namespace CrossdexBar.App.ViewModels;

/// <summary>Shared toggle between "time left" (relative) and "reset date" (absolute) shown on every card.</summary>
public sealed class ResetDisplayMode
{
    public bool ShowAbsolute { get; set; }
}
