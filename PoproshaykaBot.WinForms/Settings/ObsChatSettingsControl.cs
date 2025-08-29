using Cyotek.Windows.Forms;

namespace PoproshaykaBot.WinForms.Settings;

public partial class ObsChatSettingsControl : UserControl
{
    private static readonly ObsChatSettings DefaultSettings = new();

    public ObsChatSettingsControl()
    {
        InitializeComponent();
        SetPlaceholders();
        InitializeAnimationControls();
    }

    public event EventHandler? SettingChanged;

    public void LoadSettings(ObsChatSettings settings)
    {
        _backgroundColorButton.BackColor = settings.BackgroundColor;
        _textColorButton.BackColor = settings.TextColor;
        _usernameColorButton.BackColor = settings.UsernameColor;
        _systemMessageColorButton.BackColor = settings.SystemMessageColor;
        _timestampColorButton.BackColor = settings.TimestampColor;

        UpdateColorButtons();

        _fontFamilyTextBox.Text = settings.FontFamily;
        _fontSizeNumeric.Value = settings.FontSize;
        _fontBoldCheckBox.Checked = settings.FontBold;

        _paddingNumeric.Value = settings.Padding;
        _marginNumeric.Value = settings.Margin;
        _borderRadiusNumeric.Value = settings.BorderRadius;

        _enableAnimationsCheckBox.Checked = settings.EnableAnimations;
        _animationDurationNumeric.Value = settings.AnimationDuration;

        _maxMessagesNumeric.Value = settings.MaxMessages;
        _showTimestampCheckBox.Checked = settings.ShowTimestamp;

        _emoteSizeNumeric.Value = settings.EmoteSizePixels;
        _badgeSizeNumeric.Value = settings.BadgeSizePixels;

        _showUserTypeBordersCheckBox.Checked = settings.ShowUserTypeBorders;
        _highlightFirstTimeUsersCheckBox.Checked = settings.HighlightFirstTimeUsers;
        _highlightMentionsCheckBox.Checked = settings.HighlightMentions;
        _enableMessageShadowsCheckBox.Checked = settings.EnableMessageShadows;
        _enableSpecialEffectsCheckBox.Checked = settings.EnableSpecialEffects;

        _enableSmoothScrollCheckBox.Checked = settings.EnableSmoothScroll;
        _autoScrollEnabledCheckBox.Checked = settings.AutoScrollEnabled;
        _scrollAnimationDurationNumeric.Value = settings.ScrollAnimationDuration;

        _enableMessageFadeOutCheckBox.Checked = settings.EnableMessageFadeOut;
        _messageLifetimeNumeric.Value = settings.MessageLifetimeSeconds;
        SetAnimationTypeInComboBox(_fadeOutAnimationComboBox, settings.FadeOutAnimationType);
        _fadeOutAnimationDurationNumeric.Value = settings.FadeOutAnimationDurationMs;

        SetAnimationTypeInComboBox(_userMessageAnimationComboBox, settings.UserMessageAnimation);
        SetAnimationTypeInComboBox(_botMessageAnimationComboBox, settings.BotMessageAnimation);
        SetAnimationTypeInComboBox(_systemMessageAnimationComboBox, settings.SystemMessageAnimation);
        SetAnimationTypeInComboBox(_broadcasterMessageAnimationComboBox, settings.BroadcasterMessageAnimation);
        SetAnimationTypeInComboBox(_firstTimeUserMessageAnimationComboBox, settings.FirstTimeUserMessageAnimation);
    }

    public void SaveSettings(ObsChatSettings settings)
    {
        settings.BackgroundColor = _backgroundColorButton.BackColor;
        settings.TextColor = _textColorButton.BackColor;
        settings.UsernameColor = _usernameColorButton.BackColor;
        settings.SystemMessageColor = _systemMessageColorButton.BackColor;
        settings.TimestampColor = _timestampColorButton.BackColor;

        settings.FontFamily = ValidateFontFamily(_fontFamilyTextBox.Text.Trim()) ?? DefaultSettings.FontFamily;
        settings.FontSize = (int)_fontSizeNumeric.Value;
        settings.FontBold = _fontBoldCheckBox.Checked;

        settings.Padding = (int)_paddingNumeric.Value;
        settings.Margin = (int)_marginNumeric.Value;
        settings.BorderRadius = (int)_borderRadiusNumeric.Value;

        settings.EnableAnimations = _enableAnimationsCheckBox.Checked;
        settings.AnimationDuration = (int)_animationDurationNumeric.Value;

        settings.MaxMessages = (int)_maxMessagesNumeric.Value;
        settings.ShowTimestamp = _showTimestampCheckBox.Checked;

        settings.EmoteSizePixels = (int)_emoteSizeNumeric.Value;
        settings.BadgeSizePixels = (int)_badgeSizeNumeric.Value;

        settings.ShowUserTypeBorders = _showUserTypeBordersCheckBox.Checked;
        settings.HighlightFirstTimeUsers = _highlightFirstTimeUsersCheckBox.Checked;
        settings.HighlightMentions = _highlightMentionsCheckBox.Checked;
        settings.EnableMessageShadows = _enableMessageShadowsCheckBox.Checked;
        settings.EnableSpecialEffects = _enableSpecialEffectsCheckBox.Checked;

        settings.EnableSmoothScroll = _enableSmoothScrollCheckBox.Checked;
        settings.AutoScrollEnabled = _autoScrollEnabledCheckBox.Checked;
        settings.ScrollAnimationDuration = (int)_scrollAnimationDurationNumeric.Value;

        settings.EnableMessageFadeOut = _enableMessageFadeOutCheckBox.Checked;
        settings.MessageLifetimeSeconds = (int)_messageLifetimeNumeric.Value;
        settings.FadeOutAnimationType = GetAnimationTypeFromComboBox(_fadeOutAnimationComboBox);
        settings.FadeOutAnimationDurationMs = (int)_fadeOutAnimationDurationNumeric.Value;

        settings.UserMessageAnimation = GetAnimationTypeFromComboBox(_userMessageAnimationComboBox);
        settings.BotMessageAnimation = GetAnimationTypeFromComboBox(_botMessageAnimationComboBox);
        settings.SystemMessageAnimation = GetAnimationTypeFromComboBox(_systemMessageAnimationComboBox);
        settings.BroadcasterMessageAnimation = GetAnimationTypeFromComboBox(_broadcasterMessageAnimationComboBox);
        settings.FirstTimeUserMessageAnimation = GetAnimationTypeFromComboBox(_firstTimeUserMessageAnimationComboBox);
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackgroundColorResetButtonClicked(object sender, EventArgs e)
    {
        _backgroundColorButton.BackColor = DefaultSettings.BackgroundColor;
        UpdateColorButtons();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextColorResetButtonClicked(object sender, EventArgs e)
    {
        _textColorButton.BackColor = DefaultSettings.TextColor;
        UpdateColorButtons();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUsernameColorResetButtonClicked(object sender, EventArgs e)
    {
        _usernameColorButton.BackColor = DefaultSettings.UsernameColor;
        UpdateColorButtons();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemMessageColorResetButtonClicked(object sender, EventArgs e)
    {
        _systemMessageColorButton.BackColor = DefaultSettings.SystemMessageColor;
        UpdateColorButtons();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimestampColorResetButtonClicked(object sender, EventArgs e)
    {
        _timestampColorButton.BackColor = DefaultSettings.TimestampColor;
        UpdateColorButtons();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontFamilyResetButtonClicked(object sender, EventArgs e)
    {
        _fontFamilyTextBox.Text = DefaultSettings.FontFamily;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontSizeResetButtonClicked(object sender, EventArgs e)
    {
        _fontSizeNumeric.Value = DefaultSettings.FontSize;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaddingResetButtonClicked(object sender, EventArgs e)
    {
        _paddingNumeric.Value = DefaultSettings.Padding;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMarginResetButtonClicked(object sender, EventArgs e)
    {
        _marginNumeric.Value = DefaultSettings.Margin;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBorderRadiusResetButtonClicked(object sender, EventArgs e)
    {
        _borderRadiusNumeric.Value = DefaultSettings.BorderRadius;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAnimationDurationResetButtonClicked(object sender, EventArgs e)
    {
        _animationDurationNumeric.Value = DefaultSettings.AnimationDuration;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnableAnimationsResetButtonClicked(object sender, EventArgs e)
    {
        _enableAnimationsCheckBox.Checked = DefaultSettings.EnableAnimations;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaxMessagesResetButtonClicked(object sender, EventArgs e)
    {
        _maxMessagesNumeric.Value = DefaultSettings.MaxMessages;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEmoteSizeResetButtonClicked(object sender, EventArgs e)
    {
        _emoteSizeNumeric.Value = DefaultSettings.EmoteSizePixels;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBadgeSizeResetButtonClicked(object sender, EventArgs e)
    {
        _badgeSizeNumeric.Value = DefaultSettings.BadgeSizePixels;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnScrollAnimationDurationResetButtonClicked(object sender, EventArgs e)
    {
        _scrollAnimationDurationNumeric.Value = 300;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnableMessageFadeOutResetButtonClicked(object sender, EventArgs e)
    {
        _enableMessageFadeOutCheckBox.Checked = DefaultSettings.EnableMessageFadeOut;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessageLifetimeResetButtonClicked(object sender, EventArgs e)
    {
        _messageLifetimeNumeric.Value = DefaultSettings.MessageLifetimeSeconds;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFadeOutAnimationResetButtonClicked(object sender, EventArgs e)
    {
        _fadeOutAnimationComboBox.SelectedIndex = 5; // Исчезновение (FadeOut)
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFadeOutAnimationDurationResetButtonClicked(object sender, EventArgs e)
    {
        _fadeOutAnimationDurationNumeric.Value = DefaultSettings.FadeOutAnimationDurationMs;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserMessageAnimationResetButtonClicked(object sender, EventArgs e)
    {
        SetAnimationTypeInComboBox(_userMessageAnimationComboBox, DefaultSettings.UserMessageAnimation);
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBotMessageAnimationResetButtonClicked(object sender, EventArgs e)
    {
        SetAnimationTypeInComboBox(_botMessageAnimationComboBox, DefaultSettings.BotMessageAnimation);
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemMessageAnimationResetButtonClicked(object sender, EventArgs e)
    {
        SetAnimationTypeInComboBox(_systemMessageAnimationComboBox, DefaultSettings.SystemMessageAnimation);
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBroadcasterMessageAnimationResetButtonClicked(object sender, EventArgs e)
    {
        SetAnimationTypeInComboBox(_broadcasterMessageAnimationComboBox, DefaultSettings.BroadcasterMessageAnimation);
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFirstTimeUserMessageAnimationResetButtonClicked(object sender, EventArgs e)
    {
        SetAnimationTypeInComboBox(_firstTimeUserMessageAnimationComboBox, DefaultSettings.FirstTimeUserMessageAnimation);
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackgroundColorButtonClicked(object sender, EventArgs e)
    {
        if (ShowColorPickerDialog(_backgroundColorButton))
        {
            UpdateColorButtons();
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTextColorButtonClicked(object sender, EventArgs e)
    {
        if (ShowColorPickerDialog(_textColorButton))
        {
            UpdateColorButtons();
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnUsernameColorButtonClicked(object sender, EventArgs e)
    {
        if (ShowColorPickerDialog(_usernameColorButton))
        {
            UpdateColorButtons();
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSystemMessageColorButtonClicked(object sender, EventArgs e)
    {
        if (ShowColorPickerDialog(_systemMessageColorButton))
        {
            UpdateColorButtons();
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTimestampColorButtonClicked(object sender, EventArgs e)
    {
        if (ShowColorPickerDialog(_timestampColorButton))
        {
            UpdateColorButtons();
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPlaceholders()
    {
        _fontFamilyTextBox.PlaceholderText = DefaultSettings.FontFamily;
    }

    private void InitializeAnimationControls()
    {
        var messageComboBoxes = new[]
        {
            _userMessageAnimationComboBox,
            _botMessageAnimationComboBox,
            _systemMessageAnimationComboBox,
            _broadcasterMessageAnimationComboBox,
            _firstTimeUserMessageAnimationComboBox,
            _fadeOutAnimationComboBox,
        };

        foreach (var comboBox in messageComboBoxes)
        {
            comboBox.Items.AddRange(MessageAnimationType.DisplayNames);
            comboBox.SelectedIndexChanged += OnSettingChanged;
        }
    }

    private string GetAnimationTypeFromComboBox(ComboBox comboBox)
    {
        return comboBox.SelectedIndex switch
        {
            0 => MessageAnimationType.None,
            1 => MessageAnimationType.SlideInRight,
            2 => MessageAnimationType.SlideInLeft,
            3 => MessageAnimationType.FadeInUp,
            4 => MessageAnimationType.BounceIn,
            5 => MessageAnimationType.FadeOut,
            6 => MessageAnimationType.SlideOutLeft,
            7 => MessageAnimationType.SlideOutRight,
            8 => MessageAnimationType.ScaleDown,
            9 => MessageAnimationType.ShrinkUp,
            _ => MessageAnimationType.SlideInRight,
        };
    }

    private void SetAnimationTypeInComboBox(ComboBox comboBox, string animationType)
    {
        var index = animationType switch
        {
            MessageAnimationType.None => 0,
            MessageAnimationType.SlideInRight => 1,
            MessageAnimationType.SlideInLeft => 2,
            MessageAnimationType.FadeInUp => 3,
            MessageAnimationType.BounceIn => 4,
            MessageAnimationType.FadeOut => 5,
            MessageAnimationType.SlideOutLeft => 6,
            MessageAnimationType.SlideOutRight => 7,
            MessageAnimationType.ScaleDown => 8,
            MessageAnimationType.ShrinkUp => 9,
            _ => 1,
        };

        comboBox.SelectedIndex = index;
    }

    private string? ValidateFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return null;
        }

        if (fontFamily.Contains(';') || fontFamily.Contains('"') || fontFamily.Contains('\''))
        {
            return null;
        }

        return fontFamily;
    }

    private bool ShowColorPickerDialog(Button colorButton)
    {
        using var colorPickerDialog = new ColorPickerDialog();
        colorPickerDialog.Color = colorButton.BackColor;
        colorPickerDialog.ShowAlphaChannel = true;

        if (colorPickerDialog.ShowDialog(this) == DialogResult.OK)
        {
            colorButton.BackColor = colorPickerDialog.Color;
            return true;
        }

        return false;
    }

    private void UpdateColorButtons()
    {
        _backgroundColorButton.Text = GetColorDisplayText(_backgroundColorButton.BackColor);
        _textColorButton.Text = GetColorDisplayText(_textColorButton.BackColor);
        _usernameColorButton.Text = GetColorDisplayText(_usernameColorButton.BackColor);
        _systemMessageColorButton.Text = GetColorDisplayText(_systemMessageColorButton.BackColor);
        _timestampColorButton.Text = GetColorDisplayText(_timestampColorButton.BackColor);
    }

    private string GetColorDisplayText(Color color)
    {
        return ColorTranslator.ToHtml(color);
    }
}
