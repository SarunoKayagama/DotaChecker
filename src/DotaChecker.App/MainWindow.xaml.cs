using DotaChecker.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DotaChecker.App;

/// <summary>Главное окно. ViewModel внедряется через DI.</summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
    }
}
