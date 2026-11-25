using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MaxCube.UI.Desktop;

/// <summary>Hinweisfenster für die zweite Instanz; rein im Code, damit es ohne ViewModel auskommt.</summary>
internal sealed class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow(string title, string message, string okText)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;

        var ok = new Button { Content = okText, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                ok,
            },
        };
    }
}
