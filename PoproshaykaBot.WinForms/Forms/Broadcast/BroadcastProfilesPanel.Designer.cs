namespace PoproshaykaBot.WinForms.Forms.Broadcast
{
    partial class BroadcastProfilesPanel
    {
        private System.ComponentModel.IContainer components = null;
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
            _cardsFlow = new System.Windows.Forms.FlowLayoutPanel();
            _emptyLabel = new System.Windows.Forms.Label();

            SuspendLayout();

            _cardsFlow.Name = "_cardsFlow";
            _cardsFlow.Dock = System.Windows.Forms.DockStyle.Fill;
            _cardsFlow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            _cardsFlow.WrapContents = true;
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

            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Name = "BroadcastProfilesPanel";
            Size = new System.Drawing.Size(700, 440);
            MinimumSize = new System.Drawing.Size(360, 200);

            ResumeLayout(false);
        }
    }
}
