using Avalonia.Controls;
using Avalonia.Interactivity;
using FFmPlayer.ViewModels;

namespace FFmPlayer;

public partial class PlaylistWindow : Window
{
    public PlaylistWindow()
    {
        InitializeComponent();
        
        // When selection occurs in the ListBox, we could automatically load it
        var listBox = this.FindControl<ListBox>("PlaylistListBox");
        if (listBox != null)
        {
            listBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is string url && DataContext is MainViewModel vm)
                {
                    vm.LoadMedia(url);
                }
            };
        }
    }
}
