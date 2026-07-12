namespace CrossdexBar.Core.Host;

public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}
