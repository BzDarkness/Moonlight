using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Atualização automática: consulta a última "Release" do GitHub, compara com a versão em execução
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

    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, string? appliedTag = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, ct);

        // 404 = o repositório ainda não tem nenhuma Release publicada.
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var remote)) return null;

        remote = new Version(remote.Major, Math.Max(remote.Minor, 0), Math.Max(remote.Build, 0));
        if (remote <= Current) return null;

        // Trava contra loop: se esta mesma versão (ou uma maior) já foi aplicada e mesmo assim o .exe diz ser mais antigo,
        // é porque o .exe da Release foi compilado com número de versão menor que a tag. Não baixa de novo.
        if (Version.TryParse((appliedTag ?? "").TrimStart('v', 'V'), out var applied))
        {
            applied = new Version(applied.Major, Math.Max(applied.Minor, 0), Math.Max(applied.Build, 0));
            if (remote <= applied) return null;
        }

        string? url = null;
        long size = 0;
        string? sha = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) continue;

                url = a.GetProperty("browser_download_url").GetString();
                if (a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number) size = s.GetInt64();
                if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
                {
                    var text = d.GetString() ?? "";
                    if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = text[7..];
                }
                break;
            }
        }

        // Release criada mas o build ainda não anexou o .exe: tenta de novo na próxima verificação.
        if (url == null) return null;

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(remote, tag, notes.Trim(), url, size, sha);
    }

    /// <summary>Baixa o novo .exe ao lado do atual (mesma pasta, para a troca ser instantânea).</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo info,
        IProgress<(long Read, long Total)> progress, CancellationToken ct = default)
    {
        var tmp = ExePath + ".update.tmp";

        try
        {
            using var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? info.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    progress.Report((read, total));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(tmp);
            throw new InvalidOperationException(
                "Sem permissão para gravar na pasta do launcher. Feche o launcher e abra de novo pelo atalho.");
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        // Confere se veio inteiro (tamanho e, quando o GitHub informa, o hash).
        var len = new FileInfo(tmp).Length;
        if (len < 1_000_000 || (info.Size > 0 && len != info.Size))
        {
            TryDelete(tmp);
            throw new InvalidOperationException("O arquivo baixado parece incompleto. Tente de novo.");
        }

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

        Process.Start(new ProcessStartInfo(current)
        {
            UseShellExecute = false,
            WorkingDirectory = IOPath.GetDirectoryName(current)!,
            ArgumentList = { "--atualizado", tag }
        });
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
            if (Array.IndexOf(args, "--atualizado") >= 0)
            {
                // Se o .exe da Release tem versão menor que a tag, avisa no log (e a trava em AppliedUpdate impede o loop).
                var ti = Array.IndexOf(args, "--atualizado");
                if (ti + 1 < args.Length && Version.TryParse(args[ti + 1].TrimStart('v', 'V'), out var tagV) && tagV > Updater.Current)
                    LauncherLog.Write($"AVISO: a Release {args[ti + 1]} trouxe um .exe com versão {Updater.CurrentText}. Compile com a mesma versão da tag.");

                await ShowPopupAsync("🌙 Launcher atualizado!",
                    $"Você agora está na {Updater.CurrentText}, já com os recursos novos. Boas partidas!",
                    null, ("Legal!", "ok"));
            }
        }
        catch (Exception ex) { LauncherLog.Write("Aviso pós-atualização", ex); }

        if (!Updater.IsConfigured) return;

        _ = CheckForUpdateAsync(manual: false);

        // Com o launcher aberto por horas, continua olhando de tempos em tempos.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(20) };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync(manual: false);
        _updateTimer.Start();
    }

    private bool PopupOpen => Overlay.Visibility == Visibility.Visible;

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating || _checkingUpdate) return;

        // Automático: nunca atrapalha quem está baixando/jogando ou lendo outro aviso.
        if (!manual && (_busy || PopupOpen)) return;

        _checkingUpdate = true;
        try
        {
            if (manual) UpdateStatusText.Text = "Verificando…";

            var info = await Updater.CheckAsync(Http, _state.AppliedUpdate);
            if (info == null)
            {
                if (manual)
                    UpdateStatusText.Text = Updater.IsConfigured
                        ? $"✔ Você já está na versão mais recente ({Updater.CurrentText})."
                        : "As atualizações não estão ligadas neste build (compile pelo GitHub Actions).";
                return;
            }

            if (!manual && info.Tag == _declinedTag) return;

            if (_busy || PopupOpen)
            {
                if (manual) UpdateStatusText.Text = $"Nova versão {info.Tag} disponível. Termine o que está fazendo e tente de novo.";
                return;
            }

            if (manual) UpdateStatusText.Text = $"Nova versão {info.Tag} disponível!";

            if (!_state.AutoUpdate)
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
                    return;
                }
            }

            await RunUpdateAsync(info);
        }
        catch (Exception ex)
        {
            // Falha ao checar atualização nunca deve atrapalhar o uso do launcher.
            LauncherLog.Write("Falha ao verificar atualização", ex);
            if (manual) UpdateStatusText.Text = "Não foi possível verificar agora (sem internet?).";
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

            var tmp = await Updater.DownloadAsync(Http, info, progress);

            PopupTitle.Text = "🌙 Reiniciando…";
            PopupMessage.Text = "Atualização pronta! O launcher vai fechar e abrir de novo com os recursos novos.";
            StatusText.Text = "Atualização pronta. Reiniciando…";
            await Task.Delay(900);

            // Grava ANTES de trocar o .exe: mesmo que algo dê errado depois, não tenta baixar a mesma versão de novo.
            _state.AppliedUpdate = info.Tag;
            SaveState();

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
