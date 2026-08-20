using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ShuBingDesktopPet.Services;

/// Stores Twitch credentials locally. Access and refresh tokens are protected with
/// Windows CurrentUser DPAPI and are never exposed by the settings UI.
public sealed class TwitchTokenStore
{
    private sealed record DiskToken(string Access, string Refresh, string ClientId,
        DateTime ExpiresUtc, string BroadcasterUserId, string ChannelName);
    public sealed record Token(string AccessToken, string RefreshToken, string ClientId,
        DateTime ExpiresUtc, string BroadcasterUserId, string ChannelName);

    public string Path { get; }
    public TwitchTokenStore(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuBingDesktopPet");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, "twitch-auth.json");
    }

    public Token? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var disk = JsonSerializer.Deserialize<DiskToken>(File.ReadAllText(Path));
            if (disk is null || string.IsNullOrWhiteSpace(disk.Access)) return null;
            return new Token(Unprotect(disk.Access), Unprotect(disk.Refresh), disk.ClientId,
                disk.ExpiresUtc, disk.BroadcasterUserId, disk.ChannelName);
        }
        catch { return null; }
    }

    public void Save(Token token)
    {
        var disk = new DiskToken(Protect(token.AccessToken), Protect(token.RefreshToken),
            token.ClientId.Trim(), token.ExpiresUtc.ToUniversalTime(), token.BroadcasterUserId, token.ChannelName);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(disk, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temp, Path, true);
    }

    public void Clear()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { /* best effort during shutdown */ }
    }

    private static string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? string.Empty), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string value) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
