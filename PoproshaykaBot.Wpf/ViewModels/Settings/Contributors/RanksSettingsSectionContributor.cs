using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class RanksSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly RanksSettingsSectionViewModel _ranks;
    private bool _edited;

    public RanksSettingsSectionContributor(RanksSettingsSectionViewModel ranks)
    {
        ArgumentNullException.ThrowIfNull(ranks);

        _ranks = ranks;
    }

    public int Order => 5;

    public string SectionKey => "ranks";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Ранги",
            PackIconLucideKind.Crown,
            "ранги ступени пороги баллы король ферзь ладья слон конь пешка значок лестница");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                string.Empty,
                "Ранги",
                "Ступени, которые бот присваивает зрителям по баллам: сообщение в чате – один балл, плюс бонусы, минус штрафы.",
                _ranks),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _ranks.Load(session.Settings.Ranks.Ranks);
        _edited = false;
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnEdited(object? sender, EventArgs e)
        {
            _edited = true;
            markDirty();
        }

        _ranks.Edited += OnEdited;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _ranks.Edited -= OnEdited);
        return subscription;
    }

    public string? ValidateBeforeSave()
    {
        return _edited && _ranks.HasErrors
            ? "В разделе «Ранги» есть ошибки, под полями написано, что не так. Исправьте их и сохраните ещё раз."
            : null;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        if (_edited)
        {
            targetSettings.Ranks.Ranks = _ranks.BuildRanks();
        }
    }

    public Task<IReadOnlyList<SettingsSectionEvent>> OnAppSettingsSavedAsync(bool appSettingsWritten)
    {
        if (!_edited)
        {
            return Task.FromResult<IReadOnlyList<SettingsSectionEvent>>([]);
        }

        _edited = !appSettingsWritten;
        return Task.FromResult<IReadOnlyList<SettingsSectionEvent>>(
            [SettingsSectionEvent.Create(new RanksSettingsChanged())]);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }
}
