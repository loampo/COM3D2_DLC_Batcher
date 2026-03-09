namespace COM3D2_DLC_Batcher.Models;

public record GameProfile(
    string DisplayName,
    string TargetApp,
    string ExcludeStr,
    string DlcPrefix,
    string LauncherExe);

public static class GameProfiles
{
    public static readonly GameProfile[] All =
    [
        new("It's a Night Magic",  "It's a Night Magic",      "",    "",            "COM3D2.exe"),
        new("COM3D2",              "CUSTOM ORDER MAID3D 2",   "2.5", "",            "COM3D2.exe"),
        new("COM3D2 2.5",          "CUSTOM ORDER MAID3D 2.5", "",    "",            "COM3D2.exe"),
        new("KC EditSystem",       "KC EditSystem",           "",    "dlc_creplg_", "CR Launcher.exe"),
    ];
}
