using System.Windows;
using System.Windows.Controls;

namespace ClusterJAM.Desktop.App.Infrastructure;

// WPF PasswordBox has no bindable Password property. Keep this bridge in presentation only.
public static class PasswordBinding
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PasswordBinding), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(string), typeof(PasswordBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBinding), new PropertyMetadata(false));

    public static string GetValue(DependencyObject target) => (string)target.GetValue(ValueProperty);
    public static void SetValue(DependencyObject target, string value) => target.SetValue(ValueProperty, value);
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not PasswordBox box)
        {
            return;
        }
        // Subscribe even when both the source and target start at the default empty string.
        // A Value change callback alone never runs for that initial binding value.
        box.PasswordChanged -= OnPasswordChanged;
        if ((bool)args.NewValue)
        {
            box.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not PasswordBox box || (bool)box.GetValue(IsUpdatingProperty))
        {
            return;
        }
        box.SetValue(IsUpdatingProperty, true);
        try
        {
            box.Password = (string?)args.NewValue ?? string.Empty;
        }
        finally
        {
            box.SetValue(IsUpdatingProperty, false);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs args)
    {
        var box = (PasswordBox)sender;
        if ((bool)box.GetValue(IsUpdatingProperty))
        {
            return;
        }
        box.SetValue(IsUpdatingProperty, true);
        try
        {
            box.SetCurrentValue(ValueProperty, box.Password);
        }
        finally
        {
            box.SetValue(IsUpdatingProperty, false);
        }
    }
}
