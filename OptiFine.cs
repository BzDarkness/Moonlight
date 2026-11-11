using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>Uma versão do OptiFine, ex.: OptiFine_1.20.1_HD_U_I6.jar</summary>
public sealed record OptiFineBuild(string Mc, string Type, string Patch)
{
    public string FileName => $"OptiFine_{Mc}_{Type}_{Patch}.jar";
    public string VersionId => $"{Mc}-OptiFine_{Type}_{Patch}";   // nome da pasta em .minecraft/versions
    public string LibVersion => $"{Mc}_{Type}_{Patch}";           // nome da biblioteca
}

internal sealed class OptiFineCache
{
    public DateTime FetchedUtc { get; set; }
    public List<OptiFineBuild> Builds { get; set; } = new();
}

/// <summary>Lista as versões do OptiFine e baixa o .jar certo (site oficial primeiro, espelho como reserva).</summary>
internal static class OptiFineCatalog
{
    private static readonly string Dir = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher");

    public static readonly string CacheFile = IOPath.Combine(Dir, "optifine-catalog.json");
    public static readonly string JarDir = IOPath.Combine(Dir, "optifine");

    // (?<!preview_) ignora as versões de teste (preview).
    private static readonly Regex FileRx = new(
        @"(?<!preview_)OptiFine_(?<mc>\d+(?:\.\d+){1,2})_(?<type>HD_[A-Z]+)_(?<patch>[A-Z]\d+)\.jar",
        RegexOptions.Compiled);

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MoonlightLauncher/2.0");
        return http;
    }

    // ---------- Lista ----------

    /// <summary>A melhor (mais nova) versão do OptiFine para cada versão do Minecraft.</summary>
    public static async Task<List<OptiFineBuild>> GetBestPerMcAsync(CancellationToken ct = default)
    {
        OptiFineCache? cache = null;
        try
        {
            if (File.Exists(CacheFile))
                cache = JsonSerializer.Deserialize<OptiFineCache>(File.ReadAllText(CacheFile));
        }
        catch { /* cache ruim: ignora */ }

        if (cache != null && cache.Builds.Count > 0 && DateTime.UtcNow - cache.FetchedUtc < TimeSpan.FromHours(12))
            return Best(cache.Builds);

        List<OptiFineBuild>? fresh = null;
        try { fresh = await FetchOfficialAsync(ct); }
        catch (Exception ex) { LauncherLog.Write("OptiFine: não consegui a lista oficial", ex); }

        if (fresh == null || fresh.Count == 0)
        {
            try { fresh = await FetchMirrorAsync(ct); }
            catch (Exception ex) { LauncherLog.Write("OptiFine: não consegui a lista do espelho", ex); }
        }

        if (fresh != null && fresh.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(new OptiFineCache
                {
                    FetchedUtc = DateTime.UtcNow,
                    Builds = fresh
                }));
            }
            catch { /* cache é opcional */ }
            return Best(fresh);
        }

        // Sem internet: usa a última lista guardada, mesmo velha.
        return cache != null ? Best(cache.Builds) : new List<OptiFineBuild>();
    }

    /// <summary>Procura o OptiFine de UMA versão do Minecraft (site oficial e espelho), mesmo fora da lista em cache.</summary>
    public static async Task<OptiFineBuild?> FindForMcAsync(string mc, CancellationToken ct = default)
    {
        var found = new List<OptiFineBuild>();
        try
        {
            found.AddRange((await FetchOfficialAsync(ct)).Where(b => b.Mc == mc));
        }
        catch (Exception ex) { LauncherLog.Write($"OptiFine {mc}: lista oficial indisponível", ex); }

        if (found.Count == 0)
        {
            try
            {
                using var http = NewClient();
                var json = await http.GetStringAsync($"https://bmclapi2.bangbang93.com/optifine/{mc}", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (!e.TryGetProperty("type", out var type) || !e.TryGetProperty("patch", out var patch)) continue;
                    var p = patch.GetString() ?? "";
                    if (!Regex.IsMatch(p, @"^[A-Z]\d+$")) continue;
                    found.Add(new OptiFineBuild(mc, type.GetString() ?? "", p));
                }
            }
            catch (Exception ex) { LauncherLog.Write($"OptiFine {mc}: espelho indisponível", ex); }
        }

        return Best(found.Where(b => b.Type.Length > 0)).FirstOrDefault();
    }

    private static async Task<List<OptiFineBuild>> FetchOfficialAsync(CancellationToken ct)
    {
        using var http = NewClient();
        var html = await http.GetStringAsync("https://optifine.net/downloads", ct);

        var set = new HashSet<OptiFineBuild>();
        foreach (Match m in FileRx.Matches(html))
            set.Add(new OptiFineBuild(m.Groups["mc"].Value, m.Groups["type"].Value, m.Groups["patch"].Value));
        return set.ToList();
    }

    private static async Task<List<OptiFineBuild>> FetchMirrorAsync(CancellationToken ct)
    {
        using var http = NewClient();
        var json = await http.GetStringAsync("https://bmclapi2.bangbang93.com/optifine/versionList", ct);
        using var doc = JsonDocument.Parse(json);

        var set = new HashSet<OptiFineBuild>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!e.TryGetProperty("mcversion", out var mc) || !e.TryGetProperty("type", out var type) ||
                !e.TryGetProperty("patch", out var patch)) continue;

            var p = patch.GetString() ?? "";
            if (!Regex.IsMatch(p, @"^[A-Z]\d+$")) continue; // pula previews
            set.Add(new OptiFineBuild(mc.GetString() ?? "", type.GetString() ?? "", p));
        }
        return set.Where(b => b.Mc.Length > 0 && b.Type.Length > 0).ToList();
    }

    private static List<OptiFineBuild> Best(IEnumerable<OptiFineBuild> all) =>
        all.GroupBy(b => b.Mc)
           .Select(g => g.OrderByDescending(b => b.Type == "HD_U")
                         .ThenByDescending(b => b.Type)
                         .ThenByDescending(b => Rank(b.Patch))
                         .First())
           .ToList();

    // "I6" < "J1": primeiro a letra, depois o número.
    private static int Rank(string patch)
    {
        var m = Regex.Match(patch, @"^([A-Z])(\d+)");
        return m.Success ? (m.Groups[1].Value[0] - 'A') * 1000 + int.Parse(m.Groups[2].Value) : 0;
    }

    // ---------- Download ----------

    /// <summary>Garante o .jar do OptiFine na pasta de cache e devolve o caminho.</summary>
    public static async Task<string> EnsureJarAsync(OptiFineBuild b, CancellationToken ct = default)
    {
        Directory.CreateDirectory(JarDir);
        var path = IOPath.Combine(JarDir, b.FileName);
        if (LooksLikeJar(path)) return path;

        Exception? first = null;
        try
        {
            await DownloadOfficialAsync(b, path, ct);
            if (LooksLikeJar(path)) return path;
        }
        catch (Exception ex)
        {
            first = ex;
            LauncherLog.Write($"OptiFine: download oficial falhou ({b.FileName})", ex);
        }

        try
        {
            await DownloadMirrorAsync(b, path, ct);
            if (LooksLikeJar(path)) return path;
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"OptiFine: download pelo espelho falhou ({b.FileName})", ex);
            throw new InvalidOperationException(
                $"Não consegui baixar o OptiFine para o Minecraft {b.Mc}. Verifique sua internet e tente de novo.", first ?? ex);
        }

        throw new InvalidOperationException($"O arquivo do OptiFine {b.Mc} veio inválido. Tente de novo.");
    }

    private static async Task DownloadOfficialAsync(OptiFineBuild b, string dest, CancellationToken ct)
    {
        using var http = NewClient();

        // A página do arquivo tem um link com um código temporário (downloadx?f=...&x=...).
        var page = await http.GetStringAsync($"https://optifine.net/adloadx?f={b.FileName}", ct);
        var m = Regex.Match(page, @"downloadx\?f=[^'""\s<>]+");
        if (!m.Success) throw new InvalidOperationException("Não achei o link de download no site do OptiFine.");

        var url = "https://optifine.net/" + WebUtility.HtmlDecode(m.Value);
        await DownloadFileAsync(http, url, dest, ct);
    }

    private static async Task DownloadMirrorAsync(OptiFineBuild b, string dest, CancellationToken ct)
    {
        using var http = NewClient();
        await DownloadFileAsync(http, $"https://bmclapi2.bangbang93.com/optifine/{b.Mc}/{b.Type}/{b.Patch}", dest, ct);
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string dest, CancellationToken ct)
    {
        var tmp = dest + ".part";
        try
        {
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmp);
                await src.CopyToAsync(dst, ct);
            }

            if (!LooksLikeJar(tmp)) throw new InvalidOperationException("O arquivo baixado não é um .jar.");
            File.Move(tmp, dest, true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignora */ }
        }
    }

    private static bool LooksLikeJar(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 200_000) return false;
            using var fs = fi.OpenRead();
            return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Apaga a lista e os .jar guardados (aba Ajustes → limpar cache). Devolve os MB liberados.</summary>
    public static double ClearCache()
    {
        long bytes = 0;
        try
        {
            if (File.Exists(CacheFile))
            {
                bytes += new FileInfo(CacheFile).Length;
                File.Delete(CacheFile);
            }
            if (Directory.Exists(JarDir))
            {
                foreach (var f in Directory.GetFiles(JarDir))
                {
                    try { bytes += new FileInfo(f).Length; File.Delete(f); } catch { /* em uso */ }
                }
            }
        }
        catch { /* ignora */ }
        return bytes / 1024.0 / 1024.0;
    }
}

/// <summary>Instala o OptiFine sozinho, sem abrir a janela do instalador (o mesmo que o instalador oficial faz).</summary>
internal static class OptiFineInstaller
{
    /// <summary>Devolve o nome da versão criada em .minecraft/versions (ex.: 1.20.1-OptiFine_HD_U_I6).</summary>
    public static async Task<string> InstallAsync(string root, OptiFineBuild b, string javaExe, string installerJar,
        Action<string>? status = null, CancellationToken ct = default)
    {
        var versionDir = IOPath.Combine(root, "versions", b.VersionId);
        var jsonPath = IOPath.Combine(versionDir, b.VersionId + ".json");
        var libVer = b.LibVersion;
        var libJar = IOPath.Combine(root, "libraries", "optifine", "OptiFine", libVer, $"OptiFine-{libVer}.jar");

        if (File.Exists(jsonPath) && File.Exists(libJar)) return b.VersionId;

        var vanillaJar = IOPath.Combine(root, "versions", b.Mc, b.Mc + ".jar");
        var vanillaJson = IOPath.Combine(root, "versions", b.Mc, b.Mc + ".json");
        if (!File.Exists(vanillaJar) || !File.Exists(vanillaJson))
            throw new InvalidOperationException($"O Minecraft {b.Mc} precisa estar instalado antes do OptiFine.");

        // 1) O OptiFine é entregue como "patch" em cima do jogo original: gera o .jar já aplicado.
        status?.Invoke("Instalando o OptiFine (aplicando o patch)…");
        Directory.CreateDirectory(IOPath.GetDirectoryName(libJar)!);
        var (code, output) = await RunJavaAsync(javaExe,
            new[] { "-cp", installerJar, "optifine.Patcher", vanillaJar, installerJar, libJar }, ct);

        if (code != 0 || !File.Exists(libJar) || new FileInfo(libJar).Length < 100_000)
        {
            try { if (File.Exists(libJar)) File.Delete(libJar); } catch { /* ignora */ }
            throw new InvalidOperationException("O patch do OptiFine não funcionou.\n" + Tail(output));
        }

        // 2) O instalador traz junto o "launchwrapper" que o OptiFine usa para entrar no jogo.
        var libs = new JsonArray { new JsonObject { ["name"] = $"optifine:OptiFine:{libVer}" } };
        var wrapperAdded = false;
        using (var zip = ZipFile.OpenRead(installerJar))
        {
            var txt = zip.GetEntry("launchwrapper-of.txt");
            if (txt != null)
            {
                string ver;
                using (var sr = new StreamReader(txt.Open())) ver = sr.ReadToEnd().Trim();

                var entry = Regex.IsMatch(ver, @"^[0-9A-Za-z._-]+$") ? zip.GetEntry($"launchwrapper-of-{ver}.jar") : null;
                if (entry != null)
                {
                    var wrapperJar = IOPath.Combine(root, "libraries", "optifine", "launchwrapper-of", ver,
                        $"launchwrapper-of-{ver}.jar");
                    Directory.CreateDirectory(IOPath.GetDirectoryName(wrapperJar)!);
                    entry.ExtractToFile(wrapperJar, true);
                    libs.Add(new JsonObject { ["name"] = $"optifine:launchwrapper-of:{ver}" });
                    wrapperAdded = true;
                }
            }
        }

        // Instaladores antigos não trazem o launchwrapper: usa o da Mojang.
        if (!wrapperAdded)
            libs.Add(new JsonObject
            {
                ["name"] = "net.minecraft:launchwrapper:1.12",
                ["url"] = "https://libraries.minecraft.net/"
            });

        // 3) Cria a versão (herda do Minecraft original).
        var parent = JsonNode.Parse(File.ReadAllText(vanillaJson))?.AsObject()
                     ?? throw new InvalidOperationException("O arquivo do Minecraft original está inválido.");

        var now = DateTimeOffset.UtcNow.ToString("o");
        var json = new JsonObject
        {
            ["id"] = b.VersionId,
            ["inheritsFrom"] = b.Mc,
            ["time"] = now,
            ["releaseTime"] = now,
            ["type"] = "release",
            ["libraries"] = libs,
            ["mainClass"] = "net.minecraft.launchwrapper.Launch"
        };

        if (parent["arguments"] is JsonObject)
        {
            json["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--tweakClass", "optifine.OptiFineTweaker")
            };
        }
        else
        {
            var old = parent["minecraftArguments"]?.GetValue<string>() ?? "";
            json["minecraftArguments"] = (old + " --tweakClass optifine.OptiFineTweaker").Trim();
        }

        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        return b.VersionId;
    }

    /// <summary>Reserva: abre o instalador oficial do OptiFine e espera o jogador fechar.</summary>
    public static async Task RunGuiAsync(string javaExe, string installerJar)
    {
        var javaw = javaExe.Replace("java.exe", "javaw.exe", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(javaw)) javaw = javaExe;

        var psi = new ProcessStartInfo(javaw) { UseShellExecute = false };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJar);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui abrir o instalador do OptiFine.");
        await p.WaitForExitAsync();
    }

    private static async Task<(int Code, string Output)> RunJavaAsync(string javaExe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(javaExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui executar o Java.");
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { /* ignora */ }
            throw new InvalidOperationException("O patch do OptiFine demorou demais.");
        }

        p.WaitForExit(); // termina de ler a saída
        lock (sb) return (p.ExitCode, sb.ToString());
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(8);
        return string.Join("\n", lines);
    }
}
