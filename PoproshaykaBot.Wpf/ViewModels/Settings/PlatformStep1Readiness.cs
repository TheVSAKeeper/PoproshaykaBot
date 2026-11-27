using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using System.ComponentModel.DataAnnotations;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

internal readonly record struct Step1ReadinessResult(bool Ready, string? Summary, string? MissingReason);

internal static class PlatformStep1Readiness
{
    public static Step1ReadinessResult Evaluate(
        StreamingPlatform platform,
        OAuthSettingsViewModel? twitchDraft,
        SettingsManager? settingsManager,
        YouTubeSettingsSectionViewModel? youtubeDraft,
        YouTubeSettingsStore? youtubeSettingsStore,
        YouTubeAccountsStore? youtubeAccountsStore)
    {
        if (platform == StreamingPlatform.Twitch && twitchDraft is not null && settingsManager is not null)
        {
            return EvaluateTwitch(twitchDraft, settingsManager);
        }

        if (platform == StreamingPlatform.YouTube && youtubeDraft is not null && youtubeSettingsStore is not null && youtubeAccountsStore is not null)
        {
            return EvaluateYouTube(youtubeDraft, youtubeSettingsStore, youtubeAccountsStore);
        }

        return new(true, null, null);
    }

    public static Step1ReadinessResult EvaluateTwitch(OAuthSettingsViewModel draft, SettingsManager settingsManager)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(draft.ClientId))
        {
            missing.Add("Client ID");
        }

        if (string.IsNullOrWhiteSpace(draft.ClientSecret))
        {
            missing.Add("Client Secret");
        }

        if (OAuthSettingsViewModel.ValidateRedirectUri(draft.RedirectUri, new(draft)) != ValidationResult.Success)
        {
            missing.Add("Redirect URI");
        }

        if (missing.Count > 0)
        {
            return new(false, null, $"Заполните и сохраните Шаг 1 («Доступ площадки») выше: не хватает {string.Join(", ", missing)}.");
        }

        var saved = settingsManager.Current.Twitch;
        if (draft.ClientId.Trim() != saved.ClientId
            || draft.ClientSecret.Trim() != saved.ClientSecret
            || draft.RedirectUri.Trim() != saved.RedirectUri)
        {
            return new(false, null, "Сначала заполните и сохраните Шаг 1 («Доступ площадки») выше: есть несохранённые изменения реквизитов.");
        }

        return new(true, "Ключи приложения и Redirect URI настроены", null);
    }

    public static Step1ReadinessResult EvaluateYouTube(
        YouTubeSettingsSectionViewModel draft,
        YouTubeSettingsStore settingsStore,
        YouTubeAccountsStore accountsStore)
    {
        List<string> missing = [];
        if (!draft.Enabled)
        {
            missing.Add("включить YouTube");
        }

        if (string.IsNullOrWhiteSpace(draft.ClientId))
        {
            missing.Add("указать Client ID");
        }

        if (string.IsNullOrWhiteSpace(draft.ClientSecret))
        {
            missing.Add("указать Client Secret");
        }

        if (draft.HasQuotaBudgetError)
        {
            missing.Add("исправить бюджет квоты");
        }

        if (missing.Count > 0)
        {
            return new(false, null, $"Сначала заполните и сохраните Шаг 1 («Доступ площадки») выше: нужно {string.Join(", ", missing)}.");
        }

        var saved = settingsStore.Load();
        var savedSecret = accountsStore.LoadClientSecret();
        if (!saved.Enabled
            || saved.Enabled != draft.Enabled
            || draft.ClientId.Trim() != saved.ClientId
            || draft.ClientSecret.Trim() != savedSecret)
        {
            return new(false, null, "Сначала заполните и сохраните Шаг 1 («Доступ площадки») выше: есть несохранённые изменения параметров.");
        }

        return new(true, "YouTube включён, ключи Google-проекта сохранены", null);
    }
}
