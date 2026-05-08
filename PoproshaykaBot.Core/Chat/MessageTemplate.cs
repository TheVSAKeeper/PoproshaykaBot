namespace PoproshaykaBot.Core.Chat;

public readonly struct MessageTemplate
{
    private readonly string _template;
    private readonly List<KeyValuePair<string, string>> _values;

    private MessageTemplate(string template, List<KeyValuePair<string, string>> values)
    {
        _template = template;
        _values = values;
    }

    public static MessageTemplate For(string template)
    {
        return new(template ?? string.Empty, []);
    }

    public MessageTemplate With(string key, string? value)
    {
        var copy = new List<KeyValuePair<string, string>>(_values)
        {
            new($"{{{key}}}", value ?? string.Empty),
        };

        return new(_template, copy);
    }

    public string Render()
    {
        if (string.IsNullOrEmpty(_template) || _values.Count == 0)
        {
            return _template ?? string.Empty;
        }

        var result = _template;

        foreach (var (placeholder, value) in _values)
        {
            result = result.Replace(placeholder, value);
        }

        return result;
    }

    public bool Contains(string key)
    {
        return _template.Contains($"{{{key}}}", StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Render();
    }
}
