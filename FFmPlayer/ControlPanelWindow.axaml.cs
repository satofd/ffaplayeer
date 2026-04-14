using Avalonia.Controls;
using System.ComponentModel;

namespace FFmPlayer;

public partial class ControlPanelWindow : Window
{
    public ControlPanelWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Prevent destruction of window so we can reopen it without re-instantiating, unless app is shutting down
        // Or simply hide it and cancel the close
        e.Cancel = true;
        this.Hide();
    }

    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }
}
