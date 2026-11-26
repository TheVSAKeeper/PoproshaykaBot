using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class StartupOnboardingSession
{
    public bool IncludeLegacyImport { get; init; }

    public IReadOnlyList<LegacyDataSummary> Candidates { get; init; } = [];

    public LegacyDataSummary? Target { get; init; }

    public Func<LegacyImportResult, bool, Task<IServiceProvider?>>? OnImportCompletedAsync { get; init; }

    public Action<bool>? OnLegacyImportSkipped { get; init; }
}
