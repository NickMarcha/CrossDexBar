namespace CrossdexBar.Core.Host;

public interface IPlatformPaths
{
    string HomeDirectory { get; }
    string ConfigDirectory { get; }
    string ConfigFilePath { get; }

    /// <summary>
    /// The per-user application-data root shared by other installed apps (Windows: <c>%APPDATA%</c>; Linux:
    /// <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c>) — for providers that need to locate another app's local
    /// data, such as Cursor's <c>state.vscdb</c>.
    /// </summary>
    string AppDataRoot { get; }
}
