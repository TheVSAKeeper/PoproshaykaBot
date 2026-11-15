using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public abstract class DraftlessSettingsSectionContributorBase(
    int order,
    string sectionKey,
    string title,
    PackIconLucideKind icon,
    string searchKeywords,
    object content) : ISettingsSectionContributor
{
    public int Order { get; } = order;

    public string SectionKey { get; } = sectionKey;

    public bool IsDraftless => true;

    public SettingsSection CreateSection()
    {
        return new(SectionKey, title, icon, searchKeywords);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(string.Empty, title, null, content),
        ];
    }

    public void Load(SettingsSessionState session)
    {
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        return EmptySubscription.Instance;
    }

    public void BeginSave(AppSettings targetSettings)
    {
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static readonly EmptySubscription Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class McpSettingsSectionContributor(McpSettingsSectionViewModel mcp)
    : DraftlessSettingsSectionContributorBase(
        9,
        "mcp",
        "Агентная отладка",
        PackIconLucideKind.Bot,
        "mcp агент claude codex сервер токен порт инструменты",
        mcp);

public sealed class AppearanceSettingsSectionContributor(AppearanceSettingsSectionViewModel appearance)
    : DraftlessSettingsSectionContributorBase(
        10,
        "appearance",
        "Оформление",
        PackIconLucideKind.Palette,
        "тема масштаб шрифта размер текста заголовок страницы внешний вид",
        appearance);

public sealed class MiscSettingsSectionContributor(MiscSettingsSectionViewModel misc)
    : DraftlessSettingsSectionContributorBase(
        11,
        "misc",
        "Прочее",
        PackIconLucideKind.Wrench,
        "данные приложения папка настроек логи профили трансляций импорт экспорт сброс",
        misc);
