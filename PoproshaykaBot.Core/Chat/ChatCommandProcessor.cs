using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat;

public sealed class ChatCommandProcessor
{
    public const string DefaultPrefix = "!";

    private readonly string _unknownCommandsFilePath;
    private readonly Dictionary<string, IChatCommand> _tokenToCommand;
    private readonly CommandSettingsStore _commandSettingsStore;
    private readonly CommandUsageRepository _usageRepository;
    private readonly ILogger<ChatCommandProcessor> _logger;
    private readonly IChatPlatformRegistry? _platformRegistry;

    public ChatCommandProcessor(
        IEnumerable<IChatCommand> commands,
        CommandSettingsStore commandSettingsStore,
        CommandUsageRepository usageRepository,
        ILogger<ChatCommandProcessor> logger,
        string prefix = DefaultPrefix,
        IChatPlatformRegistry? platformRegistry = null)
    {
        _commandSettingsStore = commandSettingsStore;
        _usageRepository = usageRepository;
        _logger = logger;
        _platformRegistry = platformRegistry;

        Directory.CreateDirectory(AppPaths.BaseDirectory);
        _unknownCommandsFilePath = AppPaths.Combine("unknown_commands.txt");

        Prefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;
        _tokenToCommand = new(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            Register(command);
        }
    }

    public string Prefix { get; }

    public void Register(IChatCommand command)
    {
        RegisterToken(command.Canonical, command);

        foreach (var alias in command.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            RegisterToken(alias, command);
        }
    }

    public IReadOnlyCollection<IChatCommand> GetAllCommands()
    {
        return _tokenToCommand
            .Values
            .GroupBy(x => x.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    public IReadOnlyCollection<IChatCommand> GetEnabledCommands()
    {
        var settings = _commandSettingsStore.Load();

        return GetAllCommands()
            .Where(command => settings.IsEnabled(command.Canonical))
            .ToList();
    }

    public IReadOnlyCollection<IChatCommand> GetAvailableCommands(StreamingPlatform platform)
    {
        var settings = _commandSettingsStore.Load();

        return GetAllCommands()
            .Where(command => settings.IsEnabled(command.Canonical, platform)
                              && HasRequiredCapabilities(platform, command))
            .ToList();
    }

    public CommandDryRunResult DryRunInspect(
        string text,
        StreamingPlatform platform,
        bool isBroadcaster = false,
        bool isModerator = false)
    {
        if (!TryParseCommandText(text, out var token, out var args))
        {
            return new(
                CommandDryRunOutcome.NotACommand,
                false,
                null,
                [],
                null,
                [],
                null,
                null,
                PlatformCapabilities.None,
                false,
                false,
                false,
                false,
                "Сообщение не начинается с префикса команды или не содержит имени команды.");
        }

        if (!_tokenToCommand.TryGetValue(token, out var command))
        {
            return new(
                CommandDryRunOutcome.UnknownCommand,
                false,
                token,
                args,
                null,
                [],
                null,
                null,
                PlatformCapabilities.None,
                false,
                false,
                false,
                false,
                $"Команда «{token}» не найдена.");
        }

        var settings = _commandSettingsStore.Load();
        var responseTarget = settings.ResolveResponseTarget(command.Canonical);

        var probeContext = new CommandContext
        {
            Platform = platform,
            Arguments = args,
            IsBroadcaster = isBroadcaster,
            IsModerator = isModerator,
            RecordsUserData = false,
        };

        var eligibility = EvaluateEligibility(command, settings, probeContext);

        return new(
            eligibility.Outcome,
            eligibility.Outcome == CommandDryRunOutcome.WouldExecute,
            token,
            args,
            command.Canonical,
            command.Aliases.ToArray(),
            responseTarget,
            eligibility.RequiredLevel,
            command.RequiredCapabilities,
            eligibility.IsEnabled,
            eligibility.HasCapabilities,
            eligibility.AccessAllowed,
            eligibility.CanExecute,
            eligibility.Reason);
    }

    public async Task<ChatCommandResult> TryProcessAsync(string messageText, CommandContext context, CancellationToken cancellationToken)
    {
        if (!TryParseCommandText(messageText, out var token, out var args))
        {
            return ChatCommandResult.NotHandled;
        }

        if (!_tokenToCommand.TryGetValue(token, out var command))
        {
            LogUnknown(messageText, context);
            return ChatCommandResult.NotHandled;
        }

        var settings = _commandSettingsStore.Load();

        var enrichedContext = new CommandContext
        {
            Platform = context.Platform,
            Channel = context.Channel,
            ChannelKey = context.ChannelKey,
            Origin = context.Origin,
            MessageId = context.MessageId,
            UserId = context.UserId,
            Username = context.Username,
            DisplayName = context.DisplayName,
            Arguments = args,
            IsBroadcaster = context.IsBroadcaster,
            IsModerator = context.IsModerator,
            RecordsUserData = context.RecordsUserData,
        };

        var eligibility = EvaluateEligibility(command, settings, enrichedContext);

        if (eligibility.Outcome != CommandDryRunOutcome.WouldExecute)
        {
            switch (eligibility.Outcome)
            {
                case CommandDryRunOutcome.Disabled:
                    _logger.LogDebug("Команда {Canonical} выключена в настройках, сообщение от {Username} пропущено",
                        command.Canonical,
                        enrichedContext.Username);
                    break;
                case CommandDryRunOutcome.MissingCapabilities:
                    _logger.LogDebug("Команда {Canonical} недоступна на площадке {Platform}: требуются возможности {Capabilities}",
                        command.Canonical,
                        enrichedContext.Platform,
                        command.RequiredCapabilities);
                    break;
                case CommandDryRunOutcome.AccessDenied:
                    _logger.LogDebug("Команда {Canonical} отклонена для {Username}: настройка прав требует {Level}",
                        command.Canonical,
                        enrichedContext.Username,
                        eligibility.RequiredLevel);
                    break;
                case CommandDryRunOutcome.CanExecuteDenied:
                    _logger.LogDebug("Команда {Canonical} отклонена для {Username}: условия выполнения не соблюдены",
                        command.Canonical,
                        enrichedContext.Username);
                    break;
            }

            return ChatCommandResult.NotHandled;
        }

        try
        {
            var response = await command.ExecuteAsync(enrichedContext, cancellationToken);

            _usageRepository.Track(command.Canonical, enrichedContext.DisplayName);

            if (response is null)
            {
                _logger.LogInformation("Команда {Canonical} выполнена для {Username}, ответа нет", command.Canonical, enrichedContext.Username);
            }
            else
            {
                _logger.LogInformation("Команда {Canonical} выполнена для {Username}, ответ {Delivery} длиной {Length} символов",
                    command.Canonical,
                    enrichedContext.Username,
                    response.Delivery,
                    response.Text.Length);
            }

            return ChatCommandResult.Succeeded(command.Canonical, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Команда {Canonical} упала на сообщении от {Username}", command.Canonical, enrichedContext.Username);
            return ChatCommandResult.Failed(command.Canonical);
        }
    }

    private bool TryParseCommandText(string? messageText, out string token, out string[] args)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            token = string.Empty;
            args = [];
            return false;
        }

        var trimmed = messageText.Trim();

        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            token = string.Empty;
            args = [];
            return false;
        }

        var afterPrefix = trimmed[Prefix.Length..].Trim();

        if (string.IsNullOrEmpty(afterPrefix))
        {
            token = string.Empty;
            args = [];
            return false;
        }

        var spaceIdx = afterPrefix.IndexOf(' ');
        token = spaceIdx >= 0 ? afterPrefix[..spaceIdx] : afterPrefix;
        var argsString = spaceIdx >= 0 ? afterPrefix[(spaceIdx + 1)..] : string.Empty;
        args = string.IsNullOrWhiteSpace(argsString)
            ? []
            : argsString.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return true;
    }

    private CommandEligibility EvaluateEligibility(IChatCommand command, CommandSettings settings, CommandContext context)
    {
        var requiredLevel = settings.ResolveAccessLevel(command.Canonical);
        var isEnabled = settings.IsEnabled(command.Canonical, context.Platform);

        if (!isEnabled)
        {
            return new(
                CommandDryRunOutcome.Disabled,
                requiredLevel,
                false,
                false,
                false,
                false,
                $"Команда «{command.Canonical}» выключена для площадки {context.Platform}.");
        }

        var hasCapabilities = HasRequiredCapabilities(context.Platform, command);

        if (!hasCapabilities)
        {
            return new(
                CommandDryRunOutcome.MissingCapabilities,
                requiredLevel,
                true,
                false,
                false,
                false,
                $"Площадка {context.Platform} не поддерживает требуемые возможности: {command.RequiredCapabilities}.");
        }

        var accessAllowed = requiredLevel.Allows(context);

        if (!accessAllowed)
        {
            return new(
                CommandDryRunOutcome.AccessDenied,
                requiredLevel,
                true,
                true,
                false,
                false,
                $"Недостаточно прав: требуется уровень {requiredLevel}.");
        }

        var canExecute = command.CanExecute(context);

        if (!canExecute)
        {
            return new(
                CommandDryRunOutcome.CanExecuteDenied,
                requiredLevel,
                true,
                true,
                true,
                false,
                $"Условия выполнения команды «{command.Canonical}» не соблюдены.");
        }

        return new(CommandDryRunOutcome.WouldExecute, requiredLevel, true, true, true, true, null);
    }

    private readonly record struct CommandEligibility(
        CommandDryRunOutcome Outcome,
        CommandAccessLevel RequiredLevel,
        bool IsEnabled,
        bool HasCapabilities,
        bool AccessAllowed,
        bool CanExecute,
        string? Reason);

    private bool HasRequiredCapabilities(StreamingPlatform platform, IChatCommand command)
    {
        if (command.RequiredCapabilities == PlatformCapabilities.None || _platformRegistry is null)
        {
            return true;
        }

        var capabilities = _platformRegistry.Find(platform)?.Capabilities ?? PlatformCapabilities.None;
        return (capabilities & command.RequiredCapabilities) == command.RequiredCapabilities;
    }

    private void RegisterToken(string token, IChatCommand command)
    {
        if (_tokenToCommand.TryGetValue(token, out var owner) && !ReferenceEquals(owner, command))
        {
            _logger.LogWarning("Токен {Token} команды {Canonical} уже занят командой {Owner}, остаётся за ней",
                token,
                command.Canonical,
                owner.Canonical);

            return;
        }

        _tokenToCommand[token] = command;
    }

    private void LogUnknown(string originalText, CommandContext context)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O}\t{context.Channel}\t{context.UserId}\t{context.Username}\t{originalText}{Environment.NewLine}";
            File.AppendAllText(_unknownCommandsFilePath, line);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать неизвестную команду в {File}", _unknownCommandsFilePath);
        }
    }
}

public readonly record struct ChatCommandResult(bool IsCommand, OutgoingMessage? Response, string? Canonical, bool Executed)
{
    public static ChatCommandResult NotHandled { get; } = new(false, null, null, false);

    public static ChatCommandResult Succeeded(string canonical, OutgoingMessage? response)
    {
        return new(true, response, canonical, true);
    }

    public static ChatCommandResult Failed(string canonical)
    {
        return new(true, null, canonical, false);
    }
}

public enum DeliveryType
{
    Normal = 0,
    Reply = 1,
}

public sealed class CommandContext
{
    public required StreamingPlatform Platform { get; init; }
    public string Channel { get; init; } = string.Empty;
    public ChannelKey? ChannelKey { get; init; }
    public ChatOrigin? Origin { get; init; }
    public string MessageId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public bool IsBroadcaster { get; init; }
    public bool IsModerator { get; init; }
    public bool RecordsUserData { get; init; } = true;
}

public sealed record OutgoingMessage
{
    public string Text { get; init; } = string.Empty;
    public DeliveryType Delivery { get; init; }
    public string? ReplyToMessageId { get; init; }

    public static OutgoingMessage Normal(string text)
    {
        return new()
        {
            Text = text,
            Delivery = DeliveryType.Normal,
        };
    }

    public static OutgoingMessage Reply(string text, string? replyToMessageId)
    {
        return new()
        {
            Text = text,
            Delivery = DeliveryType.Reply,
            ReplyToMessageId = replyToMessageId,
        };
    }
}
