using Microsoft.UI.Xaml.Controls;

namespace DownloadTray.App.ActivityCard;

public sealed partial class DownloadCardControl : UserControl
{
    public DownloadCardViewModel? ViewModel { get; private set; }

    public DownloadCardControl()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            ViewModel = args.NewValue as DownloadCardViewModel;
            Bindings.Update();
        };
    }
}
