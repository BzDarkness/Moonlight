using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// Aba "Skins": escolher uma skin (arquivo .png, nick, link do NameMC ou UUID) para a conta selecionada.
//  • Conta Microsoft: botão "Enviar para a Mojang" -> a skin passa a existir de verdade e aparece no NameMC.
//  • Conta offline: a skin é aplicada no jogo por um resource pack gerado ao clicar em Jogar
//    (o NameMC não tem como mostrar contas offline, porque elas não existem nos servidores da Mojang).
public partial class MainWindow
{
    private const string SkinPackName = "MoonlightSkin.zip";
    private static readonly string SkinsDir = IOPath.Combine(AppDataDir, "MoonlightLauncher", "skins");
    private static readonly string LibraryDir = IOPath.Combine(SkinsDir, "library");

    private static readonly Regex McNameRx = new("^[A-Za-z0-9_]{3,16}$");
    private static readonly Regex UuidRx =
        new("^[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}$");
    private static readonly Regex NameMcSkinIdRx = new("^[0-9a-fA-F]{16}$");

    private bool _updatingSkinUi;

    private static string SkinFilePath(Account acc) =>
        IOPath.Combine(SkinsDir, Regex.Replace(acc.Key, "[^A-Za-z0-9_.-]", "_") + ".png");

    // ---------- Tela ----------

    private void RefreshSkinUi()
    {
        if (SkinView == null || HomeSkinView == null) return; // a janela ainda está montando

        RefreshSkinLibrary();

        _updatingSkinUi = true;
        try
        {
            var acc = _active;
            var hasSkin = acc != null && File.Exists(SkinFilePath(acc));
            var isMs = acc?.Type == AccountType.Microsoft;

            SkinAccountText.Text = acc == null
                ? "Nenhuma conta selecionada.\nCrie uma na aba Contas."
                : $"{acc.Name}\n{acc.TypeLabel}";
            SkinSlimCheck.IsChecked = acc?.SkinSlim ?? false;
            SkinSlimCheck.IsEnabled = acc != null;
            SkinImportBtn.IsEnabled = acc != null;
            SkinPickBtn.IsEnabled = acc != null;
            SkinRemoveBtn.IsEnabled = hasSkin;
            SkinUploadBtn.IsEnabled = isMs && hasSkin;
            SkinMyNameMcBtn.IsEnabled = isMs;

            // A skin aparece em 3D (aba Skins e tela Jogar) e a cabeça acompanha o mouse.
            byte[]? skinBytes = null;
            if (hasSkin)
            {
                try { skinBytes = File.ReadAllBytes(SkinFilePath(acc!)); }
                catch (Exception ex)
                {
                    LauncherLog.Write("Não consegui ler a skin para a prévia", ex);
                    hasSkin = false;
                }
            }

            SkinNoneText.Visibility = hasSkin ? Visibility.Collapsed : Visibility.Visible;
            SkinView.SetSkin(skinBytes, acc?.SkinSlim ?? false);
            HomeSkinView.SetSkin(skinBytes, acc?.SkinSlim ?? false);

            SkinModeText.Text = acc == null ? "" : isMs
                ? "Conta Microsoft: escolha a skin e clique em “Enviar para a Mojang”. Depois disso ela vale no jogo " +
                  "inteiro e aparece no seu perfil do NameMC."
                : "Conta Moonlight/offline: a skin fica salva neste PC e é aplicada no jogo quando você clica em Jogar " +
                  "(modo solo e servidores offline). O NameMC não mostra esse tipo de conta.";
        }
        finally
        {
            _updatingSkinUi = false;
        }

        FriendsProfileChanged(); // amigos veem o nome e o rosto da conta/skin atual
    }

    private void SkinSlim_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSkinUi || _active == null) return;
        UpdateAccount(_active with { SkinSlim = SkinSlimCheck.IsChecked == true });
    }

    // ---------- Escolher a skin ----------

    private async void ImportSkin_Click(object sender, RoutedEventArgs e)
    {
        var acc = _active;
        if (acc == null)
        {
            SkinStatus.Text = "Selecione ou crie uma conta primeiro (aba Contas).";
            return;
        }

        var input = SkinSourceBox.Text.Trim();
        if (input.Length == 0)
        {
            SkinStatus.Text = "Digite um nick, um link do NameMC (perfil ou skin), um UUID ou um link .png.";
            return;
        }

        SkinImportBtn.IsEnabled = false;
        SkinStatus.Text = "Buscando a skin…";
        try
        {
            var (bytes, slim) = await FetchSkinAsync(input);
            StoreSkin(acc, bytes, slim, LabelFor(input));
            SkinStatus.Text = "Skin carregada! " + NextStepHint(acc);
        }
        catch (InvalidOperationException ex)
        {
            SkinStatus.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            SkinStatus.Text = "Não consegui baixar a skin. Verifique sua internet e tente de novo.";
        }
        catch (TaskCanceledException)
        {
            SkinStatus.Text = "A busca demorou demais. Tente de novo.";
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao importar skin", ex);
            SkinStatus.Text = "Erro: " + ex.Message;
        }
        finally
        {
            SkinImportBtn.IsEnabled = _active != null;
        }
    }

    private void PickSkinFile_Click(object sender, RoutedEventArgs e)
    {
        var acc = _active;
        if (acc == null)
        {
            SkinStatus.Text = "Selecione ou crie uma conta primeiro (aba Contas).";
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Escolha o arquivo da skin",
            Filter = "Skin do Minecraft (*.png)|*.png",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            StoreSkin(acc, File.ReadAllBytes(dlg.FileName), null,
                IOPath.GetFileNameWithoutExtension(dlg.FileName));
            SkinStatus.Text = "Skin carregada! " + NextStepHint(acc);
        }
        catch (InvalidOperationException ex)
        {
            SkinStatus.Text = ex.Message;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao abrir arquivo de skin", ex);
            SkinStatus.Text = "Erro: " + ex.Message;
        }
    }

    private void RemoveSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        try { File.Delete(SkinFilePath(_active)); } catch { /* não é crítico */ }
        SkinStatus.Text = "Skin removida desta conta.";
        RefreshSkinUi();
    }

    private static string NextStepHint(Account acc) =>
        acc.Type == AccountType.Microsoft
            ? "Clique em “Enviar para a Mojang” para ela valer no jogo e no NameMC."
            : "Ela será aplicada quando você clicar em Jogar.";

    // Valida/converte a imagem para 64×64, guarda na biblioteca e usa como skin da conta.
    private void StoreSkin(Account acc, byte[] raw, bool? slimHint, string label)
    {
        var png = SkinTools.NormalizeToPng(raw);
        var current = _state.Accounts.FirstOrDefault(a => a.Key == acc.Key) ?? acc;
        var slim = slimHint ?? current.SkinSlim;

        SaveToLibrary(label, png, slim);
        ApplySkin(current, png, slim);
    }

    // Define a skin da conta. Só atualiza a conta se o modelo (braço fino/grosso) mudou; senão redesenha a prévia.
    private void ApplySkin(Account current, byte[] png, bool slim)
    {
        Directory.CreateDirectory(SkinsDir);
        File.WriteAllBytes(SkinFilePath(current), png);

        if (slim != current.SkinSlim) UpdateAccount(current with { SkinSlim = slim });
        else RefreshSkinUi();
    }

    // ---------- Biblioteca de skins (vale para todas as contas) ----------

    private sealed record SkinEntry(string Label, string FilePath, bool Slim)
    {
        public string Display => Slim ? $"{Label}  ·  braço fino" : Label;
    }

    private static string SafeLabel(string raw)
    {
        var l = Regex.Replace(raw.Trim(), "[^A-Za-z0-9_-]", "_").Trim('_');
        if (l.Length == 0) l = "skin";
        return l.Length > 24 ? l[..24] : l;
    }

    private static string LabelFor(string input)
    {
        var m = Regex.Match(input, @"namemc\.com/profile/([A-Za-z0-9_]{3,16})", RegexOptions.IgnoreCase);
        if (m.Success) return SafeLabel(m.Groups[1].Value);
        m = Regex.Match(input, @"namemc\.com/skin/([0-9a-fA-F]{16})", RegexOptions.IgnoreCase);
        if (m.Success) return "namemc-" + m.Groups[1].Value[..6].ToLowerInvariant();
        if (McNameRx.IsMatch(input)) return SafeLabel(input);
        return "skin-" + DateTime.Now.ToString("MMdd-HHmmss");
    }

    private static void SaveToLibrary(string label, byte[] png, bool slim)
    {
        try
        {
            Directory.CreateDirectory(LibraryDir);
            var baseName = IOPath.Combine(LibraryDir, SafeLabel(label));
            File.Delete(baseName + ".png");
            File.Delete(baseName + ".slim.png");
            File.WriteAllBytes(baseName + (slim ? ".slim.png" : ".png"), png);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui guardar a skin na biblioteca", ex);
        }
    }

    private void RefreshSkinLibrary()
    {
        var list = new List<SkinEntry>();
        try
        {
            if (Directory.Exists(LibraryDir))
                foreach (var f in Directory.EnumerateFiles(LibraryDir, "*.png")
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var name = IOPath.GetFileName(f);
                    var slim = name.EndsWith(".slim.png", StringComparison.OrdinalIgnoreCase);
                    list.Add(new SkinEntry(name[..^(slim ? 9 : 4)], f, slim));
                }
        }
        catch { /* biblioteca vazia */ }

        SkinLibList.ItemsSource = list;
        SkinLibEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UseLibrarySkin_Click(object sender, RoutedEventArgs e)
    {
        var acc = _active;
        if (acc == null)
        {
            SkinStatus.Text = "Selecione ou crie uma conta primeiro (aba Contas).";
            return;
        }

        if (SkinLibList.SelectedItem is not SkinEntry entry)
        {
            SkinStatus.Text = "Escolha uma skin da lista primeiro.";
            return;
        }

        try
        {
            ApplySkin(acc, File.ReadAllBytes(entry.FilePath), entry.Slim);
            SkinStatus.Text = $"Usando a skin “{entry.Label}”. " + NextStepHint(acc);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao usar skin da biblioteca", ex);
            SkinStatus.Text = "Não consegui abrir essa skin: " + ex.Message;
        }
    }

    private void DeleteLibrarySkin_Click(object sender, RoutedEventArgs e)
    {
        if (SkinLibList.SelectedItem is not SkinEntry entry)
        {
            SkinStatus.Text = "Escolha uma skin da lista primeiro.";
            return;
        }

        try { File.Delete(entry.FilePath); } catch { /* não é crítico */ }
        SkinStatus.Text = $"“{entry.Label}” saiu da biblioteca.";
        RefreshSkinLibrary();
    }

    // ---------- Buscar skin (NameMC / Mojang / link direto) ----------

    private async Task<(byte[] Bytes, bool? Slim)> FetchSkinAsync(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var host = uri.Host.ToLowerInvariant();
            var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (host == "namemc.com" || host.EndsWith(".namemc.com", StringComparison.Ordinal))
            {
                // https://namemc.com/skin/6e8a6d2c80f9e54b  -> imagem da skin no CDN do NameMC
                if (segs.Length >= 2 && segs[0].Equals("skin", StringComparison.OrdinalIgnoreCase))
                {
                    var id = segs[1];
                    if (!NameMcSkinIdRx.IsMatch(id))
                        throw new InvalidOperationException("Não reconheci o link da skin do NameMC. Copie o link da página da skin.");
                    return (await DownloadBytesAsync($"https://s.namemc.com/i/{id.ToLowerInvariant()}.png"), null);
                }

                // https://namemc.com/profile/Nick.1  -> pega a skin atual desse jogador pela Mojang
                if (segs.Length >= 2 && segs[0].Equals("profile", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(segs[1], @"^([A-Za-z0-9_]{3,16})(\.\d+)?$");
                    if (m.Success) return await FetchProfileSkinAsync(m.Groups[1].Value);
                    if (UuidRx.IsMatch(segs[1])) return await FetchProfileSkinAsync(segs[1]);
                }

                throw new InvalidOperationException(
                    "Link do NameMC não reconhecido. Use o link de um perfil (namemc.com/profile/Nick) ou de uma skin (namemc.com/skin/…).");
            }

            // Link direto para um .png (só https; a textures.minecraft.net às vezes vem em http).
            var direct = host == "textures.minecraft.net" ? new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri : uri;
            if (direct.Scheme != "https")
                throw new InvalidOperationException("Por segurança, só aceito links https.");
            return (await DownloadBytesAsync(direct.ToString()), null);
        }

        if (UuidRx.IsMatch(input) || McNameRx.IsMatch(input))
            return await FetchProfileSkinAsync(input);

        throw new InvalidOperationException(
            "Não entendi isso. Digite um nick (3 a 16 letras/números/_), um UUID, um link do NameMC ou um link .png.");
    }

    private static async Task<byte[]> DownloadBytesAsync(string url)
    {
        using var resp = await Http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Essa skin não foi encontrada (link errado ou removida).");
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 1_000_000)
            throw new InvalidOperationException("Esse arquivo é grande demais para ser uma skin.");
        return bytes;
    }

    // Nick ou UUID -> skin atual do jogador, direto da Mojang (a mesma que o NameMC mostra).
    private async Task<(byte[] Bytes, bool? Slim)> FetchProfileSkinAsync(string nameOrUuid)
    {
        string uuid;
        if (UuidRx.IsMatch(nameOrUuid))
        {
            uuid = nameOrUuid.Replace("-", "");
        }
        else
        {
            uuid = await ResolveUuidAsync(nameOrUuid)
                   ?? throw new InvalidOperationException($"Não achei o jogador “{nameOrUuid}” na Mojang. Confira o nick.");
        }

        using var resp = await Http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}");
        if ((int)resp.StatusCode == 429)
            throw new InvalidOperationException("A Mojang pediu um tempinho (muitas buscas seguidas). Tente de novo em 1 minuto.");
        if (resp.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Não achei o perfil desse jogador na Mojang.");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        string? texturesB64 = null;
        if (doc.RootElement.TryGetProperty("properties", out var props))
            foreach (var p in props.EnumerateArray())
                if (p.TryGetProperty("name", out var n) && n.GetString() == "textures")
                    texturesB64 = p.GetProperty("value").GetString();

        if (texturesB64 == null)
            throw new InvalidOperationException("Esse jogador não tem skin própria (usa a padrão Steve/Alex).");

        using var tex = JsonDocument.Parse(Convert.FromBase64String(texturesB64));
        if (!tex.RootElement.TryGetProperty("textures", out var t) || !t.TryGetProperty("SKIN", out var skin))
            throw new InvalidOperationException("Esse jogador não tem skin própria (usa a padrão Steve/Alex).");

        var url = skin.GetProperty("url").GetString()
                  ?? throw new InvalidOperationException("A Mojang não informou o endereço da skin.");
        var slim = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var model)
                   && model.GetString() == "slim";

        var u = new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri.ToString();
        return (await DownloadBytesAsync(u), slim);
    }

    private static async Task<string?> ResolveUuidAsync(string name)
    {
        foreach (var url in new[]
                 {
                     $"https://api.minecraftservices.com/minecraft/profile/lookup/name/{Uri.EscapeDataString(name)}",
                     $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}"
                 })
        {
            try
            {
                using var r = await Http.GetAsync(url);
                if (!r.IsSuccessStatusCode) continue;
                using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                if (d.RootElement.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s) return s;
            }
            catch (HttpRequestException) { }
            catch (JsonException) { }
        }

        return null;
    }

    // ---------- Conta Microsoft: enviar para a Mojang (aparece no NameMC) ----------

    private async void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { Type: AccountType.Microsoft } acc)
        {
            SkinStatus.Text = "Esse botão é só para contas Microsoft. Contas offline usam a skin direto no jogo.";
            return;
        }

        var file = SkinFilePath(acc);
        if (!File.Exists(file))
        {
            SkinStatus.Text = "Escolha uma skin primeiro.";
            return;
        }

        SkinUploadBtn.IsEnabled = false;
        SkinStatus.Text = "Verificando sua conta Microsoft…";
        try
        {
            var session = await GetSessionAsync(acc);

            SkinStatus.Text = "Enviando a skin para a Mojang…";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(acc.SkinSlim ? "slim" : "classic"), "variant");
            var img = new ByteArrayContent(await File.ReadAllBytesAsync(file));
            img.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(img, "file", "skin.png");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
            {
                Content = form
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var resp = await Http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                SkinStatus.Text = "Pronto! Sua skin foi enviada. No jogo ela já vale; o NameMC pode levar alguns " +
                                  "minutos para atualizar (abra seu perfil lá para forçar).";
                return;
            }

            var body = await resp.Content.ReadAsStringAsync();
            LauncherLog.Write($"Envio de skin recusado: {(int)resp.StatusCode} {body}");
            SkinStatus.Text = (int)resp.StatusCode switch
            {
                401 or 403 => "A Mojang recusou o login desta conta. Entre de novo com a Microsoft na aba Contas.",
                429 => "Muitos envios seguidos. Espere um pouco e tente de novo.",
                _ => $"A Mojang recusou a skin (erro {(int)resp.StatusCode}). Confira se o arquivo é uma skin 64×64 válida."
            };
        }
        catch (HttpRequestException)
        {
            SkinStatus.Text = "Não consegui falar com a Mojang. Verifique sua internet.";
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao enviar skin", ex);
            SkinStatus.Text = "Não foi possível enviar: " + ex.Message;
        }
        finally
        {
            SkinUploadBtn.IsEnabled = _active?.Type == AccountType.Microsoft && _active != null && File.Exists(SkinFilePath(_active));
        }
    }

    private void OpenNameMc_Click(object sender, RoutedEventArgs e) => OpenUrl("https://namemc.com/");

    private void OpenMyNameMc_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { Type: AccountType.Microsoft } acc)
            OpenUrl($"https://namemc.com/profile/{Uri.EscapeDataString(acc.Name)}");
    }

    // ---------- Conta offline: aplica a skin no jogo (resource pack) ----------

    // Chamado ao clicar em Jogar. Conta offline/Moonlight com skin -> cria/atualiza o pack e liga no options.txt.
    // Qualquer outro caso -> desliga o pack (para não sobrar nada de uma conta offline anterior).
    private void SyncSkinPack(Account acc, string mcVersion, bool offline)
    {
        var packFile = IOPath.Combine(_root, "resourcepacks", SkinPackName);
        var skinFile = SkinFilePath(acc);

        var m = Regex.Match(mcVersion, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        var major = m.Success ? int.Parse(m.Groups[1].Value) : 1;
        var minor = m.Success ? int.Parse(m.Groups[2].Value) : 20;
        var patch = m.Success && m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        var legacyNames = major == 1 && minor < 13; // até a 1.12 o options.txt não usa o prefixo "file/"

        if (offline && File.Exists(skinFile))
        {
            // O formato exato do pack vem do próprio jogo (version.json dentro do .jar); a tabela é só reserva.
            var jar = IOPath.Combine(_root, "versions", mcVersion, mcVersion + ".jar");
            var format = SkinTools.ReadPackFormat(jar) ?? SkinTools.PackFormat(major, minor, patch);

            Directory.CreateDirectory(IOPath.GetDirectoryName(packFile)!);
            SkinTools.BuildSkinPack(packFile, File.ReadAllBytes(skinFile), format);
            SetSkinPackEnabled(true, legacyNames);
            LauncherLog.Write($"Skin offline aplicada: conta={acc.Key}, versão={mcVersion}, pack_format={format}");
        }
        else
        {
            SetSkinPackEnabled(false, legacyNames);
            if (File.Exists(packFile))
            {
                File.Delete(packFile);
                LauncherLog.Write($"Skin offline desligada (conta={acc.Key}, offline={offline})");
            }
        }
    }

    // Liga/desliga o pack no options.txt, sem mexer em nenhuma outra linha.
    // Além de "resourcePacks", marca o pack em "incompatibleResourcePacks": é o que o próprio jogo faz quando você
    // aceita usar um pack de outra versão. Sem isso, o Minecraft apaga o pack da lista ao abrir se achar que ele
    // não combina com a versão, e a skin some.
    private void SetSkinPackEnabled(bool enable, bool legacyNames)
    {
        var optionsFile = IOPath.Combine(_root, "options.txt");
        var lines = File.Exists(optionsFile) ? File.ReadAllLines(optionsFile).ToList() : new List<string>();
        var entry = legacyNames ? SkinPackName : "file/" + SkinPackName;

        var changed = EditPackList(lines, "resourcePacks", enable, entry, addVanilla: !legacyNames);
        changed |= EditPackList(lines, "incompatibleResourcePacks", enable, entry, addVanilla: false);
        if (!changed) return;

        Directory.CreateDirectory(_root);
        File.WriteAllLines(optionsFile, lines, new UTF8Encoding(false));
    }

    private static bool EditPackList(List<string> lines, string key, bool enable, string entry, bool addVanilla)
    {
        var idx = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
        if (!enable && idx < 0) return false;

        var before = new List<string>(); // exatamente como estão escritos no arquivo
        if (idx >= 0)
            foreach (Match x in Regex.Matches(lines[idx], "\"((?:[^\"\\\\]|\\\\.)*)\""))
                before.Add(x.Groups[1].Value);

        var packs = before.Where(p => p != SkinPackName && p != "file/" + SkinPackName).ToList();
        if (enable)
        {
            if (addVanilla && packs.Count == 0) packs.Add("vanilla");
            packs.Add(entry); // por último = prioridade máxima
        }

        if (packs.SequenceEqual(before)) return false; // nada mudou

        var line = key + ":[" + string.Join(",", packs.Select(p => "\"" + p + "\"")) + "]";
        if (idx >= 0) lines[idx] = line; else lines.Add(line);
        return true;
    }
}

// ---------- Imagem da skin (sem depender de nada além do WPF) ----------

internal static class SkinTools
{
    // Lê um PNG e devolve os pixels BGRA. Array vazio = tamanho que não é de skin (só 64×64 e 64×32).
    private static byte[] Decode(byte[] data, out int w, out int h)
    {
        using var ms = new MemoryStream(data);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
        w = conv.PixelWidth;
        h = conv.PixelHeight;
        if (w != 64 || (h != 64 && h != 32)) return Array.Empty<byte>();

        var buf = new byte[w * h * 4];
        conv.CopyPixels(buf, w * 4, 0);
        return buf;
    }

    // O jogo ignora o alfa da camada de base (cabeça, corpo, braços e pernas): ela é sempre opaca.
    // Muitas skins escuras guardam alfa 0 nessas áreas; sem isto, partes do boneco "somem".
    private static void ForceOpaqueBase(byte[] px)
    {
        void Opaque(int x0, int y0, int x1, int y1)
        {
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    px[(y * 64 + x) * 4 + 3] = 255;
        }

        Opaque(0, 0, 32, 16);
        Opaque(0, 16, 64, 32);
        Opaque(16, 48, 48, 64);
    }

    // Aceita só PNG 64×64 ou 64×32 (formato antigo) e devolve sempre um PNG 64×64 no padrão do jogo.
    public static byte[] NormalizeToPng(byte[] data)
    {
        if (data.Length < 8 || data.Length > 1_000_000 ||
            data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            throw new InvalidOperationException("Esse arquivo não é uma imagem PNG válida.");

        byte[] src;
        int w, h;
        try { src = Decode(data, out w, out h); }
        catch { throw new InvalidOperationException("Não consegui ler essa imagem. Ela parece corrompida."); }

        if (src.Length == 0)
            throw new InvalidOperationException($"A skin precisa ter 64×64 (ou 64×32) pixels. Essa tem {w}×{h}.");

        var px = new byte[64 * 64 * 4];
        Buffer.BlockCopy(src, 0, px, 0, src.Length);

        if (h == 32) // skin antiga: monta braço e perna esquerdos espelhando os direitos (igual ao jogo)
        {
            void Copy(int x, int y, int dx, int dy, int cw, int ch)
            {
                for (var j = 0; j < ch; j++)
                    for (var i = 0; i < cw; i++)
                    {
                        var s = ((y + j) * 64 + x + i) * 4;
                        var d = ((y + dy + j) * 64 + x + dx + (cw - 1 - i)) * 4;
                        Buffer.BlockCopy(px, s, px, d, 4);
                    }
            }

            Copy(4, 16, 16, 32, 4, 4);
            Copy(8, 16, 16, 32, 4, 4);
            Copy(0, 20, 24, 32, 4, 12);
            Copy(4, 20, 16, 32, 4, 12);
            Copy(8, 20, 8, 32, 4, 12);
            Copy(12, 20, 16, 32, 4, 12);
            Copy(44, 16, -8, 32, 4, 4);
            Copy(48, 16, -8, 32, 4, 4);
            Copy(40, 20, 0, 32, 4, 12);
            Copy(44, 20, -8, 32, 4, 12);
            Copy(48, 20, -16, 32, 4, 12);
            Copy(52, 20, -8, 32, 4, 12);
        }

        ForceOpaqueBase(px); // a camada de base do corpo nunca é transparente

        var bmp = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, px, 64 * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var outMs = new MemoryStream();
        enc.Save(outMs);
        return outMs.ToArray();
    }

    // Boneco de frente (16×32 px) montado a partir da skin, para a prévia.
    public static BitmapSource RenderFront(byte[] png, bool slim)
    {
        var src = Decode(png, out _, out var h);
        if (src.Length == 0 || h != 64) throw new InvalidOperationException("Skin inválida.");
        ForceOpaqueBase(src);

        var dst = new byte[16 * 32 * 4];
        var aw = slim ? 3 : 4;

        // camada de base
        Blit(src, 8, 8, 8, 8, dst, 4, 0, false);          // cabeça
        Blit(src, 20, 20, 8, 12, dst, 4, 8, false);       // corpo
        Blit(src, 44, 20, aw, 12, dst, 4 - aw, 8, false); // braço direito
        Blit(src, 36, 52, aw, 12, dst, 12, 8, false);     // braço esquerdo
        Blit(src, 4, 20, 4, 12, dst, 4, 20, false);       // perna direita
        Blit(src, 20, 52, 4, 12, dst, 8, 20, false);      // perna esquerda
        // segunda camada (chapéu, casaco, mangas, calça)
        Blit(src, 40, 8, 8, 8, dst, 4, 0, true);
        Blit(src, 20, 36, 8, 12, dst, 4, 8, true);
        Blit(src, 44, 36, aw, 12, dst, 4 - aw, 8, true);
        Blit(src, 52, 52, aw, 12, dst, 12, 8, true);
        Blit(src, 4, 36, 4, 12, dst, 4, 20, true);
        Blit(src, 4, 52, 4, 12, dst, 8, 20, true);

        var bmp = BitmapSource.Create(16, 32, 96, 96, PixelFormats.Bgra32, null, dst, 16 * 4);
        bmp.Freeze();
        return bmp;
    }

    // Textura para o 3D: 64×64 vira 256×256 sem suavizar (cada pixel da skin vira um bloco 4×4).
    public static BitmapSource ToTexture(byte[] png)
    {
        var src = Decode(png, out _, out var h);
        if (src.Length == 0) throw new InvalidOperationException("Skin inválida.");

        if (h == 32) // formato antigo: converte para 64×64 antes
            src = Decode(NormalizeToPng(png), out _, out h);
        if (src.Length == 0 || h != 64) throw new InvalidOperationException("Skin inválida.");
        ForceOpaqueBase(src);

        const int scale = 4;
        const int size = 64 * scale;
        var dst = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var sy = y / scale;
            for (var x = 0; x < size; x++)
            {
                var si = (sy * 64 + x / scale) * 4;
                var di = (y * size + x) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, dst, size * 4);
        bmp.Freeze();
        return bmp;
    }

    // Pixels BGRA 64×64 da skin (camada de base já opaca), para o desenhista do boneco 3D.
    public static byte[] ToPixels(byte[] png)
    {
        var src = Decode(png, out _, out var h);
        if (src.Length == 0) throw new InvalidOperationException("Skin inválida.");

        if (h == 32) // formato antigo: converte para 64×64 antes
            src = Decode(NormalizeToPng(png), out _, out h);
        if (src.Length == 0 || h != 64) throw new InvalidOperationException("Skin inválida.");

        ForceOpaqueBase(src);
        return src;
    }

    public static BitmapSource PlaceholderTexture() => ToTextureFromPixels(PlaceholderPixels());

    // Boneco "lunar" mostrado quando ainda não há skin: lilás, com olhinhos que acompanham o mouse.
    public static byte[] PlaceholderPixels()
    {
        var px = new byte[64 * 64 * 4];

        void Fill(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var i = (y * 64 + x) * 4;
                    px[i] = b;
                    px[i + 1] = g;
                    px[i + 2] = r;
                    px[i + 3] = 255;
                }
        }

        Fill(0, 0, 32, 16, 0xD9, 0xD0, 0xFF);    // cabeça
        Fill(16, 16, 40, 32, 0x7B, 0x63, 0xF0);  // corpo
        Fill(40, 16, 56, 32, 0xD9, 0xD0, 0xFF);  // braço direito
        Fill(32, 48, 48, 64, 0xD9, 0xD0, 0xFF);  // braço esquerdo
        Fill(0, 16, 16, 32, 0x2B, 0x2F, 0x6B);   // perna direita
        Fill(16, 48, 32, 64, 0x2B, 0x2F, 0x6B);  // perna esquerda

        // rosto (frente da cabeça = x 8..16, y 8..16)
        Fill(9, 11, 11, 13, 0x1A, 0x14, 0x46);
        Fill(13, 11, 15, 13, 0x1A, 0x14, 0x46);
        Fill(11, 14, 13, 15, 0xF2, 0x8F, 0xB8);

        return px;
    }

    private static BitmapSource ToTextureFromPixels(byte[] src)
    {
        const int scale = 4;
        const int size = 64 * scale;
        var dst = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                Buffer.BlockCopy(src, ((y / scale) * 64 + x / scale) * 4, dst, (y * size + x) * 4, 4);

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, dst, size * 4);
        bmp.Freeze();
        return bmp;
    }

    private static void Blit(byte[] s, int sx, int sy, int cw, int ch, byte[] d, int dx, int dy, bool overlay)
    {
        for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
            {
                var si = ((sy + y) * 64 + sx + x) * 4;
                var di = ((dy + y) * 16 + dx + x) * 4;
                var a = s[si + 3];

                if (overlay)
                {
                    if (a == 0) continue;
                    if (a < 255 && d[di + 3] != 0)
                    {
                        for (var c = 0; c < 3; c++)
                            d[di + c] = (byte)((s[si + c] * a + d[di + c] * (255 - a)) / 255);
                        d[di + 3] = 255;
                        continue;
                    }
                }

                d[di] = s[si];
                d[di + 1] = s[si + 1];
                d[di + 2] = s[si + 2];
                d[di + 3] = a;
            }
    }

    // ---------- Resource pack com a skin ----------

    // pack_format de cada versão do jogo (se for uma versão mais nova que a tabela, o supported_formats cobre).
    public static int PackFormat(int major, int minor, int patch)
    {
        if (major != 1) return 69;
        return minor switch
        {
            <= 8 => 1,
            9 or 10 => 2,
            11 or 12 => 3,
            13 or 14 => 4,
            15 => 5,
            16 => patch >= 2 ? 6 : 5,
            17 => 7,
            18 => 8,
            19 => patch <= 2 ? 9 : patch == 3 ? 12 : 13,
            20 => patch < 2 ? 15 : patch == 2 ? 18 : patch < 5 ? 22 : 32,
            21 => patch <= 1 ? 34 : patch <= 3 ? 42 : patch == 4 ? 46 : patch == 5 ? 55 : patch == 6 ? 63 : patch <= 8 ? 64 : 69,
            _ => 69
        };
    }

    // Lê o formato de resource pack que a versão do jogo espera (version.json dentro do .jar). Null se não achar.
    public static int? ReadPackFormat(string jarPath)
    {
        try
        {
            if (!File.Exists(jarPath)) return null;
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("version.json");
            if (entry == null) return null;

            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("pack_version", out var pv)) return null;
            if (pv.TryGetProperty("resource", out var r) && r.TryGetInt32(out var n)) return n;
            if (pv.TryGetProperty("resource_major", out var rm) && rm.TryGetInt32(out var n2)) return n2;
        }
        catch { /* usa a tabela */ }

        return null;
    }

    private static readonly string[] DefaultSkinNames =
        { "steve", "alex", "ari", "efe", "kai", "makena", "noor", "sunny", "zuri" };

    // Troca a skin padrão (Steve/Alex & cia) do jogo pela do usuário. Contas offline sempre caem numa skin
    // padrão, então é isso que aparece no jogo. Cobre as versões antigas (steve.png/alex.png) e as novas
    // (pastas wide/slim, a partir da 1.19.3).
    public static void BuildSkinPack(string zipPath, byte[] skinPng, int packFormat)
    {
        var mcmeta = "{\"pack\":{\"pack_format\":" + packFormat +
                     ",\"description\":\"Moonlight Launcher - skin offline\"," +
                     "\"supported_formats\":{\"min_inclusive\":1,\"max_inclusive\":999}}}";

        var tmp = zipPath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] bytes)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var es = entry.Open();
                es.Write(bytes, 0, bytes.Length);
            }

            Add("pack.mcmeta", new UTF8Encoding(false).GetBytes(mcmeta));
            Add("assets/minecraft/textures/entity/steve.png", skinPng);
            Add("assets/minecraft/textures/entity/alex.png", skinPng);
            foreach (var folder in new[] { "wide", "slim" })
                foreach (var name in DefaultSkinNames)
                    Add($"assets/minecraft/textures/entity/player/{folder}/{name}.png", skinPng);
        }

        File.Move(tmp, zipPath, overwrite: true);
    }
}
