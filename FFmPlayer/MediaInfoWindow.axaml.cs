using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FFmPlayer;

public partial class MediaInfoWindow : Window
{
    public MediaInfoWindow()
    {
        InitializeComponent();
    }

    public void SetInfo(string info)
    {
        var textBlock = this.FindControl<TextBlock>("InfoTextBlock");
        if (textBlock != null)
        {
            textBlock.Text = info;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
