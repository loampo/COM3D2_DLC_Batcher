using System.IO;
using System.Windows;
using System.Windows.Controls;
using COM3D2_DLC_Batcher.ViewModels;

namespace COM3D2_DLC_Batcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            vm.LogEntryAdded += OnLogEntryAdded;
        }
    }

    private void OnLogEntryAdded()
    {
        LogScrollViewer.ScrollToBottom();
    }

    private void Path_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && sender is TextBox textBox)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                var path = files[0];
                if (Directory.Exists(path))
                {
                    textBox.Text = path;
                }
                else if (File.Exists(path))
                {
                    textBox.Text = Path.GetDirectoryName(path);
                }
            }
        }
    }
}
