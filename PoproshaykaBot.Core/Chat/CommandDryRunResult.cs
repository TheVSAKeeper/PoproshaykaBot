using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Chat;

public enum CommandDryRunOutcome
{
    NotACommand = 0,
    UnknownCommand = 1,
    Disabled = 2,
    MissingCapabilities = 3,
    AccessDenied = 4,
    CanExecuteDenied = 5,
    WouldExecute = 6,
}

public sealed record CommandDryRunResult(
    CommandDryRunOutcome Outcome,
    bool WouldExecute,
    string? Token,
    IReadOnlyList<string> Arguments,
    string? Canonical,
    IReadOnlyList<string> Aliases,
    CommandResponseTarget? ResponseTarget,
    CommandAccessLevel? RequiredAccessLevel,
    PlatformCapabilities RequiredCapabilities,
    bool IsEnabled,
    bool HasRequiredCapabilities,
    bool AccessAllowed,
    bool CanExecuteResult,
    string? Reason);
