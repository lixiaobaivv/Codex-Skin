using Microsoft.Win32;

internal static class DreamSkinShellIntegration
{
    private const string ProtocolKey = @"Software\Classes\dreamskin";
    private const string ExtensionKey = @"Software\Classes\.dreamskin";
    private const string FileTypeKey = @"Software\Classes\CodexThemeStore.dreamskin";

    public static void Register()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径。");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DSI_PROTOCOL_INVALID_HOST: 请使用已发布的 Codex-Skin.exe 注册协议，不能注册 dotnet.exe 宿主。");
        }
        using (var existingCommand = Registry.CurrentUser.OpenSubKey($@"{ProtocolKey}\shell\open\command"))
        {
            if (existingCommand?.GetValue(null) is string existing
                && !existing.Contains(executable, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("DSI_PROTOCOL_CONFLICT: dreamskin:// 已由其他客户端注册，请先在该客户端中解除关联。");
            }
        }
        using (var existingExtension = Registry.CurrentUser.OpenSubKey(ExtensionKey))
        {
            if (existingExtension?.GetValue(null) is string existing
                && !existing.Equals("CodexThemeStore.dreamskin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("DSI_PROTOCOL_CONFLICT: .dreamskin 已由其他客户端关联，请先解除原有关联。");
            }
        }
        var iconValue = $"\"{executable}\",0";

        using (var protocol = Registry.CurrentUser.CreateSubKey(ProtocolKey, writable: true))
        {
            protocol.SetValue(null, "URL:Dream Skin Import Protocol", RegistryValueKind.String);
            protocol.SetValue("URL Protocol", "", RegistryValueKind.String);
            using var icon = protocol.CreateSubKey("DefaultIcon", writable: true);
            icon.SetValue(null, iconValue, RegistryValueKind.String);
            using var command = protocol.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(null, $"\"{executable}\" \"%1\"", RegistryValueKind.String);
        }

        using (var extension = Registry.CurrentUser.CreateSubKey(ExtensionKey, writable: true))
        {
            extension.SetValue(null, "CodexThemeStore.dreamskin", RegistryValueKind.String);
        }
        using (var fileType = Registry.CurrentUser.CreateSubKey(FileTypeKey, writable: true))
        {
            fileType.SetValue(null, "Codex Dream Skin Theme Package", RegistryValueKind.String);
            using var icon = fileType.CreateSubKey("DefaultIcon", writable: true);
            icon.SetValue(null, iconValue, RegistryValueKind.String);
            using var command = fileType.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(null, $"\"{executable}\" \"%1\"", RegistryValueKind.String);
        }
    }

    public static void Unregister()
    {
        var executable = Environment.ProcessPath;
        if (executable is null) return;
        var ownsProtocol = false;
        using (var command = Registry.CurrentUser.OpenSubKey($@"{ProtocolKey}\shell\open\command"))
        {
            ownsProtocol = command?.GetValue(null) is string value
                && value.Contains(executable, StringComparison.OrdinalIgnoreCase);
        }
        if (ownsProtocol) Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);

        var ownsExtension = false;
        using (var extension = Registry.CurrentUser.OpenSubKey(ExtensionKey))
        {
            ownsExtension = extension?.GetValue(null) is string value
                && value.Equals("CodexThemeStore.dreamskin", StringComparison.OrdinalIgnoreCase);
        }
        if (ownsExtension)
        {
            Registry.CurrentUser.DeleteSubKeyTree(ExtensionKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(FileTypeKey, throwOnMissingSubKey: false);
        }
    }

    public static bool IsRegistered()
    {
        var executable = Environment.ProcessPath;
        if (executable is null) return false;
        using var command = Registry.CurrentUser.OpenSubKey($@"{ProtocolKey}\shell\open\command");
        return command?.GetValue(null) is string value
            && value.Contains(executable, StringComparison.OrdinalIgnoreCase)
            && value.Contains("%1", StringComparison.Ordinal);
    }
}
