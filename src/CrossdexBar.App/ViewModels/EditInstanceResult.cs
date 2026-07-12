namespace CrossdexBar.App.ViewModels;

public sealed record EditInstanceResult(string Label, IReadOnlyDictionary<string, string> Settings);
