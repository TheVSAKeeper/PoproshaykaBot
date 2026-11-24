using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public sealed record BroadcastProfilePlatformApplyResult(
    StreamingPlatform Platform,
    bool Succeeded,
    string? ErrorMessage = null);

public static class BroadcastProfileApplySummary
{
    public static bool IsPartial(IReadOnlyList<BroadcastProfilePlatformApplyResult>? results)
    {
        if (results is null || results.Count == 0)
        {
            return false;
        }

        var hasSuccess = false;
        var hasFailure = false;

        foreach (var result in results)
        {
            if (result.Succeeded)
            {
                hasSuccess = true;
            }
            else
            {
                hasFailure = true;
            }

            if (hasSuccess && hasFailure)
            {
                return true;
            }
        }

        return false;
    }

    public static string FormatPartialStatus(
        string profileName,
        IReadOnlyList<BroadcastProfilePlatformApplyResult> results,
        Func<StreamingPlatform, string>? getPlatformName = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        var parts = results.Select(result => FormatPlatformOutcome(result, getPlatformName));
        return $"Профиль «{profileName}»: {string.Join("; ", parts)}";
    }

    public static string FormatFailureStatus(
        IReadOnlyList<BroadcastProfilePlatformApplyResult> results,
        Func<StreamingPlatform, string>? getPlatformName = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        var failures = results.Where(static result => !result.Succeeded).ToArray();

        if (failures.Length == 0)
        {
            return "Не удалось применить профиль";
        }

        if (failures.Length == 1 && !string.IsNullOrWhiteSpace(failures[0].ErrorMessage))
        {
            return failures[0].ErrorMessage!;
        }

        return string.Join("; ", failures.Select(result => FormatPlatformOutcome(result, getPlatformName)));
    }

    private static string FormatPlatformOutcome(
        BroadcastProfilePlatformApplyResult result,
        Func<StreamingPlatform, string>? getPlatformName)
    {
        var name = getPlatformName?.Invoke(result.Platform) ?? result.Platform.ToString();

        if (result.Succeeded)
        {
            return $"{name} обновлён";
        }

        var reason = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "не удалось обновить"
            : result.ErrorMessage.Trim();

        return $"{name} – {reason}";
    }
}
