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

    /// <summary>
    /// 「開く」ボタンがクリックされた際、入力欄のURL文字列を取得してダイアログを閉じ（結果を返し）ます。
    /// </summary>
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        InputUrl = this.FindControl<TextBox>("UrlTextBox")?.Text;
        Close(InputUrl);
    }

    /// <summary>
    /// 「キャンセル」ボタンがクリックされた際、結果としてnullを返してダイアログを閉じます。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
