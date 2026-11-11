using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public record RemoteFile(string Url, string FileName, List<string> RequiredDeps);

public class DownloadBlockedException : Exception
{
    public DownloadBlockedException(string msg) : base(msg) { }
}

/// <summary>O CurseForge recusou a chave de API (chave errada, vencida ou bloqueada).</summary>
public class CurseForgeKeyException : InvalidOperationException
{
    public CurseForgeKeyException(string msg) : base(msg) { }
}

/// <summary>Acesso às APIs do Modrinth (sem chave) e do CurseForge (chave grátis do usuário).</summary>
public static class ContentApi
{
    /// <summary>
    /// Chave de API do CurseForge que já vem dentro do launcher: a aba CurseForge funciona sem o jogador
    /// precisar configurar nada. Se um dia ela for recusada, o launcher mostra um campo para colar outra.
    /// </summary>
    // A chave fica embaralhada (não aparece como texto no código nem no executável) e só é montada na hora de usar.
    private static readonly byte[] KeyMask = { 0x4D, 0x6F, 0x6F, 0x6E, 0x6C, 0x69, 0x67, 0x68, 0x74, 0x21, 0x9A, 0x37, 0xC4 };
    private static readonly byte[] KeyData = { 0x6E, 0x7B, 0x4B, 0x2E, 0xDE, 0xFB, 0x82, 0xCE, 0xE5, 0x48, 0xF4, 0x00, 0xDA, 0xB5, 0xEF, 0xD3, 0xCA, 0x33, 0x04, 0x66, 0x34, 0xA4, 0xA7, 0x26, 0xEA, 0xFA, 0x07, 0x79, 0x4B, 0x88, 0xAE, 0x92, 0xE9, 0x2A, 0x1D, 0x3F, 0xAF, 0xD0, 0x21, 0xBD, 0xE4, 0xDF, 0x3A, 0x3E, 0x57, 0x7E, 0x96, 0x85, 0xAC, 0x21, 0x51, 0xC8, 0x4D, 0x6A, 0x99, 0xED, 0xF6, 0xD3, 0x22, 0x0B };

    private static string? _defaultKey;
    public static string DefaultCurseForgeKey => _defaultKey ??= DecodeKey();

    private static string DecodeKey()
    {
        var raw = new byte[KeyData.Length];
        for (var i = 0; i < raw.Length; i++)
            raw[i] = (byte)(KeyData[i] ^ KeyMask[i % KeyMask.Length] ^ ((i * 31 + 7) & 0xFF));
        return System.Text.Encoding.ASCII.GetString(raw);
    }

    /// <summary>A chave que o jogador colou (se colou) ou a que vem de fábrica.</summary>
    public static string EffectiveKey(string? userKey) =>
        string.IsNullOrWhiteSpace(userKey) ? DefaultCurseForgeKey : userKey.Trim();

    private static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher/1.0");
        return c;
    }

    public static Task<byte[]> GetBytesAsync(string url) => Client.GetByteArrayAsync(url);

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static long Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (long)v.GetDouble()
            : 0;

    // ---------------- Modrinth ----------------

    public const int PageSize = 30;

    public static async Task<(List<ContentItem> Items, int Total)> SearchModrinthAsync(
        string query, string type, string mc, string loader, IReadOnlyList<string> topics, int page,
        string sort = "relevance")
    {
        var facets = new List<string[]> { new[] { $"project_type:{type}" } };
        if (!string.IsNullOrWhiteSpace(mc)) facets.Add(new[] { $"versions:{mc}" });
        if (type == "mod" && !string.IsNullOrWhiteSpace(loader)) facets.Add(new[] { $"categories:{loader}" });
        // Vários temas = "um OU outro" (mais variedade): ficam todos no mesmo grupo.
        if (topics.Count > 0) facets.Add(topics.Select(t => $"categories:{t}").ToArray());

        var url = $"https://api.modrinth.com/v2/search?limit={PageSize}&offset={page * PageSize}" +
                  "&index=" + (sort == "relevance" && string.IsNullOrWhiteSpace(query) ? "downloads" : sort) +
                  "&query=" + Uri.EscapeDataString(query) +
                  "&facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets));

        using var doc = JsonDocument.Parse(await Client.GetStringAsync(url));
        var list = new List<ContentItem>();
        var total = (int)Num(doc.RootElement, "total_hits");
        foreach (var h in doc.RootElement.GetProperty("hits").EnumerateArray())
        {
            var banner = Str(h, "featured_gallery");
            if (banner.Length == 0 && h.TryGetProperty("gallery", out var gal) && gal.ValueKind == JsonValueKind.Array
                && gal.GetArrayLength() > 0 && gal[0].ValueKind == JsonValueKind.String)
                banner = gal[0].GetString() ?? "";

            var cats = new List<string>();
            var catKey = h.TryGetProperty("display_categories", out var dc) && dc.ValueKind == JsonValueKind.Array && dc.GetArrayLength() > 0
                ? "display_categories" : "categories";
            if (h.TryGetProperty(catKey, out var ca) && ca.ValueKind == JsonValueKind.Array)
                foreach (var c in ca.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String) cats.Add(c.GetString() ?? "");

            list.Add(new ContentItem(
                "Modrinth", Str(h, "project_id"), type, Str(h, "title"), Str(h, "description"),
                Str(h, "author"), Str(h, "icon_url"), Num(h, "downloads"),
                $"https://modrinth.com/{Str(h, "project_type")}/{Str(h, "slug")}",
                banner, (int)Num(h, "color"), Num(h, "follows"), Str(h, "date_modified"), string.Join(",", cats)));
        }
        return (list, total);
    }

    public static async Task<RemoteFile?> GetModrinthFileAsync(string id, string type, string mc, string loader)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(mc))
            q.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mc })));
        if (type == "mod" && !string.IsNullOrWhiteSpace(loader))
            q.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })));

        var url = $"https://api.modrinth.com/v2/project/{id}/version" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        using var doc = JsonDocument.Parse(await Client.GetStringAsync(url));

        foreach (var ver in doc.RootElement.EnumerateArray()) // o mais novo vem primeiro
        {
            if (!ver.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;

            JsonElement chosen = files[0];
            foreach (var f in files.EnumerateArray())
                if (f.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True) { chosen = f; break; }

            var deps = new List<string>();
            if (ver.TryGetProperty("dependencies", out var dArr))
            {
                foreach (var d in dArr.EnumerateArray())
                    if (Str(d, "dependency_type") == "required" && Str(d, "project_id") is { Length: > 0 } pid)
                        deps.Add(pid);
            }

            return new RemoteFile(Str(chosen, "url"), Str(chosen, "filename"), deps);
        }

        return null;
    }

    // ---------------- CurseForge ----------------

    private static int ClassId(string type) => type switch
    {
        "modpack" => 4471,
        "resourcepack" => 12,
        "shader" => 6552,
        _ => 6
    };

    private static int LoaderCode(string loader) => loader switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0
    };

    private static async Task<string> CfGetAsync(string url, string key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", key);
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Client.SendAsync(req);

        if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new CurseForgeKeyException("O CurseForge recusou a chave de API. Cole uma chave válida no campo abaixo.");

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<(List<ContentItem> Items, int Total)> SearchCurseForgeAsync(
        string key, string query, string type, string mc, string loader, IReadOnlyList<int> categories, int page,
        string sort = "relevance")
    {
        var sortField = sort switch
        {
            "downloads" => 6,
            "newest" => 11,
            "updated" => 3,
            "follows" => 2,
            _ => string.IsNullOrWhiteSpace(query) ? 2 : 1
        };
        var url = $"https://api.curseforge.com/v1/mods/search?gameId=432&classId={ClassId(type)}" +
                  $"&pageSize={PageSize}&index={page * PageSize}&sortField={sortField}&sortOrder=desc";
        if (categories.Count == 1) url += "&categoryId=" + categories[0];
        else if (categories.Count > 1) url += "&categoryIds=" + Uri.EscapeDataString("[" + string.Join(",", categories) + "]");
        if (!string.IsNullOrWhiteSpace(query)) url += "&searchFilter=" + Uri.EscapeDataString(query);
        if (!string.IsNullOrWhiteSpace(mc)) url += "&gameVersion=" + Uri.EscapeDataString(mc);
        if (type == "mod" && LoaderCode(loader) > 0) url += "&modLoaderType=" + LoaderCode(loader);

        using var doc = JsonDocument.Parse(await CfGetAsync(url, key));
        var list = new List<ContentItem>();
        var total = 0;
        if (doc.RootElement.TryGetProperty("pagination", out var pg)) total = (int)Num(pg, "totalCount");
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var author = "";
            if (m.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0)
                author = Str(a[0], "name");

            var icon = m.TryGetProperty("logo", out var logo) ? Str(logo, "thumbnailUrl") : "";
            var pageUrl = m.TryGetProperty("links", out var links) ? Str(links, "websiteUrl") : "";

            var banner = "";
            if (m.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array && shots.GetArrayLength() > 0)
                banner = Str(shots[0], "url");

            var cats = new List<string>();
            if (m.TryGetProperty("categories", out var cc) && cc.ValueKind == JsonValueKind.Array)
                foreach (var c in cc.EnumerateArray()) cats.Add(Str(c, "name"));

            list.Add(new ContentItem(
                "CurseForge", Num(m, "id").ToString(), type, Str(m, "name"), Str(m, "summary"),
                author, icon, Num(m, "downloadCount"), pageUrl,
                banner, 0, Num(m, "thumbsUpCount"), Str(m, "dateModified"), string.Join(",", cats)));
        }
        return (list, Math.Min(total, 10000 - PageSize));
    }

    public static async Task<RemoteFile?> GetCurseForgeFileAsync(
        string key, string id, string type, string mc, string loader)
    {
        var url = $"https://api.curseforge.com/v1/mods/{id}/files?pageSize=20";
        if (!string.IsNullOrWhiteSpace(mc)) url += "&gameVersion=" + Uri.EscapeDataString(mc);
        if (type == "mod" && LoaderCode(loader) > 0) url += "&modLoaderType=" + LoaderCode(loader);

        using var doc = JsonDocument.Parse(await CfGetAsync(url, key));
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) return null;

        // Pega o arquivo mais recente da lista.
        JsonElement best = data[0];
        DateTime bestDate = DateTime.MinValue;
        foreach (var f in data.EnumerateArray())
        {
            if (DateTime.TryParse(Str(f, "fileDate"), out var d) && d > bestDate)
            {
                bestDate = d;
                best = f;
            }
        }

        var deps = new List<string>();
        if (best.TryGetProperty("dependencies", out var dArr))
        {
            foreach (var d in dArr.EnumerateArray())
                if (Num(d, "relationType") == 3) // 3 = dependência obrigatória
                    deps.Add(Num(d, "modId").ToString());
        }

        // downloadUrl vem vazio quando o autor bloqueou o download por launchers de terceiros.
        return new RemoteFile(Str(best, "downloadUrl"), Str(best, "fileName"), deps);
    }

    // ---------------- Download ----------------

    public static async Task DownloadFileAsync(string url, string dest)
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(dest)!);
        var tmp = dest + ".part";

        using var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst);

        File.Move(tmp, dest, true);
    }
}

public partial class MainWindow
{
    private string _modsType = "mod";
    private int _modsPage;
    private int _modsTotal;
    private readonly HashSet<string> _topics = new();
    private ModpackInfo? _packTarget; // quando não é null, "Instalar" coloca o mod dentro deste modpack
    private VersionPicker? _modsPicker;
    private VersionPicker? _packPicker;
    private string _modsSource = "Modrinth";
    private string _modsLoader = "fabric";
    private bool _modsUiReady;
    private int _modsSearchId;
    private bool _cfKeyBad; // a chave do CurseForge foi recusada: aí sim mostra o campo para colar outra

    private string CfKey => ContentApi.EffectiveKey(_state.CurseForgeKey);

    // O campo da chave só aparece se o CurseForge recusar a chave que vem de fábrica.
    private void UpdateCfBar() =>
        CfBar.Visibility = _modsSource == "CurseForge" && _cfKeyBad ? Visibility.Visible : Visibility.Collapsed;

    private static string FolderFor(string type) => type switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        _ => "mods"
    };

    private string _modsSyncedMc = "";
    private string _modsSyncedLoader = "";

    // Chamado quando o usuário abre a aba Mods: acompanha a versão e o loader escolhidos na aba Jogar,
    // mas só quando eles mudam (assim não apaga o que o usuário escolheu aqui).
    private void EnterModsPage()
    {
        _modsUiReady = false;
        var changed = false;

        var playMc = "";
        if (VersionList.SelectedItem is string sel)
        {
            var m = Regex.Match(sel, @"^\d+\.\d+(\.\d+)?");
            if (m.Success) playMc = m.Value;
        }

        if (playMc.Length > 0 && playMc != _modsSyncedMc)
        {
            ModsMcBox.Text = playMc;
            _modsSyncedMc = playMc;
            changed = true;
        }

        string? wanted = _loader switch
        {
            Loader.Fabric => "fabric",
            Loader.Forge or Loader.ForgeOptiFine => "forge",
            Loader.NeoForge => "neoforge",
            _ => null
        };

        if (wanted != null && wanted != _modsSyncedLoader)
        {
            _modsLoader = wanted;
            _modsSyncedLoader = wanted;
            foreach (var rb in ModsLoaderPanel.Children.OfType<RadioButton>())
                rb.IsChecked = rb.Tag?.ToString() == wanted;
            changed = true;
        }

        CfKeyBox.Password = _state.CurseForgeKey;
        UpdateCfBar();
        ModsLoaderPanel.Visibility = _modsType == "mod" ? Visibility.Visible : Visibility.Collapsed;

        _modsUiReady = true;
        ApplyModsTypeUi();

        _ = RefreshInstalledModsAsync(); // barra lateral com os mods da pasta
        if (_modsType == "mypacks") { RefreshPackList(); return; }
        if (changed || _modsShown == 0) { _modsPage = 0; _ = SearchModsAsync(); }
    }

    private void ModsType_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _modsType = rb.Tag?.ToString() ?? "mod";
        if (!_modsUiReady || ModsLoaderPanel == null) return;

        ApplyModsTypeUi();
        if (_modsType == "mypacks") { RefreshPackList(); return; }

        _modsPage = 0;
        _topics.RemoveWhere(t => !TopicsFor(_modsType).Any(x => x.Id == t));
        BuildTopicChips();
        _ = SearchModsAsync();
    }

    // Mostra/esconde as partes da tela conforme a sub-aba (Mods, Modpacks, Texturas, Shaders, Meus modpacks).
    private void ApplyModsTypeUi()
    {
        var mine = _modsType == "mypacks";
        ModpackPanel.Visibility = mine ? Visibility.Visible : Visibility.Collapsed;
        ModsFilterRow.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
        ModsExtraRow.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
        ModsResultsGrid.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
        ModsStatus.Visibility = mine ? Visibility.Collapsed : Visibility.Visible;
        if (mine) ModsPager.Visibility = Visibility.Collapsed;

        ModsLoaderPanel.Visibility = _modsType == "mod" ? Visibility.Visible : Visibility.Collapsed;
        TopicsBar.Visibility = TopicsFor(_modsType).Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModsSource_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _modsSource = rb.Tag?.ToString() ?? "Modrinth";
        if (!_modsUiReady || CfBar == null) return;

        UpdateCfBar();
        _modsPage = 0;
        _ = SearchModsAsync();
    }

    private void ModsLoader_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _modsLoader = rb.Tag?.ToString() ?? "fabric";
        if (_modsUiReady) _ = SearchModsAsync();
    }

    private async void ModsSearch_Click(object sender, RoutedEventArgs e) { _modsPage = 0; await SearchModsAsync(); }

    private async void ModsBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _modsPage = 0; await SearchModsAsync(); }
    }

    private void CfSaveKey_Click(object sender, RoutedEventArgs e)
    {
        _state.CurseForgeKey = CfKeyBox.Password.Trim();
        SaveState();
        _cfKeyBad = false;
        UpdateCfBar();
        ModsStatus.Text = _state.CurseForgeKey.Length == 0 ? "Voltei para a chave padrão do launcher." : "Chave salva.";
        _modsPage = 0;
        _ = SearchModsAsync();
    }

    private void CfGetKey_Click(object sender, RoutedEventArgs e) => OpenUrl("https://console.curseforge.com/");

    private void ModsOpenPage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ContentItem item && item.PageUrl.StartsWith("http"))
            OpenUrl(item.PageUrl);
    }

    private async Task SearchModsAsync()
    {
        if (_modsType == "mypacks") return;

        var searchId = ++_modsSearchId;
        var query = ModsSearchBox.Text.Trim();
        var mc = ModsMcBox.Text.Trim();
        var topics = _topics.Where(t => TopicsFor(_modsType).Any(x => x.Id == t)).ToList();

        ModsEmpty.Visibility = Visibility.Collapsed;
        ModsStatus.Text = "Buscando…";
        UpdateTopicsLabel();

        try
        {
            List<ContentItem> items;
            int total;
            if (_modsSource == "Modrinth")
            {
                (items, total) = await ContentApi.SearchModrinthAsync(query, _modsType, mc, _modsLoader, topics, _modsPage, _modsSort);
            }
            else
            {
                var cats = TopicsFor(_modsType).Where(t => topics.Contains(t.Id) && t.CfId > 0).Select(t => t.CfId).ToList();
                (items, total) = await ContentApi.SearchCurseForgeAsync(CfKey, query, _modsType, mc, _modsLoader, cats, _modsPage, _modsSort);
                if (searchId == _modsSearchId && _cfKeyBad)
                {
                    _cfKeyBad = false; // a chave voltou a funcionar
                    UpdateCfBar();
                }
            }

            if (searchId != _modsSearchId) return; // chegou uma busca mais nova

            _modsTotal = total;
            ShowModItems(items);
            ModsEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var themeTxt = topics.Count > 0 ? " · temas: " + string.Join(", ", TopicsFor(_modsType).Where(t => topics.Contains(t.Id)).Select(t => t.Label)) : "";
            ModsStatus.Text = items.Count == 0 ? "" : $"{total} resultados · {_modsSource}{themeTxt}";
            BuildPager();
        }
        catch (CurseForgeKeyException ex)
        {
            if (searchId != _modsSearchId) return;
            _cfKeyBad = true;
            UpdateCfBar();
            ShowModItems(null);
            ModsPager.Visibility = Visibility.Collapsed;
            ModsStatus.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            if (searchId == _modsSearchId) ModsStatus.Text = "Não consegui falar com o site. Verifique sua internet.";
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Erro na busca de mods", ex);
            if (searchId == _modsSearchId) ModsStatus.Text = "Erro: " + ex.Message;
        }
    }

    private async void ModsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ContentItem item) return;

        if (item.Type == "modpack")
        {
            OpenUrl(item.PageUrl);
            return;
        }

        var mc = ModsMcBox.Text.Trim();
        var loaderForInstall = _modsLoader;
        var packDest = (string?)null;
        if (_packTarget != null && item.Type == "mod")
        {
            mc = _packTarget.Mc;                    // o modpack manda: versão e loader dele
            loaderForInstall = _packTarget.Loader;
            packDest = _packTarget.ModsDir;
        }

        if (item.Type == "mod" && mc.Length == 0)
        {
            ModsStatus.Text = "Digite a versão do Minecraft (ex.: 1.20.1) para eu instalar o mod certo.";
            return;
        }

        btn.IsEnabled = false;
        ModsStatus.Text = $"Instalando {item.Title}…";
        try
        {
            var dest = packDest ?? IOPath.Combine(_root, FolderFor(item.Type));
            var report = new List<string>();
            await InstallContentAsync(item.Source, item.Id, item.Type, mc, loaderForInstall, dest,
                new HashSet<string>(), 0, report);

            ModsStatus.Text = report.Count == 0
                ? $"{item.Title}: nada novo para instalar."
                : (packDest != null ? $"✔ No modpack “{_packTarget!.Name}”: " : "✔ Instalado: ") + string.Join(", ", report);

            if (item.Type == "mod" && packDest == null) _ = RefreshInstalledModsAsync();
            if (packDest != null) RefreshPackDetail();
        }
        catch (DownloadBlockedException)
        {
            ModsStatus.Text = $"O autor de “{item.Title}” bloqueou o download fora do CurseForge. Abrindo a página…";
            if (item.PageUrl.StartsWith("http")) OpenUrl(item.PageUrl);
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Erro ao instalar {item.Title}", ex);
            ModsStatus.Text = "Erro ao instalar: " + ex.Message;
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    // Baixa o arquivo e, no caso de mods, as dependências obrigatórias (ex.: Fabric API).
    private async Task InstallContentAsync(string source, string id, string type, string mc, string loader,
        string destDir, HashSet<string> visited, int depth, List<string> report)
    {
        if (!visited.Add($"{source}:{id}")) return;

        var file = source == "Modrinth"
            ? await ContentApi.GetModrinthFileAsync(id, type, mc, loader)
            : await ContentApi.GetCurseForgeFileAsync(CfKey, id, type, mc, loader);

        if (file == null || string.IsNullOrWhiteSpace(file.FileName))
        {
            if (depth > 0) return; // dependência sem arquivo compatível: ignora
            throw new InvalidOperationException(
                $"Não achei uma versão compatível com o Minecraft {(mc.Length == 0 ? "escolhido" : mc)}.");
        }

        if (string.IsNullOrWhiteSpace(file.Url))
            throw new DownloadBlockedException("Download bloqueado pelo autor.");

        var target = IOPath.Combine(destDir, IOPath.GetFileName(file.FileName));
        if (!File.Exists(target))
        {
            await ContentApi.DownloadFileAsync(file.Url, target);
            report.Add(IOPath.GetFileName(file.FileName));
        }

        if (type != "mod" || depth >= 3) return;

        foreach (var dep in file.RequiredDeps)
        {
            try
            {
                await InstallContentAsync(source, dep, type, mc, loader, destDir, visited, depth + 1, report);
            }
            catch (Exception ex)
            {
                // Dependência que não deu certo não cancela o mod principal.
                LauncherLog.Write($"Dependência {dep} não instalada", ex);
            }
        }
    }
}
