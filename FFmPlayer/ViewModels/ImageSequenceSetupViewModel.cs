using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FFmPlayer.ViewModels;

public partial class ImageSequenceSetupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private string _selectedFpsString = "30";
    
    // FPSのドロップダウン用の基本選択肢
    public ObservableCollection<string> AvailableFpsOptions { get; } = new ObservableCollection<string>
    {
        "24", "30", "60"
    };

    public bool IsConfirmed { get; private set; }

    public ICommand BrowseCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    // Owner window (for dialog actions like Close)
    public System.Action? CloseAction { get; set; }
    
    // Function to invoke file dialog (injected by view)
    public System.Func<System.Threading.Tasks.Task<string?>>? RequestBrowseFolderAsync { get; set; }

    public ImageSequenceSetupViewModel()
    {
        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);
        ConfirmCommand = new RelayCommand(OnConfirm, CanConfirm);
        CancelCommand = new RelayCommand(OnCancel);

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FolderPath) || e.PropertyName == nameof(SelectedFpsString))
            {
                (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        };
    }

    private async System.Threading.Tasks.Task OnBrowseAsync()
    {
        if (RequestBrowseFolderAsync != null)
        {
            var folder = await RequestBrowseFolderAsync();
            if (!string.IsNullOrEmpty(folder))
            {
                FolderPath = folder;
            }
        }
    }

    private bool CanConfirm()
    {
        return !string.IsNullOrWhiteSpace(FolderPath) 
               && Directory.Exists(FolderPath)
               && double.TryParse(SelectedFpsString, out double fps)
               && fps > 0;
    }

    private void OnConfirm()
    {
        IsConfirmed = true;
        CloseAction?.Invoke();
    }

    private void OnCancel()
    {
        IsConfirmed = false;
        CloseAction?.Invoke();
    }
}
