namespace OpenSteamKitten.Utils
{
    /// <summary>
    /// OpenSteamTool 内核 DLL 清单（安装/清理共用，避免多处硬编码漂移）。
    /// </summary>
    internal static class OpenSteamToolFiles
    {
        public static readonly string[] KernelDlls = { "OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll" };
    }
}
