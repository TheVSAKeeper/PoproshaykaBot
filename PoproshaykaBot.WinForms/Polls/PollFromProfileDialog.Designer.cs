namespace PoproshaykaBot.WinForms.Polls;

partial class PollFromProfileDialog
{
    private System.ComponentModel.IContainer components = null;

    private TableLayoutPanel _mainLayout;
    private SplitContainer _split;
    private ListBox _profilesListBox;
    private Label _detailsLabel;
    private FlowLayoutPanel _buttonsFlow;
    private Button _okButton;
    private Button _cancelButton;
    private ContextMenuStrip _profileContextMenu;
    private ToolStripMenuItem _editMenuItem;
    private ToolStripMenuItem _deleteMenuItem;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _mainLayout = new TableLayoutPanel();
        _split = new SplitContainer();
        _profilesListBox = new ListBox();
        _profileContextMenu = new ContextMenuStrip(components);
        _editMenuItem = new ToolStripMenuItem();
        _deleteMenuItem = new ToolStripMenuItem();
        _detailsLabel = new Label();
        _buttonsFlow = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();
        _mainLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
        _split.Panel1.SuspendLayout();
        _split.Panel2.SuspendLayout();
        _split.SuspendLayout();
        _profileContextMenu.SuspendLayout();
        _buttonsFlow.SuspendLayout();
        SuspendLayout();
        // 
        // _mainLayout
        // 
        _mainLayout.ColumnCount = 1;
        _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainLayout.Controls.Add(_split, 0, 0);
        _mainLayout.Controls.Add(_buttonsFlow, 0, 1);
        _mainLayout.Dock = DockStyle.Fill;
        _mainLayout.Location = new Point(0, 0);
        _mainLayout.Name = "_mainLayout";
        _mainLayout.Padding = new Padding(8);
        _mainLayout.RowCount = 2;
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.Size = new Size(635, 389);
        _mainLayout.TabIndex = 0;
        // 
        // _split
        // 
        _split.Dock = DockStyle.Fill;
        _split.Location = new Point(8, 8);
        _split.Margin = new Padding(0);
        _split.Name = "_split";
        // 
        // _split.Panel1
        // 
        _split.Panel1.Controls.Add(_profilesListBox);
        // 
        // _split.Panel2
        // 
        _split.Panel2.Controls.Add(_detailsLabel);
        _split.Panel2.Padding = new Padding(8, 0, 0, 0);
        _split.Size = new Size(619, 340);
        _split.SplitterDistance = 225;
        _split.TabIndex = 0;
        // 
        // _profilesListBox
        // 
        _profilesListBox.ContextMenuStrip = _profileContextMenu;
        _profilesListBox.Dock = DockStyle.Fill;
        _profilesListBox.IntegralHeight = false;
        _profilesListBox.ItemHeight = 15;
        _profilesListBox.Location = new Point(0, 0);
        _profilesListBox.Name = "_profilesListBox";
        _profilesListBox.Size = new Size(225, 340);
        _profilesListBox.TabIndex = 0;
        _profilesListBox.SelectedIndexChanged += OnProfileSelectionChanged;
        _profilesListBox.DoubleClick += OnProfileDoubleClick;
        _profilesListBox.MouseDown += OnProfileMouseDown;
        // 
        // _profileContextMenu
        // 
        _profileContextMenu.Items.AddRange(new ToolStripItem[] { _editMenuItem, _deleteMenuItem });
        _profileContextMenu.Name = "_profileContextMenu";
        _profileContextMenu.Size = new Size(179, 48);
        _profileContextMenu.Opening += OnContextMenuOpening;
        // 
        // _editMenuItem
        // 
        _editMenuItem.Name = "_editMenuItem";
        _editMenuItem.Size = new Size(178, 22);
        _editMenuItem.Text = "✏ Редактировать…";
        _editMenuItem.Click += OnEditMenuClicked;
        // 
        // _deleteMenuItem
        // 
        _deleteMenuItem.Name = "_deleteMenuItem";
        _deleteMenuItem.Size = new Size(178, 22);
        _deleteMenuItem.Text = "− Удалить";
        _deleteMenuItem.Click += OnDeleteMenuClicked;
        // 
        // _detailsLabel
        // 
        _detailsLabel.Dock = DockStyle.Fill;
        _detailsLabel.ForeColor = Color.DimGray;
        _detailsLabel.Location = new Point(8, 0);
        _detailsLabel.Name = "_detailsLabel";
        _detailsLabel.Size = new Size(382, 340);
        _detailsLabel.TabIndex = 0;
        _detailsLabel.Text = "Нет выбранного профиля.";
        // 
        // _buttonsFlow
        // 
        _buttonsFlow.Anchor = AnchorStyles.Right;
        _buttonsFlow.AutoSize = true;
        _buttonsFlow.Controls.Add(_okButton);
        _buttonsFlow.Controls.Add(_cancelButton);
        _buttonsFlow.Location = new Point(436, 356);
        _buttonsFlow.Margin = new Padding(0, 8, 0, 0);
        _buttonsFlow.Name = "_buttonsFlow";
        _buttonsFlow.Size = new Size(191, 25);
        _buttonsFlow.TabIndex = 1;
        // 
        // _okButton
        // 
        _okButton.AutoSize = true;
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Enabled = false;
        _okButton.Location = new Point(0, 0);
        _okButton.Margin = new Padding(0, 0, 6, 0);
        _okButton.Name = "_okButton";
        _okButton.Size = new Size(110, 25);
        _okButton.TabIndex = 0;
        _okButton.Text = "Запустить";
        _okButton.Click += OnOkClicked;
        // 
        // _cancelButton
        // 
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(116, 0);
        _cancelButton.Margin = new Padding(0);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Size = new Size(75, 25);
        _cancelButton.TabIndex = 1;
        _cancelButton.Text = "Отмена";
        // 
        // PollFromProfileDialog
        // 
        AcceptButton = _okButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(635, 389);
        Controls.Add(_mainLayout);
        MinimizeBox = false;
        MinimumSize = new Size(480, 320);
        Name = "PollFromProfileDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Выбор профиля голосования";
        _mainLayout.ResumeLayout(false);
        _mainLayout.PerformLayout();
        _split.Panel1.ResumeLayout(false);
        _split.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_split).EndInit();
        _split.ResumeLayout(false);
        _profileContextMenu.ResumeLayout(false);
        _buttonsFlow.ResumeLayout(false);
        _buttonsFlow.PerformLayout();
        ResumeLayout(false);
    }
}
