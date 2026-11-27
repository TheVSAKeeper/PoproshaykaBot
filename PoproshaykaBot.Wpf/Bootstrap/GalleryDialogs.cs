using KeepShell.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Migration;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed record GalleryDialogCase(
    string Key,
    string Page,
    string? SettingsSection,
    Func<IServiceProvider, IDialogViewModel> Create);

public static class GalleryDialogs
{
    public const string Kind = "dialog";
    public const string KeyPrefix = "dialog:";

    public const string BroadcastProfile = KeyPrefix + "broadcast-profile";
    public const string PollProfile = KeyPrefix + "poll-profile";
    public const string PollFromProfile = KeyPrefix + "poll-from-profile";
    public const string PointTerm = KeyPrefix + "point-term";
    public const string ChatBlockers = KeyPrefix + "chat-blockers";
    public const string ColorPicker = KeyPrefix + "color-picker";
    public const string FirstRunChoice = KeyPrefix + "first-run-choice";
    public const string FirstRunInvalid = KeyPrefix + "first-run-invalid";
    public const string FirstRunSummary = KeyPrefix + "first-run-summary";
    public const string FirstRunPartial = KeyPrefix + "first-run-partial";

    public static IReadOnlyList<GalleryDialogCase> All { get; } =
    [
        new(BroadcastProfile, SectionKeys.Overview, null, CreateBroadcastProfile),
        new(PollProfile, SectionKeys.Overview, null, CreatePollProfile),
        new(PollFromProfile, SectionKeys.Overview, null, CreatePollFromProfile),
        new(PointTerm, SectionKeys.Users, null, CreatePointTerm),
        new(ChatBlockers, SectionKeys.Overview, null, CreateChatBlockers),
        new(ColorPicker, SectionKeys.Settings, "obs", CreateColorPicker),
        new(FirstRunChoice, SectionKeys.Overview, null, CreateFirstRunChoice),
        new(FirstRunInvalid, SectionKeys.Overview, null, CreateFirstRunInvalid),
        new(FirstRunSummary, SectionKeys.Overview, null, CreateFirstRunSummary),
        new(FirstRunPartial, SectionKeys.Overview, null, CreateFirstRunPartial),
    ];

    public static bool IsDialogKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static GalleryDialogCase? Find(string key)
    {
        return All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static IDialogViewModel CreateBroadcastProfile(IServiceProvider services)
    {
        var dialog = services.GetRequiredService<BroadcastProfileEditDialogViewModel>();

        dialog.LoadFrom(services.GetRequiredService<BroadcastProfilesStore>().Load().Profiles.FirstOrDefault() ?? new BroadcastProfile());
        dialog.ConfigureCurrentSettingsMode();

        return dialog;
    }

    private static IDialogViewModel CreatePollProfile(IServiceProvider services)
    {
        var profiles = services.GetRequiredService<PollProfilesManager>();
        var dialog = new PollProfileEditDialogViewModel(profiles);

        if (profiles.GetAll() is [var profile, ..])
        {
            dialog.Load(profile);
        }

        return dialog;
    }

    private static IDialogViewModel CreatePollFromProfile(IServiceProvider services)
    {
        return new PollFromProfileDialogViewModel(
            services.GetRequiredService<PollProfilesManager>(),
            services.GetRequiredService<IDialogService>());
    }

    private static IDialogViewModel CreatePointTerm(IServiceProvider services)
    {
        return new PointTermDialogViewModel(services.GetRequiredService<SettingsManager>().Current.Ranks.PointTerm);
    }

    private static IDialogViewModel CreateChatBlockers(IServiceProvider services)
    {
        return new ChatBlockersDialogViewModel(services.GetRequiredService<ChatDisplayStore>().LoadBlockersText());
    }

    private static IDialogViewModel CreateColorPicker(IServiceProvider services)
    {
        return new ColorPickerDialogViewModel(services.GetRequiredService<ObsChatStore>().Load().TextColor, "Цвет текста");
    }

    private static IDialogViewModel CreateFirstRunChoice(IServiceProvider services)
    {
        var candidate = new LegacyDataSummary
        {
            SourcePath = @"C:\Users\Streamer\AppData\Roaming\PoproshaykaBot",
            Kind = LegacyDataSourceKind.DefaultAppDataDirectory,
            HasMonolithicSettings = true,
            HasSettingsDirectory = false,
            Channel = "streamer_channel",
            UserStatisticsCount = 142,
            TotalMessagesProcessed = 12840,
            HasOAuthTokens = true,
            Files = [new("settings.json", @"C:\Users\Streamer\AppData\Roaming\PoproshaykaBot\settings.json", "settings/settings.json", 4096, new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc))],
        };

        return new FirstRunStartViewModel([candidate], services.GetRequiredService<IFilePicker>(), NullLogger.Instance);
    }

    private static IDialogViewModel CreateFirstRunInvalid(IServiceProvider services)
    {
        var vm = new FirstRunStartViewModel([], services.GetRequiredService<IFilePicker>(), NullLogger.Instance);
        vm.ShowGalleryInvalidFolder("В выбранной папке не найдены данные PoproshaykaBot: отсутствуют настройки (settings.json или папка settings) и файлы статистики (users_statistics.json, bot_statistics.json).");
        return vm;
    }

    private static IDialogViewModel CreateFirstRunSummary(IServiceProvider services)
    {
        var vm = new FirstRunStartViewModel([], services.GetRequiredService<IFilePicker>(), NullLogger.Instance);
        vm.ShowGallerySummary(new FirstRunImportSummary(
            "Данные успешно перенесены",
            [
                new("Профили эфира", "7 профилей"),
                new("Оверлей и локальный порт", "Оформление чата OBS и порт 8080"),
                new("OBS и аудиоисточники", "Подключение 127.0.0.1:4455, микрофон «Микрофон USB», 2 аудиоисточника"),
                new("Авто-рассылка", "Включена, каждые 15 мин"),
                new("Фразы и ранги", "16 рангов, фразы сообщений бота"),
                new("Статистика зрителей и сессий", "142 зрителя, 18 эфиров"),
            ],
            [],
            IsPartial: false));
        return vm;
    }

    private static IDialogViewModel CreateFirstRunPartial(IServiceProvider services)
    {
        var vm = new FirstRunStartViewModel([], services.GetRequiredService<IFilePicker>(), NullLogger.Instance);
        vm.ShowGallerySummary(new FirstRunImportSummary(
            "Данные перенесены частично",
            [
                new("Профили эфира", "7 профилей"),
                new("Оверлей и локальный порт", "Оформление чата OBS и порт 8080"),
                new("Авто-рассылка", "Включена, каждые 15 мин"),
                new("Статистика зрителей и сессий", "142 зрителя"),
            ],
            [
                new("OBS и аудиоисточники", "Настройки OBS в старой папке не найдены – при необходимости подключите OBS в Настройках."),
                new("Вход в Twitch", "Для работы бота нужно войти в Twitch.", IsWarning: true),
            ],
            IsPartial: true));
        return vm;
    }
}
