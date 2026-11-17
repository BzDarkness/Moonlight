using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, long Size, string? Sha256);

/// <summary>Resultado de uma verificação: a atualização (se houver), o que dizer à pessoa e se o GitHub chegou a responder.</summary>
public record UpdateCheck(UpdateInfo? Info, string Message, bool Reached);

/// <summary>
/// Atualização automática: consulta as "Releases" do GitHub, compara com a versão em execução
/// e, se houver uma nova, baixa o .exe, troca o arquivo e reabre o launcher.
/// Nada extra fica no PC: sem instalador, sem .bat, sem programa auxiliar. O próprio launcher
/// renomeia o .exe antigo (o Windows permite isso com ele aberto), coloca o novo no lugar e,
/// na abertura seguinte, apaga o arquivo antigo.
/// </summary>
public static class Updater
{
    // Onde ficam as Releases com o MoonlightLauncher.exe. Se vazio, usa o repositório onde o GitHub Actions compilou.
    private const string RepoOverride = "widielsonferreira08-hue/Moonlight-Downloads";

    public const string ExeName = "MoonlightLauncher.exe";

    public static string Repo { get; } = ResolveRepo();

    public static bool IsConfigured => Repo.Contains('/') && !Repo.Contains("SEU_USUARIO");

    // Clientes próprios do atualizador: um para consultar (curto) e um para baixar o .exe (sem limite de 60 s).
    private static readonly HttpClient Api = MakeClient(TimeSpan.FromSeconds(25), autoRedirect: true);
    private static readonly HttpClient Plain = MakeClient(TimeSpan.FromSeconds(25), autoRedirect: false);
    private static readonly HttpClient Dl = MakeClient(Timeout.InfiniteTimeSpan, autoRedirect: true);

    private static HttpClient MakeClient(TimeSpan timeout, bool autoRedirect)
    {
        var h = new HttpClientHandler { AllowAutoRedirect = autoRedirect };
        var c = new HttpClient(h) { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher-Updater/1.0");
        return c;
    }

    private static string ResolveRepo()
    {
        if (!string.IsNullOrWhiteSpace(RepoOverride)) return RepoOverride.Trim();
        try
        {
            var meta = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value;
            return (meta ?? "").Trim();
        }
        catch { return ""; }
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        }
    }

    public static string CurrentText => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Não consegui descobrir onde o launcher está.");

    // "v1.6.3", "1.6.3", "V1.7", "v1.6.3-beta" … → 1.6.3 (aceita também texto antes do número)
    private static readonly Regex VerRx = new(@"(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);

    private static bool TryParseTag(string? tag, out Version v)
    {
        v = new Version(0, 0, 0);
        var m = VerRx.Match(tag ?? "");
        if (!m.Success) return false;
        v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        return true;
    }

    private sealed record Rel(Version Version, string Tag, string Notes, string? Url, long Size, string? Sha);

    /// <summary>Lê as Releases pela API do GitHub. Devolve lista vazia se o repositório não tem nenhuma (ou 404).</summary>
    private static async Task<List<Rel>> FetchFromApiAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=15");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Api.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return new List<Rel>();
        resp.EnsureSuccessStatusCode();

        var list = new List<Rel>();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;

            var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TryParseTag(tag, out var ver)) continue;

            string? url = null, sha = null;
            long size = 0;
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                JsonElement? pick = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) { pick = a; break; }
                    // Se o nome saiu diferente (ex.: "MoonlightLauncher.(1).exe"), aceita o primeiro .exe.
                    if (pick == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) pick = a;
                }
                if (pick is { } p)
                {
                    url = p.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (p.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number) size = s.GetInt64();
                    if (p.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String)
                    {
                        var text = dg.GetString() ?? "";
                        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = text[7..];
                    }
                }
            }

            var notes = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            list.Add(new Rel(ver, tag, notes.Trim(), url, size, sha));
        }
        return list;
    }

    /// <summary>
    /// Plano B (sem a API, que tem limite de consultas por hora): a página /releases/latest redireciona para a tag da última Release,
    /// e o arquivo fica sempre em /releases/download/&lt;tag&gt;/MoonlightLauncher.exe.
    /// </summary>
    private static async Task<List<Rel>> FetchFromWebAsync(CancellationToken ct)
    {
        using var resp = await Plain.GetAsync($"https://github.com/{Repo}/releases/latest", ct);
        var loc = resp.Headers.Location?.ToString() ?? "";
        var m = Regex.Match(loc, @"/releases/tag/([^/?#]+)");
        if (!m.Success) return new List<Rel>();

        var tag = Uri.UnescapeDataString(m.Groups[1].Value);
        if (!TryParseTag(tag, out var ver)) return new List<Rel>();

        var url = $"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{ExeName}";
        return new List<Rel> { new Rel(ver, tag, "", url, 0, null) };
    }

    public static async Task<UpdateCheck> CheckAsync(string? appliedTag = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new UpdateCheck(null, "As atualizações não estão ligadas neste build.", false);

        List<Rel> rels;
        try { rels = await FetchFromApiAsync(ct); }
        catch (Exception ex)
        {
            LauncherLog.Write("Atualização: a API do GitHub falhou, tentando pelo site", ex);
            try { rels = await FetchFromWebAsync(ct); }
            catch (Exception ex2)
            {
                LauncherLog.Write("Atualização: o site do GitHub também falhou", ex2);
                return new UpdateCheck(null, "Não consegui falar com o GitHub agora (sem internet?). Tento de novo daqui a pouco.", false);
            }
        }

        if (rels.Count == 0)
            return new UpdateCheck(null,
                $"Não achei nenhuma Release em github.com/{Repo}. Confira se o repositório é público e se a Release foi publicada (não rascunho).", true);

        var best = rels.OrderByDescending(r => r.Version).First();
        LauncherLog.Write($"Atualização: última Release {best.Tag}, versão em execução {CurrentText}");

        if (best.Version <= Current)
            return new UpdateCheck(null,
                $"✔ Você já está na versão mais recente ({CurrentText}). Última Release no GitHub: {best.Tag}.", true);

        // Trava contra loop: se esta mesma versão (ou uma maior) já foi aplicada e mesmo assim o .exe diz ser mais antigo,
        // é porque o .exe da Release foi compilado com número de versão menor que a tag. Não baixa de novo.
        if (TryParseTag(appliedTag, out var applied) && best.Version <= applied)
            return new UpdateCheck(null,
                $"A Release {best.Tag} já foi instalada, mas o .exe dela foi compilado com a versão {CurrentText}. " +
                "Compile de novo com a mesma versão da tag (campo \"Versao\" do GitHub Actions) e publique uma Release nova.", true);

        if (string.IsNullOrEmpty(best.Url))
            return new UpdateCheck(null,
                $"A Release {best.Tag} existe, mas ainda não tem o {ExeName} anexado. Arraste o .exe para a Release e publique de novo.", true);

        return new UpdateCheck(new UpdateInfo(best.Version, best.Tag, best.Notes, best.Url, best.Size, best.Sha),
            $"Nova versão {best.Tag} disponível!", true);
    }

    /// <summary>Baixa o novo .exe ao lado do atual (mesma pasta, para a troca ser instantânea).</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info,
        IProgress<(long Read, long Total)> progress, CancellationToken ct = default)
    {
        var tmp = ExePath + ".update.tmp";
        Exception? last = null;

        // Até 3 tentativas: conexão que cai no meio do download é comum.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromMinutes(20));

                using var resp = await Dl.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                resp.EnsureSuccessStatusCode();

                var total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var src = await resp.Content.ReadAsStreamAsync(stall.Token);
                await using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, stall.Token)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), stall.Token);
                        read += n;
                        progress.Report((read, total));
                    }
                }

                var len = new FileInfo(tmp).Length;
                if (len < 1_000_000 || (info.Size > 0 && len != info.Size) || (total > 0 && len != total))
                    throw new InvalidOperationException("O arquivo baixado parece incompleto.");

                last = null;
                break;
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(tmp);
                throw new InvalidOperationException(
                    "Sem permissão para gravar na pasta do launcher. Feche o launcher e abra de novo pelo atalho.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TryDelete(tmp);
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                LauncherLog.Write($"Atualização: download falhou (tentativa {attempt}/3)", ex);
                TryDelete(tmp);
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }

        if (last != null)
            throw new InvalidOperationException("Não consegui baixar a atualização (" + last.Message + "). Tente de novo.");

        if (!string.IsNullOrEmpty(info.Sha256))
        {
            string hash;
            await using (var fs = File.OpenRead(tmp))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tmp);
                throw new InvalidOperationException("O arquivo baixado não passou na verificação de segurança. Tente de novo.");
            }
        }

        return tmp;
    }

    /// <summary>Troca o .exe, abre a versão nova e fecha esta.</summary>
    public static void ApplyAndRestart(string tmpFile, string tag)
    {
        var current = ExePath;
        var old = $"{current}.{DateTime.Now.Ticks}.old";

        // O Windows deixa renomear um .exe em execução (só não deixa apagar/sobrescrever).
        File.Move(current, old);
        try
        {
            File.Move(tmpFile, current);
        }
        catch
        {
            try { File.Move(old, current); } catch { /* melhor esforço */ }
            throw;
        }

        try
        {
            Process.Start(new ProcessStartInfo(current)
            {
                UseShellExecute = false,
                WorkingDirectory = IOPath.GetDirectoryName(current)!,
                ArgumentList = { "--atualizado", tag }
            });
        }
        catch
        {
            // Não conseguiu abrir a versão nova: volta a antiga para o launcher não ficar sem .exe.
            try { File.Delete(current); File.Move(old, current); } catch { /* melhor esforço */ }
            throw;
        }

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Chamado ao abrir: apaga o .exe antigo e restos de download (e do sistema de atualização antigo).
    /// Roda em segundo plano porque o .exe antigo ainda pode estar fechando.
    /// </summary>
    public static void CleanupLeftovers()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var exe = ExePath;
                var dir = IOPath.GetDirectoryName(exe)!;
                var name = IOPath.GetFileName(exe);

                for (int attempt = 0; attempt < 15; attempt++)
                {
                    var leftovers = Directory.EnumerateFiles(dir, name + ".*.old")
                        .Append(exe + ".update.tmp")
                        .Append(IOPath.Combine(dir, "MoonlightLauncher.update.exe"))
                        .Append(IOPath.Combine(IOPath.GetTempPath(), "moonlight-update.bat"))
                        .Where(File.Exists)
                        .ToList();

                    if (leftovers.Count == 0) return;
                    foreach (var f in leftovers) TryDelete(f);
                    if (leftovers.All(f => !File.Exists(f))) return;

                    await Task.Delay(2000);
                }
            }
            catch { /* limpeza nunca pode atrapalhar o launcher */ }
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ainda em uso: tenta depois */ }
    }
}

public partial class MainWindow
{
    private DispatcherTimer? _updateTimer;
    private string? _declinedTag;
    private bool _updating;
    private bool _checkingUpdate;

    /// <summary>Depois da abertura: avisa que acabou de atualizar (se for o caso) e passa a vigiar novas versões.</summary>
    private async Task AfterStartupUpdateAsync()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var idx = Array.IndexOf(args, "--atualizado");
            if (idx >= 0)
            {
                var tagText = idx + 1 < args.Length ? args[idx + 1] : "";
                var mismatch = Version.TryParse(tagText.TrimStart('v', 'V'), out var tagV) && tagV > Updater.Current;

                if (mismatch)
                {
                    // O .exe da Release veio com número menor que a tag: guarda para não baixar a mesma versão em loop.
                    LauncherLog.Write($"AVISO: a Release {tagText} trouxe um .exe com versão {Updater.CurrentText}. Compile com a mesma versão da tag.");
                    _state.AppliedUpdate = tagText;
                    SaveState();
                }

                await ShowPopupAsync("🌙 Launcher atualizado!",
                    $"Você agora está na {Updater.CurrentText}, já com os recursos novos. Boas partidas!",
                    null, ("Legal!", "ok"));
            }
            else if (_state.AppliedUpdate.Length > 0 &&
                     Version.TryParse(_state.AppliedUpdate.TrimStart('v', 'V'), out var ap) && ap <= Updater.Current)
            {
                // A trava de loop não é mais necessária: o launcher já está nessa versão (ou numa maior).
                _state.AppliedUpdate = "";
                SaveState();
            }
        }
        catch (Exception ex) { LauncherLog.Write("Aviso pós-atualização", ex); }

        if (!Updater.IsConfigured) return;

        // Com o launcher aberto por horas, continua olhando de tempos em tempos.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync(manual: false);
        _updateTimer.Start();

        // Na abertura, insiste até conseguir checar: antes, se o launcher estivesse ocupado nesse instante
        // a verificação era pulada e só voltava 20 minutos depois (parecia que a atualização "não funcionava").
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : attempt < 4 ? 10 : 30));
            if (await CheckForUpdateAsync(manual: false)) break;
        }
    }

    private bool PopupOpen => Overlay.Visibility == Visibility.Visible;

    /// <summary>Devolve true quando a verificação foi concluída (o GitHub respondeu); false se foi pulada ou falhou e vale tentar de novo.</summary>
    private async Task<bool> CheckForUpdateAsync(bool manual)
    {
        if (_updating || _checkingUpdate) return false;

        // Automático: nunca atrapalha quem está baixando/jogando ou lendo outro aviso.
        if (!manual && (_busy || PopupOpen || _gameActive)) return false;

        _checkingUpdate = true;
        try
        {
            if (manual) UpdateStatusText.Text = "Verificando…";

            var check = await Updater.CheckAsync(_state.AppliedUpdate);
            var info = check.Info;
            if (info == null)
            {
                if (manual) UpdateStatusText.Text = check.Message;
                return check.Reached;
            }

            if (!manual && info.Tag == _declinedTag) return true;

            if (_busy || PopupOpen)
            {
                if (manual) UpdateStatusText.Text = $"Nova versão {info.Tag} disponível. Termine o que está fazendo e tente de novo.";
                return false; // vale tentar de novo daqui a pouco
            }

            if (manual) UpdateStatusText.Text = $"Nova versão {info.Tag} disponível!";

            if (!_state.AutoUpdate || manual)
            {
                var r = await ShowPopupAsync(
                    "🌙 Nova versão disponível!",
                    $"Moonlight Launcher {info.Tag} está disponível (você está na {Updater.CurrentText}). " +
                    "Clique em Atualizar: o launcher baixa, reinicia sozinho e abre com os recursos novos.",
                    string.IsNullOrWhiteSpace(info.Notes) ? null : "Novidades:\n" + info.Notes,
                    ("Atualizar agora", "update"), ("Depois", "later"));

                if (r != "update")
                {
                    _declinedTag = info.Tag; // não pergunta de novo até sair outra versão
                    return true;
                }
            }

            await RunUpdateAsync(info);
            return true;
        }
        catch (Exception ex)
        {
            // Falha ao checar atualização nunca deve atrapalhar o uso do launcher.
            LauncherLog.Write("Falha ao verificar atualização", ex);
            if (manual) UpdateStatusText.Text = "Não foi possível verificar agora (sem internet?).";
            return false;
        }
        finally { _checkingUpdate = false; }
    }

    private async Task RunUpdateAsync(UpdateInfo info)
    {
        _updating = true;
        SetBusy(true);

        // Aviso de download com barra de progresso (sem botões: some sozinho quando reiniciar).
        _ = ShowPopupAsync($"⬇ Baixando a atualização {info.Tag}",
            DownloadText(0, info.Size),
            string.IsNullOrWhiteSpace(info.Notes) ? null : "Novidades:\n" + info.Notes);

        try
        {
            var progress = new System.Progress<(long Read, long Total)>(p =>
            {
                PopupMessage.Text = DownloadText(p.Read, p.Total);
                StatusText.Text = $"Baixando atualização {info.Tag}…";
            });

            var tmp = await Updater.DownloadAsync(info, progress);

            PopupTitle.Text = "🌙 Reiniciando…";
            PopupMessage.Text = "Atualização pronta! O launcher vai fechar e abrir de novo com os recursos novos.";
            StatusText.Text = "Atualização pronta. Reiniciando…";
            await Task.Delay(900);

            // A trava contra loop agora é gravada só na abertura seguinte (se o .exe novo ainda disser ser mais antigo que a tag).
            // Antes ela era gravada aqui, e uma troca que falhasse impedia qualquer nova tentativa daquela versão.
            Updater.ApplyAndRestart(tmp, info.Tag);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha na atualização", ex);
            _declinedTag = info.Tag; // não repete a tentativa a cada verificação
            _updating = false;
            SetBusy(false);
            ClosePopup("close");
            StatusText.Text = "Não foi possível atualizar agora.";
            await ShowPopupAsync("Não foi possível atualizar",
                ex.Message + "\n\nO launcher continua funcionando normalmente. Você pode tentar de novo em Ajustes → Atualizações.",
                null, ("OK", "ok"));
        }
    }

    private static string DownloadText(long read, long total)
    {
        const int cells = 24;
        var frac = total > 0 ? Math.Clamp((double)read / total, 0, 1) : 0;
        var filled = (int)Math.Round(frac * cells);
        var bar = new string('█', filled) + new string('░', cells - filled);
        var mb = read / 1048576.0;
        var size = total > 0 ? $"{mb:0.0} de {total / 1048576.0:0.0} MB" : $"{mb:0.0} MB";
        return $"{bar}  {(int)(frac * 100)}%\n{size}\n\nNão feche o launcher — ele reinicia sozinho quando terminar.";
    }

    // ---------- Ajustes → Atualizações ----------

    private void InitUpdateSettings()
    {
        AutoUpdateToggle.IsChecked = _state.AutoUpdate;
        InitShortcutSettings();
        UpdateVersionText.Text = $"Versão instalada: {Updater.CurrentText}";
        if (!Updater.IsConfigured)
            UpdateStatusText.Text = "As atualizações não estão ligadas neste build (compile pelo GitHub Actions).";
    }

    private void AutoUpdateToggle_Click(object sender, RoutedEventArgs e)
    {
        _state.AutoUpdate = AutoUpdateToggle.IsChecked == true;
        SaveState();
    }

    private async void CheckUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        _declinedTag = null; // quem clicou quer ver a versão nova, mesmo que tenha dito "Depois" antes
        await CheckForUpdateAsync(manual: true);
    }
}
