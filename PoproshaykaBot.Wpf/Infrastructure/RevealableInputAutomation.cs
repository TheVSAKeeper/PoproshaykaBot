using KeepShell.Views.Controls;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Infrastructure;

/// <summary>
/// Переносит <see cref="AutomationProperties.NameProperty"/> с <see cref="RevealableTextBox"/>
/// на внутренние <see cref="PasswordBox"/> и <see cref="TextBox"/>, чтобы экранный диктор и тесты
/// доступности видели имя редактора независимо от режима раскрытия.
/// </summary>
public static class RevealableInputAutomation
{
    public static readonly DependencyProperty PropagateNameProperty = DependencyProperty.RegisterAttached(
        "PropagateName",
        typeof(bool),
        typeof(RevealableInputAutomation),
        new PropertyMetadata(false, OnPropagateNameChanged));

    public static bool GetPropagateName(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(PropagateNameProperty);
    }

    public static void SetPropagateName(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(PropagateNameProperty, value);
    }

    private static void OnPropagateNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RevealableTextBox control)
        {
            return;
        }

        control.Initialized -= OnControlInitializedOrLoaded;
        control.Loaded -= OnControlInitializedOrLoaded;

        if ((bool)e.NewValue)
        {
            control.Initialized += OnControlInitializedOrLoaded;
            control.Loaded += OnControlInitializedOrLoaded;
            ApplyName(control);
        }
    }

    private static void OnControlInitializedOrLoaded(object? sender, EventArgs e)
    {
        if (sender is RevealableTextBox control)
        {
            ApplyName(control);
        }
    }

    private static void ApplyName(RevealableTextBox control)
    {
        var name = AutomationProperties.GetName(control);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (control.FindName("Pwd") is PasswordBox passwordBox)
        {
            AutomationProperties.SetName(passwordBox, name);
        }

        if (control.FindName("Plain") is TextBox plainBox)
        {
            AutomationProperties.SetName(plainBox, name);
        }
    }
}
