namespace CrossdexBar.App.ViewModels;

public sealed record AddInstanceResult(string ProviderId, string Label, IReadOnlyDictionary<string, string> Settings);
