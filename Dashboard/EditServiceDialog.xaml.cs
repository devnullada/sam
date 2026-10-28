using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
        TxtCommand.Text = entry.Command;
        TxtWorkDir.Text = entry.WorkingDirectory ?? "";
        TxtDescription.Text = entry.Description ?? "";
        ChkAutoStart.IsChecked = entry.AutoStart;
        Title = isNew ? "New Service" : "Edit Service";
        Loaded += (_, _) => { TxtName.Focus(); TxtName.SelectAll(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OnSave(this, new RoutedEventArgs());
            }
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = new ServiceEntry
        {
            Name = TxtName.Text.Trim(),
            Command = TxtCommand.Text,
            WorkingDirectory = string.IsNullOrWhiteSpace(TxtWorkDir.Text) ? null : TxtWorkDir.Text,
            Description = string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim(),
            AutoStart = ChkAutoStart.IsChecked == true
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
