using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace GSBT.WinUI.Controls;

public sealed class GameRowContextRequestedEventArgs : EventArgs
{
    public GameRowContextRequestedEventArgs(GameRowViewModel viewModel, FrameworkElement placementTarget, Point position)
    {
        ViewModel = viewModel;
        PlacementTarget = placementTarget;
        Position = position;
    }

    public GameRowViewModel ViewModel { get; }

    public FrameworkElement PlacementTarget { get; }

    public Point Position { get; }
}
