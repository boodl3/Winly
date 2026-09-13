using System.Windows;

namespace Winly.App.Panel;

/// <summary>Plain-language disclosure shown before any capture on first launch (FR-026).</summary>
public partial class FirstRunDisclosureWindow : Window
{
    public FirstRunDisclosureWindow() => InitializeComponent();

    private void OnContinue(object sender, RoutedEventArgs eventArgs) => DialogResult = true;

    private void OnQuit(object sender, RoutedEventArgs eventArgs) => DialogResult = false;
}
