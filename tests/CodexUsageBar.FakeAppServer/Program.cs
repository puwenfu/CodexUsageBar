using System.Text.Json;

namespace CodexUsageBar.FakeAppServer;

public static class FakeAppServerMarker;

internal static class Program
{
    private static readonly string ExpectedClientVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException(
            "The fake app-server assembly does not define a product version.");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            return 64;
        }

        return args[0] switch
        {
            "codex-bucket" => await QuotaSessionAsync("codex-bucket"),
            "legacy-bucket" => await QuotaSessionAsync("legacy-bucket"),
            "unknown-window" => await QuotaSessionAsync("unknown-window"),
            "account-a" => await QuotaSessionAsync("account-a"),
            "account-b" => await QuotaSessionAsync("account-b"),
            "chatgpt-null-email" => await QuotaSessionAsync("chatgpt-null-email"),
            "signed-out" => await QuotaSessionAsync("signed-out"),
            "account-notification" => await QuotaSessionAsync("account-notification"),
            "rate-notification" => await QuotaSessionAsync("rate-notification"),
            "rate-notification-auto-exit" => await QuotaSessionAsync("rate-notification-auto-exit"),
            "early-rate-notification" => await QuotaSessionAsync("early-rate-notification"),
            "request-timeout" => await QuotaSessionAsync("request-timeout"),
            "account-b-rate-timeout" => await QuotaSessionAsync("account-b-rate-timeout"),
            "oversized-used-percent" => await QuotaSessionAsync("oversized-used-percent"),
            "out-of-range-reset" => await QuotaSessionAsync("out-of-range-reset"),
            "missing-account-type" => await QuotaSessionAsync("missing-account-type"),
            "null-account-type" => await QuotaSessionAsync("null-account-type"),
            "empty-account-type" => await QuotaSessionAsync("empty-account-type"),
            "notification-before-response" => await NotificationBeforeResponseAsync(),
            "timeout" => await TimeoutAsync(),
            "exit-before-response" => await ExitBeforeResponseAsync(),
            "rpc-error" => await RpcErrorAsync(),
            "notification-write" => await NotificationWriteAsync(),
            "ready-then-hang" => await ReadyThenHangAsync(),
            _ => 64,
        };
    }

    private static async Task<int> QuotaSessionAsync(string scenario)
    {
        using var initialize = await ReadMessageAsync();
        if (!IsValidInitialize(initialize.RootElement))
        {
            return 65;
        }

        await WriteLineAsync(new
        {
            id = 1,
            result = new
            {
                platformFamily = "windows",
                ignoredInitializeField = "extension",
            },
        });

        using var initialized = await ReadMessageAsync();
        if (!IsValidInitialized(initialized.RootElement))
        {
            return 66;
        }

        using var accountRequest = await ReadMessageAsync();
        if (!IsValidAccountRead(accountRequest.RootElement))
        {
            return 67;
        }

        if (scenario == "request-timeout")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        object? account;
        if (scenario == "signed-out")
        {
            account = null;
        }
        else if (scenario is "account-b" or "account-b-rate-timeout")
        {
            account = new
            {
                type = "chatgpt",
                email = "account-b@example.invalid",
                planType = "pro",
                ignoredAccountField = "extension",
            };
        }
        else if (scenario == "chatgpt-null-email")
        {
            account = new
            {
                type = "chatgpt",
                email = (string?)null,
                planType = "pro",
                ignoredAccountField = "extension",
            };
        }
        else if (scenario == "missing-account-type")
        {
            account = new
            {
                email = "account-a@example.invalid",
                planType = "pro",
            };
        }
        else if (scenario == "null-account-type")
        {
            account = new
            {
                type = (string?)null,
                email = "account-a@example.invalid",
                planType = "pro",
            };
        }
        else if (scenario == "empty-account-type")
        {
            account = new
            {
                type = string.Empty,
                email = "account-a@example.invalid",
                planType = "pro",
            };
        }
        else
        {
            account = new
            {
                type = "chatgpt",
                email = "account-a@example.invalid",
                planType = "pro",
                ignoredAccountField = "extension",
            };
        }
        await WriteLineAsync(new
        {
            id = accountRequest.RootElement.GetProperty("id").GetInt64(),
            result = new { account, ignoredResultField = "extension" },
        });

        if (account is null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        using var rateRequest = await ReadMessageAsync();
        if (!IsValidRateLimitsRead(rateRequest.RootElement))
        {
            return 68;
        }

        if (scenario == "account-b-rate-timeout")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        var legacy = CreateBucket(35, 46);
        object? buckets = scenario == "legacy-bucket"
            ? null
            : new Dictionary<string, object>
            {
                ["codex"] = scenario == "unknown-window"
                    ? CreateBucket(10, 20, primaryDuration: 60)
                    : scenario == "oversized-used-percent"
                        ? CreateBucket(1e100, 59)
                        : scenario == "out-of-range-reset"
                            ? CreateBucket(28, 59, primaryReset: long.MaxValue)
                            : CreateBucket(28, 59),
                ["other"] = CreateBucket(99, 99),
            };
        if (scenario == "early-rate-notification")
        {
            await WriteLineAsync(new
            {
                method = "account/rateLimits/updated",
                @params = new { ignored = "extension" },
            });
        }

        await WriteLineAsync(new
        {
            id = rateRequest.RootElement.GetProperty("id").GetInt64(),
            result = new
            {
                rateLimits = legacy,
                rateLimitsByLimitId = buckets,
                ignoredRateResultField = "extension",
            },
        });

        if (scenario is "account-notification" or "rate-notification" or "rate-notification-auto-exit")
        {
            await Task.Delay(150);
            await WriteLineAsync(new
            {
                method = scenario == "account-notification"
                    ? "account/updated"
                    : "account/rateLimits/updated",
                @params = new
                {
                    account = new { email = "must-not-escape@example.invalid" },
                    raw = "must-not-escape",
                },
            });
        }

        if (scenario == "rate-notification-auto-exit")
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            return 0;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static object CreateBucket(
        double primaryUsed,
        int secondaryUsed,
        int primaryDuration = 300,
        long primaryReset = 1_800_000_000L) =>
        new
        {
            primary = new
            {
                usedPercent = primaryUsed,
                resetsAt = primaryReset,
                windowDurationMins = primaryDuration,
                ignoredWindowField = "extension",
            },
            secondary = new
            {
                usedPercent = secondaryUsed,
                resetsAt = 1_800_010_000L,
                windowDurationMins = 10_080,
                ignoredWindowField = "extension",
            },
            ignoredBucketField = "extension",
        };

    private static bool IsValidInitialize(JsonElement root)
    {
        if (root.GetProperty("id").GetInt64() != 1
            || root.GetProperty("method").GetString() != "initialize")
        {
            return false;
        }

        var parameters = root.GetProperty("params");
        var clientInfo = parameters.GetProperty("clientInfo");
        return clientInfo.GetProperty("name").GetString() == "CodexUsageBar"
            && clientInfo.GetProperty("title").GetString() == "Codex Usage Bar"
            && clientInfo.GetProperty("version").GetString() == ExpectedClientVersion
            && !parameters.GetProperty("capabilities")
                .GetProperty("experimentalApi")
                .GetBoolean();
    }

    private static bool IsValidInitialized(JsonElement root) =>
        !root.TryGetProperty("id", out _)
        && !root.TryGetProperty("params", out _)
        && root.GetProperty("method").GetString() == "initialized";

    private static bool IsValidAccountRead(JsonElement root) =>
        root.GetProperty("method").GetString() == "account/read"
        && !root.GetProperty("params").GetProperty("refreshToken").GetBoolean();

    private static bool IsValidRateLimitsRead(JsonElement root) =>
        root.GetProperty("method").GetString() == "account/rateLimits/read";

    private static async Task<int> NotificationBeforeResponseAsync()
    {
        using var request = await ReadMessageAsync();
        if (request.RootElement.GetProperty("method").GetString() != "initialize")
        {
            return 65;
        }

        await WriteLineAsync(new
        {
            method = "account/rateLimits/updated",
            @params = new { rateLimits = new { primary = new { usedPercent = 28 } } },
        });
        await WriteLineAsync(new
        {
            id = request.RootElement.GetProperty("id").GetInt64(),
            result = new
            {
                platformFamily = "windows",
                platformOs = "windows",
                userAgent = "fake",
                codexHome = @"C:\fake",
            },
        });
        return 0;
    }

    private static async Task<int> TimeoutAsync()
    {
        using var _ = await ReadMessageAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static async Task<int> ExitBeforeResponseAsync()
    {
        using var _ = await ReadMessageAsync();
        return 17;
    }

    private static async Task<int> RpcErrorAsync()
    {
        using var request = await ReadMessageAsync();
        await WriteLineAsync(new
        {
            id = request.RootElement.GetProperty("id").GetInt64(),
            error = new { code = -32601, message = "sensitive fake detail" },
        });
        return 0;
    }

    private static async Task<int> NotificationWriteAsync()
    {
        using var notification = await ReadMessageAsync();
        var root = notification.RootElement;
        if (root.TryGetProperty("id", out _)
            || root.GetProperty("method").GetString() != "initialized"
            || root.GetProperty("params").GetProperty("ready").GetBoolean() is not true)
        {
            return 65;
        }

        await WriteLineAsync(new { method = "fake/accepted" });
        return 0;
    }

    private static async Task<int> ReadyThenHangAsync()
    {
        using var _ = await ReadMessageAsync();
        await WriteLineAsync(new { method = "fake/ready" });
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    private static async Task<JsonDocument> ReadMessageAsync()
    {
        var line = await Console.In.ReadLineAsync();
        if (line is null)
        {
            throw new EndOfStreamException();
        }

        return JsonDocument.Parse(line);
    }

    private static Task WriteLineAsync(object message)
    {
        var line = JsonSerializer.Serialize(message, JsonOptions);
        return Console.Out.WriteLineAsync(line);
    }

    private static JsonSerializerOptions JsonOptions { get; } =
        new(JsonSerializerDefaults.Web);
}
