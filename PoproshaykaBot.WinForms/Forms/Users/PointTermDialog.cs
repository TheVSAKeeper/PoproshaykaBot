using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.WinForms.Forms.Users;

public partial class PointTermDialog : Form
{
    public PointTermDialog()
    {
        InitializeComponent();
        UpdatePreview();
    }

    public void LoadFrom(PointTerm initial)
    {
        _singularTextBox.Text = initial.Singular;
        _fewTextBox.Text = initial.Few;
        _manyTextBox.Text = initial.Many;
        UpdatePreview();
    }

    public PointTerm BuildResult()
    {
        return new()
        {
            Singular = Sanitize(_singularTextBox.Text, "балл"),
            Few = Sanitize(_fewTextBox.Text, "балла"),
            Many = Sanitize(_manyTextBox.Text, "баллов"),
        };
    }

    private void OnInputChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void OnResetDefaultsClicked(object? sender, EventArgs e)
    {
        var defaults = new PointTerm();
        _singularTextBox.Text = defaults.Singular;
        _fewTextBox.Text = defaults.Few;
        _manyTextBox.Text = defaults.Many;
        UpdatePreview();
    }

    private static string Sanitize(string? input, string fallback)
    {
        var trimmed = input?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }

    private void UpdatePreview()
    {
        var sample = BuildResult();
        _previewValueLbl.Text = $"1 {sample.ForCount(1)} · 3 {sample.ForCount(3)} · 7 {sample.ForCount(7)} · 21 {sample.ForCount(21)}";
    }
}
