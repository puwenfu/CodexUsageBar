using CodexUsageBar.Core.Models;
using CodexUsageBar.Core.Services;

namespace CodexUsageBar.Core.Tests;

public sealed class QuotaSnapshotFactoryTests
{
    [Fact]
    public void Create_ClassifiesFiveHourAndWeeklyWindows()
    {
        var snapshot = QuotaSnapshotFactory.Create(
            [new(300, 28, 1_800_000_000), new(10_080, 59, 1_800_604_800)],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(72, snapshot.FiveHour!.RemainingPercent);
        Assert.Equal(41, snapshot.Weekly!.RemainingPercent);
        Assert.Equal(QuotaWindowKind.FiveHour, snapshot.FiveHour.Kind);
        Assert.Equal(QuotaWindowKind.Weekly, snapshot.Weekly.Kind);
    }

    [Theory]
    [InlineData(-5, 100)]
    [InlineData(int.MinValue, 100)]
    [InlineData(28, 72)]
    [InlineData(120, 0)]
    public void Create_ClampsRemainingPercent(int used, int expectedRemaining)
    {
        var snapshot = QuotaSnapshotFactory.Create([new(300, used, null)], DateTimeOffset.UnixEpoch);

        Assert.Equal(expectedRemaining, snapshot.FiveHour!.RemainingPercent);
    }

    [Fact]
    public void Create_RejectsUnknownDuration()
    {
        Assert.Throws<QuotaCompatibilityException>(() =>
            QuotaSnapshotFactory.Create([new(60, 10, null)], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Create_RejectsNullDuration()
    {
        Assert.Throws<QuotaCompatibilityException>(() =>
            QuotaSnapshotFactory.Create([new(null, 10, null)], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Create_RejectsDuplicateWindowKind()
    {
        Assert.Throws<QuotaCompatibilityException>(() =>
            QuotaSnapshotFactory.Create([new(300, 10, null), new(300, 20, null)], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Create_AllowsOneMissingWindow()
    {
        var snapshot = QuotaSnapshotFactory.Create([new(300, 28, null)], DateTimeOffset.UnixEpoch);

        Assert.NotNull(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
    }
}
