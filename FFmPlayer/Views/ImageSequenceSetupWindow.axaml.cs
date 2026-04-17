using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFmPlayer.ViewModels;
using System.Threading.Tasks;

namespace FFmPlayer.Views;

public partial class ImageSequenceSetupWindow : Window
{
    public ImageSequenceSetupWindow()
    {
        InitializeComponent();
        
        DataContextChanged += (s, e) =>
        {
            if (DataContext is ImageSequenceSetupViewModel vm)
            {
                vm.CloseAction = Close;
                vm.RequestBrowseFolderAsync = async () =>
                {
                    var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Select Image Sequence Folder",
                        AllowMultiple = false
                    });
                    
                    if (result != null && result.Count > 0)
                    {
                        return result[0].Path.LocalPath;
                    }
                    return null;
                };
            }
        };
    }
}
