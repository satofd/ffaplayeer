using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FFmPlayer;

public partial class MediaInfoWindow : Window
{
    public MediaInfoWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ウィンドウ内のテキストブロックに指定されたプロパティ情報（解像度等）を表示します。
    /// </summary>
    /// <param name="info">表示対象の文字列群</param>
    public void SetInfo(string info)
    {
        var textBlock = this.FindControl<TextBlock>("InfoTextBlock");
        if (textBlock != null)
        {
            textBlock.Text = info;
        }
    }

    /// <summary>
    /// 閉じるボタンがクリックされた際にウィンドウを閉じます。
    /// </summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
