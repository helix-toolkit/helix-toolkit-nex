using System.Windows;
using Interop.Common;

namespace WpfInterop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        ViewportFly.Rendering += (s, e) =>
        {
            if (ViewportFly.DataContext is MainViewModel vm)
            {
                vm.OnFlyRendering(s, e);
            }
        };
        ViewportOverhead.Rendering += (s, e) =>
        {
            if (ViewportOverhead.DataContext is MainViewModel vm)
            {
                vm.OnOverheadRendering(s, e);
            }
        };
    }
}
