using System.Windows.Media;

namespace COM3D2_DLC_Batcher.Models;

public class LogEntry
{
    public string Text { get; }
    public Brush Brush { get; }

    public LogEntry(string text, Brush brush)
    {
        Text = text;
        Brush = brush;
    }
}
