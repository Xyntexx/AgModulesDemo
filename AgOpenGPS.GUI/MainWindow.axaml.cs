using Avalonia.Controls;
using AgOpenGPS.GUI.ViewModels;

namespace AgOpenGPS.GUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
}
