using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Settings;

public sealed class SettingsSectionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MultiBlockTemplate { get; set; }

    public DataTemplate? SingleBlockTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SettingsSectionPresentation presentation)
        {
            return presentation.HasMultipleBlocks ? MultiBlockTemplate : SingleBlockTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
