using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public partial class UserStatisticsForm : Form
{
    private readonly StatisticsCollector? _statisticsCollector;
    private readonly UserRankService _userRankService;
    private Bot? _bot;
    private List<UserStatistics> _allUsers = [];
    private List<UserStatistics> _filteredUsers = [];

    public UserStatisticsForm(
        StatisticsCollector statisticsCollector,
        UserRankService userRankService,
        Bot? bot = null)
    {
        _statisticsCollector = statisticsCollector;
        _userRankService = userRankService;
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

        bool updated;

        var managementService = _bot?.MessagesManagementService;
        if (managementService == null)
        {
            MessageBox.Show("⚠️ Сервис управления сообщениями недоступен.", "⚠️ Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (delta < 0)
        {
            var notificationMessage = managementService.GetPunishmentNotification(user.Name, (ulong)-delta);
            MessageBox.Show(notificationMessage, "🏴‍☠️ Наказание от СЕРЁГИ ПИРАТА! ⚔️", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            try
            {
                updated = managementService.PunishUser(user.UserId, user.Name, (ulong)-delta, _bot?.Channel);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Не удалось наказать пользователя: {exception.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            var notificationMessage = managementService.GetRewardNotification(user.Name, (ulong)delta);
            MessageBox.Show(notificationMessage, "🎉 Поощрение от СЕРЁГИ ПИРАТА! 🏆", MessageBoxButtons.OK, MessageBoxIcon.Information);

            try
            {
                updated = managementService.RewardUser(user.UserId, user.Name, (ulong)delta, _bot?.Channel);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Не удалось поощрить пользователя: {exception.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

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

    private void InitializeRuntime()
    {
        listBoxUsers.Format += (_, e) =>
        {
            if (e.ListItem is not UserStatistics user)
            {
                return;
            }

            var rank = _userRankService.GetRank(user.MessageCount);
            e.Value = $"{rank.Emoji} {user.Name} ({user.MessageCount} 💬)";
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

        var rank = _userRankService.GetRank(user.MessageCount);
        labelChessPiece.Text = $"{rank.Emoji} {rank.Level}";
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
