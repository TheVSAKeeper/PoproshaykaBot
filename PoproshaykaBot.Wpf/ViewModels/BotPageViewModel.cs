using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class BotPageViewModel(
    CommandsPageViewModel commands, AutoMessagesPageViewModel autoMessages, IUnsavedChangesPrompt prompt)
    : ObservableObject, IPageHeader, IUnsavedChangesPage
{
    private int _activeTab;

    [ObservableProperty]
    private int _selectedTab;

    public string PageTitle => "Бот";
    public string? PageDescription => null;
    public IReadOnlyList<SegmentOption> Tabs { get; } =
        [new(PackIconLucideKind.Terminal, "Команды"), new(PackIconLucideKind.MessageSquare, "Автосообщения")];
    public IUnsavedChangesPage ActivePage => _activeTab == 0 ? commands : autoMessages;
    public bool HasUnsavedChanges => ActivePage.HasUnsavedChanges;
    public string UnsavedChangesSubject => ActivePage.UnsavedChangesSubject;

    public Task<bool> TrySaveUnsavedChangesAsync() => ActivePage.TrySaveUnsavedChangesAsync();
    public void DiscardUnsavedChanges() => ActivePage.DiscardUnsavedChanges();

    public void OnEnter()
    {
        if (_activeTab == 0)
        {
            commands.OnEnter();
        }
        else
        {
            autoMessages.OnEnter();
        }
    }

    public void OpenCommands(string? canonical = null)
    {
        SelectedTab = 0;
        if (_activeTab == 0 && canonical is not null)
        {
            commands.TrySelect(canonical);
        }
    }

    public void OpenRule(string key)
    {
        SelectedTab = 1;
        if (_activeTab == 1)
        {
            autoMessages.TryOpen(key);
        }
    }

    partial void OnSelectedTabChanged(int value)
    {
        if (value == _activeTab)
        {
            return;
        }

        if (value < 0 || value >= Tabs.Count || !ConfirmDraft())
        {
            SelectedTab = _activeTab;
            return;
        }

        _activeTab = value;
        OnEnter();
        OnPropertyChanged(nameof(ActivePage));
    }

    private bool ConfirmDraft()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        switch (prompt.Ask(UnsavedChangesSubject, "переходом на другую вкладку"))
        {
            case UnsavedChangesDecision.Save:
                return ActivePage switch
                {
                    CommandsPageViewModel page => page.TrySaveDraft(),
                    AutoMessagesPageViewModel page => page.TrySaveDraft(),
                    _ => false,
                };
            case UnsavedChangesDecision.Discard:
                DiscardUnsavedChanges();
                return true;
            default:
                return false;
        }
    }
}
