using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace DngOpcodesEditor.Avalonia;

// Avalonia has no built-in MessageBox. This minimal modal dialog covers the
// error / info popups the WPF version used via System.Windows.MessageBox.
public static class MessageDialog
{
    public static Task Show(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };
        var ok = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(20, 5)
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(15),
            Spacing = 15,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                ok
            }
        };
        return dialog.ShowDialog(owner);
    }
}
