using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Infrastructure;

namespace PoproshaykaBot.WinForms.Chat;

public sealed class ChatCommandProcessor
{
    private readonly string _unknownCommandsFilePath;
    private readonly Dictionary<string, IChatCommand> _tokenToCommand;
    private readonly string _prefix;

    public ChatCommandProcessor(IEnumerable<IChatCommand> commands, string prefix = "!")
    {
        Directory.CreateDirectory(AppPaths.BaseDirectory);
        _unknownCommandsFilePath = AppPaths.Combine("unknown_commands.txt");

        _prefix = string.IsNullOrWhiteSpace(prefix) ? "!" : prefix;
        _tokenToCommand = new(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            Register(command);
        }
    }

    public void Register(IChatCommand command)
    {
        _tokenToCommand[command.Canonical] = command;

        foreach (var alias in command.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _tokenToCommand[alias] = command;
            }
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

    public bool TryProcess(string messageText, CommandContext context, out OutgoingMessage? response)
    {
        response = null;

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var trimmed = messageText.Trim();

        if (!trimmed.StartsWith(_prefix))
        {
            return false;
        }

        var afterPrefix = trimmed[_prefix.Length..].Trim();

        if (string.IsNullOrEmpty(afterPrefix))
        {
            return false;
        }

        var spaceIdx = afterPrefix.IndexOf(' ');
        var token = spaceIdx >= 0 ? afterPrefix[..spaceIdx] : afterPrefix;
        var argsString = spaceIdx >= 0 ? afterPrefix[(spaceIdx + 1)..] : string.Empty;

        if (!_tokenToCommand.TryGetValue(token, out var command))
        {
            LogUnknown(messageText, context);
            return false;
        }

        var args = string.IsNullOrWhiteSpace(argsString)
            ? []
            : argsString.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var enrichedContext = new CommandContext
        {
            Channel = context.Channel,
            MessageId = context.MessageId,
            UserId = context.UserId,
            Username = context.Username,
            DisplayName = context.DisplayName,
            Arguments = args,
            IsBroadcaster = context.IsBroadcaster,
            IsModerator = context.IsModerator,
        };

        if (!command.CanExecute(enrichedContext))
        {
            return false;
        }

        response = command.Execute(enrichedContext);
        return true;
    }

    private void LogUnknown(string originalText, CommandContext context)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O}\t{context.Channel}\t{context.UserId}\t{context.Username}\t{originalText}{Environment.NewLine}";
            File.AppendAllText(_unknownCommandsFilePath, line);
        }
        catch
        {
            // TODO: Логирование
        }
    }
}

public enum DeliveryType
{
    Normal = 0,
    Reply = 1,
}

public sealed class CommandContext
{
    public string Channel { get; init; } = string.Empty;
    public string MessageId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public bool IsBroadcaster { get; init; }
    public bool IsModerator { get; init; }
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
