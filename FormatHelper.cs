namespace COM3D2_DLC_Batcher.Helpers;

public static class FormatHelper
{
    public static string Bytes(long b) => b switch
    {
        < 1024L                => $"{b} B",
        < 1024L * 1024         => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024  => $"{b / (1024.0 * 1024):F1} MB",
        _                      => $"{b / (1024.0 * 1024 * 1024):F2} GB"
    };
}
