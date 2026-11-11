using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// ====================================================================================
//  Mods do mundo do amigo: descobre quais faltam (pelo hash SHA-1 do arquivo) e baixa
//  exatamente os mesmos arquivos pelo Modrinth.
// ====================================================================================
public sealed record ModFile(string Name, string Sha1, long Size);

public sealed record ModDownload(string Name, string Url);

public static class FriendMods
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly Dictionary<string, (long Size, long Ticks, string Hash)> Cache = new();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher/1.5 (friends)");
        return c;
    }

    /// <summary>Lê a pasta mods e devolve o nome, o SHA-1 e o tamanho de cada .jar.</summary>
    public static Task<List<ModFile>> HashFolderAsync(string modsDir) => Task.Run(() =>
    {
        var list = new List<ModFile>();
        if (!Directory.Exists(modsDir)) return list;

        foreach (var f in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            try
            {
                var fi = new FileInfo(f);
                lock (Cache)
                {
                    if (Cache.TryGetValue(f, out var c) && c.Size == fi.Length && c.Ticks == fi.LastWriteTimeUtc.Ticks)
                    {
                        list.Add(new ModFile(fi.Name, c.Hash, fi.Length));
                        continue;
                    }
                }

                using var fs = File.OpenRead(f);
                var hash = Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
                lock (Cache) Cache[f] = (fi.Length, fi.LastWriteTimeUtc.Ticks, hash);
                list.Add(new ModFile(fi.Name, hash, fi.Length));
            }
            catch { /* arquivo em uso ou ilegível: ignora */ }
        }
        return list;
    });

    /// <summary>Mods que o anfitrião tem e que você ainda não tem (o OptiFine é tratado à parte pelo launcher).</summary>
    public static List<ModFile> Missing(IEnumerable<ModFile> host, IEnumerable<ModFile> mine)
    {
        var have = new HashSet<string>(mine.Select(m => m.Sha1), StringComparer.OrdinalIgnoreCase);
        return host.Where(h => !have.Contains(h.Sha1) &&
                               !h.Name.StartsWith("OptiFine", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Pergunta ao Modrinth de quais mods são esses arquivos. O que não for achado vem em "notFound".</summary>
    public static async Task<(List<ModDownload> Found, List<ModFile> NotFound)> ResolveAsync(IReadOnlyList<ModFile> files)
    {
        var found = new List<ModDownload>();
        var notFound = new List<ModFile>();
        if (files.Count == 0) return (found, notFound);

        var body = JsonSerializer.Serialize(new { hashes = files.Select(f => f.Sha1).ToArray(), algorithm = "sha1" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync("https://api.modrinth.com/v2/version_files", content);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        foreach (var f in files)
        {
            string? url = null;
            if (doc.RootElement.TryGetProperty(f.Sha1, out var ver) &&
                ver.TryGetProperty("files", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in arr.EnumerateArray())
                {
                    if (file.TryGetProperty("hashes", out var hs) && hs.TryGetProperty("sha1", out var sh) &&
                        string.Equals(sh.GetString(), f.Sha1, StringComparison.OrdinalIgnoreCase) &&
                        file.TryGetProperty("url", out var u))
                    {
                        url = u.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(url)) notFound.Add(f); else found.Add(new ModDownload(f.Name, url));
        }
        return (found, notFound);
    }

    /// <summary>Baixa os arquivos para a pasta mods (o nome vem do anfitrião, para ficar igual).</summary>
    public static async Task DownloadAsync(IReadOnlyList<ModDownload> items, string modsDir,
        Action<int, int, string> progress)
    {
        Directory.CreateDirectory(modsDir);
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            progress(i, items.Count, it.Name);
            var safe = IOPath.GetFileName(it.Name);
            if (string.IsNullOrWhiteSpace(safe) || !safe.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
            var target = IOPath.Combine(modsDir, safe);
            if (File.Exists(target)) continue;
            await ContentApi.DownloadFileAsync(it.Url, target);
        }
        progress(items.Count, items.Count, "");
    }
}
