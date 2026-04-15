using System.Windows;
using Interop.Common;

namespace WpfInterop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
