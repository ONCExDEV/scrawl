using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scrawl.Services;

/// <summary>A newer release on GitHub that matches the kind of exe the user runs.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string AssetName, Uri DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Updates Scrawl from its GitHub releases. Nothing here runs by itself: the network is only touched
/// when the user presses the Update button. The new exe replaces the old one in the same place, so
/// shortcuts and the "open text files with Scrawl" setting keep working, and notes are never touched.
/// </summary>
public sealed class Updater
{
    public const string Repository = "ONCExDEV/scrawl";
    public const string SmallAsset = "Scrawl.exe";
    public const string StandaloneAsset = "Scrawl-standalone.exe";

    private readonly HttpClient _http;

    public Updater(HttpClient? http = null) => _http = http ?? CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Scrawl-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The version of the running app (major.minor.patch).</summary>
    public static Version Current => Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0, 0));

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>The big exe (runtime included) is the standalone build; the small one needs the .NET runtime installed.</summary>
    public static bool IsStandalone(string exePath)
    {
        try { return new FileInfo(exePath).Length > 30_000_000; }
        catch (IOException) { return false; }
    }

    /// <summary>Returns the newest release if it is newer than <paramref name="current"/>, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(bool standalone, Version current, CancellationToken cancellation = default)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) throw new InvalidOperationException($"Unexpected version “{tag}” on GitHub.");
        var latest = Normalize(parsed);
        if (latest <= Normalize(current)) return null;

        var wanted = standalone ? StandaloneAsset : SmallAsset;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != wanted) continue;
            var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new UpdateInfo(latest, tag, wanted, new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64(), sha);
        }
        throw new InvalidOperationException($"Release {tag} has no {wanted} to download.");
    }

    private static bool IsGitHub(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "github.com" || uri.Host.EndsWith(".github.com") || uri.Host.EndsWith(".githubusercontent.com"));

    /// <summary>Downloads the release exe to <paramref name="targetPath"/> and checks it (size, exe header, SHA-256 when GitHub provides one).</summary>
    public async Task DownloadAsync(UpdateInfo update, string targetPath, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (!IsGitHub(update.DownloadUrl)) throw new InvalidOperationException("The download address is not on GitHub, so it was not used.");
        try
        {
            using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } final && !IsGitHub(final)) throw new InvalidOperationException("The download was redirected away from GitHub, so it was not used.");
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            Verify(update, targetPath);
        }
        catch
        {
            TryDelete(targetPath);
            throw;
        }
    }

    private static void Verify(UpdateInfo update, string path)
    {
        var info = new FileInfo(path);
        if (update.Size > 0 && info.Length != update.Size) throw new InvalidDataException("The download is incomplete. Nothing was changed.");
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("The download is not a program file. Nothing was changed.");
        if (update.Sha256 is null) return;
        stream.Position = 0;
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download does not match GitHub's checksum. Nothing was changed.");
    }

    /// <summary>
    /// Puts the new exe where the running one is. A running exe can be renamed but not overwritten, so the old
    /// one becomes "name.exe.old" (removed on the next start). If anything fails the old exe is put back.
    /// </summary>
    public static void Replace(string newFile, string targetExe)
    {
        var old = targetExe + ".old";
        TryDelete(old);
        File.Move(targetExe, old);
        try
        {
            File.Move(newFile, targetExe);
        }
        catch
        {
            File.Move(old, targetExe, overwrite: true);
            throw;
        }
    }

    /// <summary>Removes leftovers of an earlier update.</summary>
    public static void CleanUp(string exe)
    {
        TryDelete(exe + ".old");
        TryDelete(exe + ".download");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leftover, cleaned up next time */ }
    }
}
