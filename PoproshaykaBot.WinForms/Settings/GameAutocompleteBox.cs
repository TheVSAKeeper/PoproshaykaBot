using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Settings;

public sealed partial class GameAutocompleteBox : UserControl
{
    public GameAutocompleteBox()
    {
        InitializeComponent();
    }

    public event EventHandler? SelectionChanged;

    [Inject]
    public IGameCategoryResolver Resolver { get; internal init; } = null!;

    public GameSuggestion? Selected { get; private set; }

    public void SetSelected(string gameId, string gameName)
    {
        Selected = string.IsNullOrEmpty(gameId) ? null : new GameSuggestion(gameId, gameName, string.Empty);
        _queryTextBox.Text = gameName;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        try
        {
            _debounceTimer.Stop();

            if (IsDisposed)
            {
                return;
            }

            var query = _queryTextBox.Text.Trim();

            if (query.Length < 2)
            {
                _suggestionsListBox.Visible = false;
                return;
            }

            var suggestions = await Resolver.SearchAsync(query, CancellationToken.None);

            if (IsDisposed)
            {
                return;
            }

            _suggestionsListBox.Items.Clear();

            foreach (var s in suggestions)
            {
                _suggestionsListBox.Items.Add(s);
            }

            _suggestionsListBox.DisplayMember = nameof(GameSuggestion.Name);
            _suggestionsListBox.Visible = suggestions.Count > 0;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnSuggestionClicked(object? sender, EventArgs e)
    {
        if (_suggestionsListBox.SelectedItem is not GameSuggestion suggestion)
        {
            return;
        }

        Selected = suggestion;
        _queryTextBox.Text = suggestion.Name;
        _suggestionsListBox.Visible = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
