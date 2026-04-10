using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ServiceManager.Shared;

namespace ServiceManager.Dashboard;

public partial class EditServiceDialog : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public ServiceEntry? Result { get; private set; }

    public EditServiceDialog(ServiceEntry entry, bool isNew = false)
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        TxtName.Text = entry.Name;
        TxtName.IsReadOnly = !isNew;
        TxtName.Background = isNew
            ? (System.Windows.Media.Brush)FindResource("MainBg")
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3c, 0x3c, 0x3c));
        TxtCommand.Text = entry.Command;
        TxtWorkDir.Text = entry.WorkingDirectory ?? "";
        ChkAutoStart.IsChecked = entry.AutoStart;
        Title = isNew ? "New Service" : "Edit Service";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = new ServiceEntry
        {
            Name = TxtName.Text,
            Command = TxtCommand.Text,
            WorkingDirectory = string.IsNullOrWhiteSpace(TxtWorkDir.Text) ? null : TxtWorkDir.Text,
            AutoStart = ChkAutoStart.IsChecked == true
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
