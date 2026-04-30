using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Statistics;
using System.Collections;

namespace PoproshaykaBot.WinForms.Users;

public sealed partial class UserStatisticsForm : Form
{
    private readonly StatisticsCollector _statisticsCollector;
    private readonly UserRankService _userRankService;
    private readonly UserMessagesManagementService _userMessagesManagementService;
    private readonly IChannelProvider _channelProvider;

    private List<UserStatistics> _allUsers = [];
    private bool _initialized;

    public UserStatisticsForm(
        StatisticsCollector statisticsCollector,
        UserRankService userRankService,
        UserMessagesManagementService userMessagesManagementService,
        IChannelProvider channelProvider)
    {
        _statisticsCollector = statisticsCollector;
        _userRankService = userRankService;
        _userMessagesManagementService = userMessagesManagementService;
        _channelProvider = channelProvider;

        InitializeComponent();
    }

    private UserStatisticsForm()
    {
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        UpdateDetails(null);
        UpdateActionState();
        LoadData();
    }

    private void textBoxFilter_TextChanged(object sender, EventArgs e)
    {
        ApplyFilter();
    }

    private void listViewUsers_SelectedIndexChanged(object sender, EventArgs e)
    {
        var user = listViewUsers.SelectedItems.Count > 0
            ? listViewUsers.SelectedItems[0].Tag as UserStatistics
            : null;

        UpdateDetails(user);
        UpdateActionState();
    }

    private async void buttonAction_Click(object sender, EventArgs e)
    {
        if (listViewUsers.SelectedItems.Count == 0 || listViewUsers.SelectedItems[0].Tag is not UserStatistics user)
        {
            MessageBox.Show("⚠️ Не выбран пользователь.", "⚠️ Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var delta = (long)numericIncrement.Value;

        if (delta == 0)
        {
            UpdateActionState();
            return;
        }

        bool updated;

        try
        {
            updated = await ApplyAdjustmentAsync(user, delta);
        }
        catch (Exception exception)
        {
            var verb = delta < 0 ? "наказать" : "поощрить";
            MessageBox.Show($"Не удалось {verb} пользователя: {exception.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
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

    private void ButtonClearFilterOnClick(object? sender, EventArgs e)
    {
        textBoxFilter.Text = string.Empty;
    }

    private void listViewUsers_ColumnClick(object sender, ColumnClickEventArgs e)
    {
        var nextOrder = SortOrder.Ascending;

        if (listViewUsers.Tag is int currentSortCol
            && currentSortCol == e.Column
            && listViewUsers.ListViewItemSorter is ListViewItemComparer comparer
            && comparer.Order == SortOrder.Ascending)
        {
            nextOrder = SortOrder.Descending;
        }

        listViewUsers.Tag = e.Column;
        listViewUsers.ListViewItemSorter = new ListViewItemComparer(e.Column, nextOrder);
    }

    private void listViewUsers_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (listViewUsers.SelectedItems.Count == 0)
        {
            return;
        }

        numericIncrement.Focus();
        numericIncrement.Select(0, numericIncrement.Text.Length);
    }

    private void textBoxFilter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || listViewUsers.Items.Count == 0)
        {
            return;
        }

        listViewUsers.Items[0].Selected = true;
        listViewUsers.Focus();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void numericIncrement_ValueChanged(object sender, EventArgs e)
    {
        UpdateActionState();
    }

    private void numericIncrement_KeyUp(object sender, KeyEventArgs e)
    {
        UpdateActionState();
    }

    private void numericIncrement_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || !buttonAction.Enabled)
        {
            return;
        }

        buttonAction.PerformClick();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private Task<bool> ApplyAdjustmentAsync(UserStatistics user, long delta)
    {
        if (delta < 0)
        {
            var amount = (ulong)-delta;
            var notification = _userMessagesManagementService.GetPunishmentNotification(user.Name, amount);
            MessageBox.Show(notification, "🏴‍☠️ Наказание от СЕРЁГИ ПИРАТА! ⚔️", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return _userMessagesManagementService.PunishUserAsync(user.UserId, user.Name, amount, _channelProvider.Channel);
        }
        else
        {
            var amount = (ulong)delta;
            var notification = _userMessagesManagementService.GetRewardNotification(user.Name, amount);
            MessageBox.Show(notification, "🎉 Поощрение от СЕРЁГИ ПИРАТА! 🏆", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return _userMessagesManagementService.RewardUserAsync(user.UserId, user.Name, amount, _channelProvider.Channel);
        }
    }

    private void LoadData()
    {
        _allUsers = _statisticsCollector.GetAllUsers();
        UpdateGlobalStats();
        ApplyFilter();
    }

    private void RefreshUserList()
    {
        _allUsers = _statisticsCollector.GetAllUsers();
        UpdateGlobalStats();
        ApplyFilter();

        if (listViewUsers.SelectedItems.Count > 0)
        {
            listViewUsers.SelectedItems[0].EnsureVisible();
        }
    }

    private void ApplyFilter()
    {
        var term = textBoxFilter.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(term)
            ? _allUsers
            : _allUsers.Where(x =>
                x.Name.Contains(term, StringComparison.InvariantCultureIgnoreCase)
                || x.UserId.Contains(term, StringComparison.InvariantCultureIgnoreCase));

        var selectedUserId = listViewUsers.SelectedItems.Count > 0
            ? (listViewUsers.SelectedItems[0].Tag as UserStatistics)?.UserId
            : null;

        listViewUsers.BeginUpdate();
        listViewUsers.Items.Clear();

        foreach (var user in filtered)
        {
            var rank = _userRankService.GetRank(user.TotalMessageCount);
            var item = new ListViewItem(user.Name)
            {
                Tag = user,
            };

            item.SubItems.Add(user.TotalMessageCount.ToString());
            item.SubItems.Add($"{rank.Emoji} {rank.DisplayName}");

            listViewUsers.Items.Add(item);

            if (selectedUserId != null && user.UserId == selectedUserId)
            {
                item.Selected = true;
            }
        }

        listViewUsers.EndUpdate();

        UpdateActionState();
    }

    private void UpdateGlobalStats()
    {
        var totalUsers = _allUsers.Count;
        var totalMessages = (long)_allUsers.Sum(u => (decimal)u.TotalMessageCount);
        var totalWritten = (long)_allUsers.Sum(u => (decimal)u.MessageCount);
        var totalBonus = (long)_allUsers.Sum(u => (decimal)u.BonusMessageCount);
        var totalPenalty = (long)_allUsers.Sum(u => (decimal)u.ShtrafMessageCount);

        labelGlobalUsers.Text = $"👥 Пользователей: {totalUsers:N0}";
        labelGlobalTotal.Text = $"💬 Всего сообщ: {totalMessages:N0}";
        labelGlobalWritten.Text = $"✍️ Написано: {totalWritten:N0}";
        labelGlobalBonus.Text = $"🎁 Бонус: {totalBonus:N0} / 🚫 Штраф: {totalPenalty:N0}";
    }

    private void UpdateDetails(UserStatistics? user)
    {
        labelUserId.Text = $"🆔 ID: {user?.UserId ?? "—"}";
        labelUserName.Text = $"👤 Имя: {user?.Name ?? "—"}";
        labelMessageTotal.Text = $"💬 Всего: {user?.TotalMessageCount:N0}";
        labelMessageWritten.Text = $"    ✍️ Написано: {user?.MessageCount:N0}";
        labelMessageBonus.Text = $"    🎁 Бонус: {user?.BonusMessageCount:N0}";
        labelMessagePenalty.Text = $"    🚫 Штраф: {user?.ShtrafMessageCount:N0}";

        var rank = _userRankService.GetRank(user?.TotalMessageCount ?? 0);
        labelChessPiece.Text = $"{rank.Emoji} {rank.DisplayName}";
    }

    private void UpdateActionState()
    {
        var hasSelection = listViewUsers.SelectedItems.Count > 0;
        var hasValue = numericIncrement.Value != 0;
        buttonAction.Enabled = hasSelection && hasValue;

        buttonAction.Text = numericIncrement.Value switch
        {
            > 0 => $"➕ Добавить {numericIncrement.Value} сообщений",
            < 0 => $"➖ Убрать {-numericIncrement.Value} сообщений",
            _ => "➕ Добавить сообщения",
        };
    }

    private sealed class ListViewItemComparer(int column, SortOrder order) : IComparer
    {
        public SortOrder Order { get; } = order;

        public int Compare(object? x, object? y)
        {
            var itemX = (ListViewItem)x!;
            var itemY = (ListViewItem)y!;
            var textX = itemX.SubItems[column].Text;
            var textY = itemY.SubItems[column].Text;

            int returnVal;

            if (column == 1 && long.TryParse(textX, out var valX) && long.TryParse(textY, out var valY))
            {
                returnVal = valX.CompareTo(valY);
            }
            else
            {
                returnVal = string.Compare(textX, textY, StringComparison.Ordinal);
            }

            return Order == SortOrder.Descending ? -returnVal : returnVal;
        }
    }
}
