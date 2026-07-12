namespace CrossdexBar.Core.Tests.Fakes;

internal sealed class FakeClock(DateTimeOffset start)
{
    private DateTimeOffset _current = start;

    public DateTimeOffset Now() => _current;

    public void Advance(TimeSpan by) => _current = _current.Add(by);
}
