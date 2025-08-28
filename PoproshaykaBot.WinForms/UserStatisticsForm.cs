using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public partial class UserStatisticsForm : Form
{
    private readonly StatisticsCollector? _statisticsCollector;
    private Bot? _bot;
    private List<UserStatistics> _allUsers = [];
    private List<UserStatistics> _filteredUsers = [];

    public UserStatisticsForm(StatisticsCollector statisticsCollector, Bot? bot = null)
    {
        _statisticsCollector = statisticsCollector;
        _bot = bot;
        InitializeComponent();
        InitializeRuntime();
        LoadData();
    }

    public void UpdateBotReference(Bot? bot)
    {
        _bot = bot;
    }

    private void textBoxFilter_TextChanged(object sender, EventArgs e)
    {
        ApplyFilter();
    }

    private void listBoxUsers_SelectedIndexChanged(object sender, EventArgs e)
    {
        var user = listBoxUsers.SelectedItem as UserStatistics;
        UpdateDetails(user);
        UpdateActionState();
    }

    private async void buttonAction_Click(object sender, EventArgs e)
    {
        if (listBoxUsers.SelectedItem is not UserStatistics user)
        {
            MessageBox.Show("⚠️ Не выбран пользователь.", "⚠️ Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_statisticsCollector == null)
        {
            MessageBox.Show("❌ Статистика недоступна.", "❌ Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var delta = (long)numericIncrement.Value;

        if (delta == 0)
        {
            UpdateActionState();
            return;
        }

        if (delta < 0)
        {
            var pirateMessage = $"🏴‍☠️ Пользователя {user.Name} лично наказал СЕРЁГА ПИРАТ! ⚔️ Убрано {-delta} сообщений. 💀";
            MessageBox.Show(pirateMessage, "🏴‍☠️ Наказание от СЕРЁГИ ПИРАТА! ⚔️", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            try
            {
                _bot?.SendPunishmentMessage(user.Name, (ulong)-delta);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Не удалось отправить сообщение в чат: {exception.Message}", "Ошибка отправки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        var updated = delta > 0
            ? _statisticsCollector.IncrementUserMessages(user.UserId, (ulong)delta)
            : _statisticsCollector.DecrementUserMessages(user.UserId, (ulong)-delta);

        if (!updated)
        {
            MessageBox.Show("🔍 Пользователь не найден в статистике.", "🔍 Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        UpdateDetails(user);
        RefreshUserList();

        try
        {
            await _statisticsCollector.SaveNowAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"💾 Не удалось сохранить статистику: {ex.Message}", "💾 Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // TODO: Вынести глобально
    private static (string emoji, string level) GetChessPieceInfo(ulong messageCount)
    {
        return messageCount switch
        {
            >= 5000 => ("♔", "КОРОЛЬ"),
            >= 2500 => ("♛", "ФЕРЗЬ"),
            >= 1000 => ("♜", "ЛАДЬЯ"),
            >= 500 => ("♝", "СЛОН"),
            >= 250 => ("♞", "КОНЬ"),
            _ => ("♟", "ПЕШКА"),
        };
    }

    private void InitializeRuntime()
    {
        listBoxUsers.Format += (_, e) =>
        {
            if (e.ListItem is not UserStatistics user)
            {
                return;
            }

            var chessPiece = GetChessPieceInfo(user.MessageCount).emoji;
            e.Value = $"{chessPiece} {user.Name} ({user.MessageCount} 💬)";
        };

        numericIncrement.ValueChanged += (_, _) => UpdateActionState();

        UpdateDetails(null);
        UpdateActionState();
    }

    private void LoadData()
    {
        if (_statisticsCollector == null)
        {
            return;
        }

        _allUsers = _statisticsCollector.GetAllUsers();
        ApplyFilter();
    }

    private void RefreshUserList()
    {
        if (_statisticsCollector == null)
        {
            return;
        }

        var currentFilter = textBoxFilter.Text;
        var selectedUserId = (listBoxUsers.SelectedItem as UserStatistics)?.UserId;

        _allUsers = _statisticsCollector.GetAllUsers();

        textBoxFilter.Text = currentFilter;
        ApplyFilter();

        if (selectedUserId != null)
        {
            var userToSelect = _filteredUsers.FirstOrDefault(u => u.UserId == selectedUserId);

            if (userToSelect != null)
            {
                listBoxUsers.SelectedItem = userToSelect;
            }
        }
    }

    private void ApplyFilter()
    {
        var term = textBoxFilter.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(term))
        {
            _filteredUsers = _allUsers.ToList();
        }
        else
        {
            term = term.ToLowerInvariant();

            _filteredUsers = _allUsers
                .Where(x => x.Name.Contains(term, StringComparison.InvariantCultureIgnoreCase)
                            || x.UserId.Contains(term, StringComparison.InvariantCultureIgnoreCase))
                .ToList();
        }

        var selected = listBoxUsers.SelectedItem as UserStatistics;

        listBoxUsers.DataSource = null;
        listBoxUsers.DataSource = _filteredUsers;

        if (selected != null)
        {
            var keep = _filteredUsers.FirstOrDefault(x => x.UserId == selected.UserId);

            if (keep != null)
            {
                listBoxUsers.SelectedItem = keep;
            }
        }

        UpdateActionState();
    }

    private void UpdateDetails(UserStatistics? user)
    {
        if (user == null)
        {
            labelUserId.Text = "🆔 ID: —";
            labelUserName.Text = "👤 Имя: —";
            labelMessageCount.Text = "💬 Сообщений: 0";
            labelChessPiece.Text = "♟ ПЕШКА";
            return;
        }

        labelUserId.Text = $"🆔 ID: {user.UserId}";
        labelUserName.Text = $"👤 Имя: {user.Name}";
        labelMessageCount.Text = $"💬 Сообщений: {user.MessageCount}";

        var (emoji, level) = GetChessPieceInfo(user.MessageCount);
        labelChessPiece.Text = $"{emoji} {level}";
    }

    private void UpdateActionState()
    {
        var hasSelection = listBoxUsers.SelectedItem != null;
        var hasValue = numericIncrement.Value != 0;
        buttonAction.Enabled = hasSelection && hasValue;

        if (numericIncrement.Value > 0)
        {
            buttonAction.Text = $"➕ Добавить {numericIncrement.Value} сообщений";
        }
        else if (numericIncrement.Value < 0)
        {
            buttonAction.Text = $"➖ Убрать {-numericIncrement.Value} сообщений";
        }
        else
        {
            buttonAction.Text = "➕ Добавить сообщения";
        }
    }
}
