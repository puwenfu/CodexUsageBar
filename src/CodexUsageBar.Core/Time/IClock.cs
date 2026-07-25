namespace CodexUsageBar.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
