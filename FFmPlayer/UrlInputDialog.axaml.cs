using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FFmPlayer;

public partial class UrlInputDialog : Window
{
    public string? InputUrl { get; private set; }

    public UrlInputDialog()
    {
        InitializeComponent();
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        InputUrl = this.FindControl<TextBox>("UrlTextBox")?.Text;
        Close(InputUrl);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
