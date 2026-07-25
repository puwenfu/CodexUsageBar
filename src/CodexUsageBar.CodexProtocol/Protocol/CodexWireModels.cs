namespace CodexUsageBar.CodexProtocol.Protocol;

internal sealed record CodexAccountReadResult(CodexAccount? Account);

internal sealed record CodexAccount(
    string Type,
    string? Email,
    string? PlanType);

internal sealed record CodexRateLimitsReadResult(
    CodexRateLimitBucket? RateLimits,
    IReadOnlyDictionary<string, CodexRateLimitBucket>? RateLimitsByLimitId);

internal sealed record CodexRateLimitBucket(
    CodexRateLimitWindow? Primary,
    CodexRateLimitWindow? Secondary);

internal sealed record CodexRateLimitWindow(
    double? UsedPercent,
    long? ResetsAt,
    long? WindowDurationMins);
