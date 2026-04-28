namespace PoproshaykaBot.WinForms.Broadcast
{
    partial class BroadcastProfilesPanel
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel _header;
        private System.Windows.Forms.FlowLayoutPanel _headerFlow;
        private System.Windows.Forms.Button _addButton;
        private System.Windows.Forms.Button _importButton;
        private System.Windows.Forms.Button _editCurrentButton;
        private System.Windows.Forms.Label _statusLabel;
        private System.Windows.Forms.FlowLayoutPanel _cardsFlow;
        private System.Windows.Forms.Label _emptyLabel;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _header = new System.Windows.Forms.Panel();
            _headerFlow = new System.Windows.Forms.FlowLayoutPanel();
            _addButton = new System.Windows.Forms.Button();
            _importButton = new System.Windows.Forms.Button();
            _editCurrentButton = new System.Windows.Forms.Button();
            _statusLabel = new System.Windows.Forms.Label();
            _cardsFlow = new System.Windows.Forms.FlowLayoutPanel();
            _emptyLabel = new System.Windows.Forms.Label();

            _header.SuspendLayout();
            _headerFlow.SuspendLayout();
            SuspendLayout();

            _header.Name = "_header";
            _header.Dock = System.Windows.Forms.DockStyle.Top;
            _header.Height = 36;
            _header.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);

            _headerFlow.Name = "_headerFlow";
            _headerFlow.Dock = System.Windows.Forms.DockStyle.Fill;
            _headerFlow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            _headerFlow.WrapContents = false;
            _headerFlow.AutoSize = false;

            _addButton.Name = "_addButton";
            _addButton.Text = "+ Добавить";
            _addButton.AutoSize = true;
            _addButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);

            _importButton.Name = "_importButton";
            _importButton.Text = "⇪ Импорт";
            _importButton.AutoSize = true;
            _importButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);

            _editCurrentButton.Name = "_editCurrentButton";
            _editCurrentButton.Text = "✎ Текущие";
            _editCurrentButton.AutoSize = true;
            _editCurrentButton.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);

            _statusLabel.Name = "_statusLabel";
            _statusLabel.AutoSize = true;
            _statusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            _statusLabel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            _statusLabel.Text = string.Empty;

            _headerFlow.Controls.Add(_addButton);
            _headerFlow.Controls.Add(_importButton);
            _headerFlow.Controls.Add(_editCurrentButton);
            _headerFlow.Controls.Add(_statusLabel);

            _header.Controls.Add(_headerFlow);

            _cardsFlow.Name = "_cardsFlow";
            _cardsFlow.Dock = System.Windows.Forms.DockStyle.Fill;
            _cardsFlow.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            _cardsFlow.WrapContents = false;
            _cardsFlow.AutoScroll = true;
            _cardsFlow.Padding = new System.Windows.Forms.Padding(4);

            _emptyLabel.Name = "_emptyLabel";
            _emptyLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            _emptyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _emptyLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            _emptyLabel.Text = "Нет профилей. Нажмите «+ Добавить», чтобы создать первый.";
            _emptyLabel.Visible = false;

            Controls.Add(_cardsFlow);
            Controls.Add(_emptyLabel);
            Controls.Add(_header);

            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Name = "BroadcastProfilesPanel";
            Size = new System.Drawing.Size(700, 440);
            MinimumSize = new System.Drawing.Size(360, 200);

            _headerFlow.ResumeLayout(false);
            _headerFlow.PerformLayout();
            _header.ResumeLayout(false);
            _header.PerformLayout();
            ResumeLayout(false);
        }
    }
}
