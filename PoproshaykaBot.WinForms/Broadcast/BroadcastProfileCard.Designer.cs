using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast
{
    partial class BroadcastProfileCard
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel _headerRow;
        private System.Windows.Forms.Label _activeBadge;
        private System.Windows.Forms.Label _nameLabel;
        private System.Windows.Forms.FlowLayoutPanel _actionsFlow;
        private System.Windows.Forms.Button _applyButton;
        private System.Windows.Forms.Button _editButton;
        private System.Windows.Forms.Button _menuButton;
        private System.Windows.Forms.Label _titleLabel;
        private System.Windows.Forms.Label _metaLabel;
        private System.Windows.Forms.Label _driftLabel;
        private System.Windows.Forms.ContextMenuStrip _menu;
        private System.Windows.Forms.ToolStripMenuItem _duplicateMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _deleteMenuItem;
        private System.Windows.Forms.ToolTip _toolTip;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _menu = new ContextMenuStrip(components);
            _duplicateMenuItem = new ToolStripMenuItem();
            _deleteMenuItem = new ToolStripMenuItem();
            _toolTip = new ToolTip(components);
            _headerRow = new TableLayoutPanel();
            _activeBadge = new Label();
            _nameLabel = new Label();
            _actionsFlow = new FlowLayoutPanel();
            _applyButton = new Button();
            _editButton = new Button();
            _menuButton = new Button();
            _titleLabel = new Label();
            _metaLabel = new Label();
            _driftLabel = new Label();
            _menu.SuspendLayout();
            _headerRow.SuspendLayout();
            _actionsFlow.SuspendLayout();
            SuspendLayout();
            // 
            // _menu
            // 
            _menu.Items.AddRange(new ToolStripItem[] { _duplicateMenuItem, _deleteMenuItem });
            _menu.Name = "_menu";
            _menu.Size = new Size(159, 48);
            // 
            // _duplicateMenuItem
            // 
            _duplicateMenuItem.Name = "_duplicateMenuItem";
            _duplicateMenuItem.Size = new Size(158, 22);
            _duplicateMenuItem.Text = "⎘ Дублировать";
            // 
            // _deleteMenuItem
            // 
            _deleteMenuItem.Name = "_deleteMenuItem";
            _deleteMenuItem.Size = new Size(158, 22);
            _deleteMenuItem.Text = "🗑 Удалить";
            // 
            // _headerRow
            // 
            _headerRow.AutoSize = true;
            _headerRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _headerRow.ColumnCount = 3;
            _headerRow.ColumnStyles.Add(new ColumnStyle());
            _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _headerRow.ColumnStyles.Add(new ColumnStyle());
            _headerRow.Controls.Add(_activeBadge, 0, 0);
            _headerRow.Controls.Add(_nameLabel, 1, 0);
            _headerRow.Controls.Add(_actionsFlow, 2, 0);
            _headerRow.Dock = DockStyle.Top;
            _headerRow.Location = new Point(8, 6);
            _headerRow.Margin = new Padding(0, 0, 0, 2);
            _headerRow.MinimumSize = new Size(0, 28);
            _headerRow.Name = "_headerRow";
            _headerRow.RowCount = 1;
            _headerRow.RowStyles.Add(new RowStyle());
            _headerRow.Size = new Size(302, 28);
            _headerRow.TabIndex = 4;
            // 
            // _activeBadge
            // 
            _activeBadge.AutoSize = true;
            _activeBadge.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _activeBadge.ForeColor = Color.SeaGreen;
            _activeBadge.Location = new Point(0, 0);
            _activeBadge.Margin = new Padding(0, 0, 4, 0);
            _activeBadge.Name = "_activeBadge";
            _activeBadge.Size = new Size(17, 15);
            _activeBadge.TabIndex = 0;
            _activeBadge.Text = "✓";
            _activeBadge.Visible = false;
            // 
            // _nameLabel
            // 
            _nameLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _nameLabel.AutoEllipsis = true;
            _nameLabel.AutoSize = true;
            _nameLabel.Location = new Point(21, 0);
            _nameLabel.Margin = new Padding(0);
            _nameLabel.Name = "_nameLabel";
            _nameLabel.Size = new Size(193, 15);
            _nameLabel.TabIndex = 1;
            // 
            // _actionsFlow
            // 
            _actionsFlow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _actionsFlow.AutoSize = true;
            _actionsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _actionsFlow.Controls.Add(_applyButton);
            _actionsFlow.Controls.Add(_editButton);
            _actionsFlow.Controls.Add(_menuButton);
            _actionsFlow.Location = new Point(214, 0);
            _actionsFlow.Margin = new Padding(0);
            _actionsFlow.Name = "_actionsFlow";
            _actionsFlow.Size = new Size(88, 26);
            _actionsFlow.TabIndex = 2;
            _actionsFlow.WrapContents = false;
            // 
            // _applyButton
            // 
            _applyButton.FlatStyle = FlatStyle.Flat;
            _applyButton.Location = new Point(0, 0);
            _applyButton.Margin = new Padding(0, 0, 2, 0);
            _applyButton.MinimumSize = new Size(28, 26);
            _applyButton.Name = "_applyButton";
            _applyButton.Size = new Size(28, 26);
            _applyButton.TabIndex = 0;
            _applyButton.Text = "↻";
            // 
            // _editButton
            // 
            _editButton.FlatStyle = FlatStyle.Flat;
            _editButton.Location = new Point(30, 0);
            _editButton.Margin = new Padding(0, 0, 2, 0);
            _editButton.MinimumSize = new Size(28, 26);
            _editButton.Name = "_editButton";
            _editButton.Size = new Size(28, 26);
            _editButton.TabIndex = 1;
            _editButton.Text = "✎";
            // 
            // _menuButton
            // 
            _menuButton.FlatStyle = FlatStyle.Flat;
            _menuButton.Location = new Point(60, 0);
            _menuButton.Margin = new Padding(0);
            _menuButton.MinimumSize = new Size(28, 26);
            _menuButton.Name = "_menuButton";
            _menuButton.Size = new Size(28, 26);
            _menuButton.TabIndex = 2;
            _menuButton.Text = "⋯";
            // 
            // _titleLabel
            // 
            _titleLabel.AutoEllipsis = true;
            _titleLabel.AutoSize = true;
            _titleLabel.Dock = DockStyle.Top;
            _titleLabel.ForeColor = Color.DimGray;
            _titleLabel.Location = new Point(8, 34);
            _titleLabel.Margin = new Padding(0, 0, 0, 2);
            _titleLabel.MaximumSize = new Size(0, 40);
            _titleLabel.Name = "_titleLabel";
            _titleLabel.Size = new Size(0, 15);
            _titleLabel.TabIndex = 3;
            _titleLabel.Visible = false;
            // 
            // _metaLabel
            // 
            _metaLabel.AutoSize = true;
            _metaLabel.Dock = DockStyle.Top;
            _metaLabel.ForeColor = Color.Gray;
            _metaLabel.Location = new Point(8, 49);
            _metaLabel.Margin = new Padding(0, 0, 0, 2);
            _metaLabel.Name = "_metaLabel";
            _metaLabel.Size = new Size(0, 15);
            _metaLabel.TabIndex = 2;
            _metaLabel.Visible = false;
            // 
            // _driftLabel
            // 
            _driftLabel.AutoSize = true;
            _driftLabel.Dock = DockStyle.Top;
            _driftLabel.ForeColor = Color.DarkOrange;
            _driftLabel.Location = new Point(8, 64);
            _driftLabel.Margin = new Padding(0);
            _driftLabel.Name = "_driftLabel";
            _driftLabel.Size = new Size(160, 15);
            _driftLabel.TabIndex = 1;
            _driftLabel.Text = "⚠ Стрим изменён вручную";
            _driftLabel.Visible = false;
            //
            // BroadcastProfileCard
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_driftLabel);
            Controls.Add(_metaLabel);
            Controls.Add(_titleLabel);
            Controls.Add(_headerRow);
            Margin = new Padding(4, 2, 4, 2);
            MinimumSize = new Size(320, 0);
            Name = "BroadcastProfileCard";
            Padding = new Padding(8, 6, 8, 6);
            Size = new Size(318, 85);
            _menu.ResumeLayout(false);
            _headerRow.ResumeLayout(false);
            _headerRow.PerformLayout();
            _actionsFlow.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
