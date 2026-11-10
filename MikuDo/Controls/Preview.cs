using System.Windows;

namespace MikuDo.Controls;

/// <summary>
/// The preview card an element shows while the pointer rests on it: the
/// template that draws its data, and whether the card has something on it to
/// use, so the pointer may move onto it.
/// </summary>
public static class Preview
{
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.RegisterAttached(
        "Template", typeof(DataTemplate), typeof(Preview), new PropertyMetadata(null));

    public static readonly DependencyProperty HasActionsProperty = DependencyProperty.RegisterAttached(
        "HasActions", typeof(bool), typeof(Preview), new PropertyMetadata(false));

    public static void SetTemplate(DependencyObject element, DataTemplate? value) => element.SetValue(TemplateProperty, value);
    public static DataTemplate? GetTemplate(DependencyObject element) => (DataTemplate?)element.GetValue(TemplateProperty);

    public static void SetHasActions(DependencyObject element, bool value) => element.SetValue(HasActionsProperty, value);
    public static bool GetHasActions(DependencyObject element) => (bool)element.GetValue(HasActionsProperty);
}
