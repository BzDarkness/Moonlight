using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>Um modpack criado pelo jogador: pasta própria com os mods e um modpack.json.</summary>
public sealed class ModpackInfo
{
    public string Name { get; set; } = "";
    public string Mc { get; set; } = "";
    public string Loader { get; set; } = "fabric"; // fabric | forge | neoforge
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonIgnore] public string Dir { get; set; } = "";
    [JsonIgnore] public string ModsDir => IOPath.Combine(Dir, "mods");

    [JsonIgnore]
    public string LoaderLabel => Loader switch { "forge" => "Forge", "neoforge" => "NeoForge", _ => "Fabric" };

    [JsonIgnore] public string Summary => $"Minecraft {Mc}  ·  {LoaderLabel}";
}

internal static class ModpackStore
{
    public static readonly string Root = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "modpacks");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<ModpackInfo> Load()
    {
        var list = new List<ModpackInfo>();
        try
        {
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var file = IOPath.Combine(dir, "modpack.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var info = JsonSerializer.Deserialize<ModpackInfo>(File.ReadAllText(file), Opts);
                    if (info == null || string.IsNullOrWhiteSpace(info.Name)) continue;
                    info.Dir = dir;
                    list.Add(info);
                }
                catch (Exception ex) { LauncherLog.Write("Modpack ilegível: " + dir, ex); }
            }
        }
        catch (Exception ex) { LauncherLog.Write("Não consegui listar os modpacks", ex); }
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ModpackInfo Create(string name, string mc, string loader)
    {
        var slug = Regex.Replace(name.Trim(), "[^A-Za-z0-9_-]", "_").Trim('_');
        if (slug.Length == 0) slug = "modpack";
        var dir = IOPath.Combine(Root, slug);
        for (var i = 2; Directory.Exists(dir); i++) dir = IOPath.Combine(Root, $"{slug}-{i}");

        Directory.CreateDirectory(IOPath.Combine(dir, "mods"));
        var info = new ModpackInfo { Name = name.Trim(), Mc = mc, Loader = loader, Dir = dir };
        File.WriteAllText(IOPath.Combine(dir, "modpack.json"), JsonSerializer.Serialize(info, Opts));
        return info;
    }
}

internal sealed record Topic(string Id, string Label, int CfId, bool ForMods, bool ForPacks);

public partial class MainWindow
{
    // ---------- Sugestões por tema ----------

    private static readonly Topic[] AllTopics =
    {
        new("adventure", "⚔ Aventura", 422, true, true),
        new("magic", "✨ Magia", 419, true, true),
        new("technology", "⚙ Tecnologia", 412, true, true),
        new("optimization", "⚡ Otimização", 0, true, true),
        new("decoration", "🏡 Decoração", 0, true, false),
        new("equipment", "🛡 Equipamentos", 434, true, false),
        new("mobs", "🐾 Criaturas", 411, true, false),
        new("worldgen", "🌍 Mundo e biomas", 406, true, false),
        new("food", "🍖 Comida", 436, true, false),
        new("game-mechanics", "🎲 Mecânicas", 0, true, false),
        new("storage", "📦 Armazenamento", 420, true, false),
        new("transportation", "🚂 Transporte", 0, true, false),
        new("utility", "🧰 Utilidades", 5191, true, false),
        new("minigame", "🎮 Minigames", 0, true, false),
        new("library", "📚 Bibliotecas", 421, true, false),
        new("combat", "🗡 Combate", 0, false, true),
        new("quests", "📜 Missões", 0, false, true),
        new("lightweight", "🍃 Leve", 0, false, true),
        new("multiplayer", "👥 Multijogador", 0, false, true),
        new("kitchen-sink", "🧩 Completão", 0, false, true),
    };

    private static IReadOnlyList<Topic> TopicsFor(string type) => type switch
    {
        "mod" => AllTopics.Where(t => t.ForMods).ToList(),
        "modpack" => AllTopics.Where(t => t.ForPacks).ToList(),
        _ => Array.Empty<Topic>()
    };

    private void InitModsExtras()
    {
        _modsPicker = new VersionPicker(this, ModsMcBox, ModsMcArrow,
            () => _modsType == "mod" ? _modsLoader : "",
            () => _releases,
            ver => { _modsPage = 0; _ = SearchModsAsync(); });

        _packPicker = new VersionPicker(this, PackMcBox, PackMcArrow, () => _packLoader, () => _releases);
        BuildTopicChips();
        InitModsView();
    }

    private void BuildTopicChips()
    {
        if (TopicsPanel == null) return;
        TopicsPanel.Children.Clear();
        var topics = TopicsFor(_modsType);
        TopicsBar.Visibility = topics.Count > 0 && _modsType != "mypacks" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var t in topics)
        {
            var chip = new ToggleButton
            {
                Content = t.Label,
                Tag = t.Id,
                IsChecked = _topics.Contains(t.Id),
                Style = (Style)FindResource("TopicChip")
            };
            chip.Click += TopicChip_Click;
            TopicsPanel.Children.Add(chip);
        }
    }

    private void TopicChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.Tag is not string id) return;
        if (tb.IsChecked == true) _topics.Add(id); else _topics.Remove(id);
        _modsPage = 0;
        _ = SearchModsAsync();
    }

    private void TopicsClear_Click(object sender, RoutedEventArgs e)
    {
        _topics.Clear();
        foreach (var tb in TopicsPanel.Children.OfType<ToggleButton>()) tb.IsChecked = false;
        _modsPage = 0;
        _ = SearchModsAsync();
    }

    // ---------- Páginas dos resultados (1, 2, 3…) ----------

    private void BuildPager()
    {
        ModsPager.Children.Clear();
        var pages = Math.Min((_modsTotal + ContentApi.PageSize - 1) / ContentApi.PageSize, 200);
        if (pages <= 1)
        {
            ModsPager.Visibility = Visibility.Collapsed;
            return;
        }

        ModsPager.Visibility = Visibility.Visible;

        // Só os números: 1 … 4 5 [6] 7 8 … 40  (a página atual fica colorida).
        void Add(string text, int target, bool current, bool enabled = true)
        {
            var b = new Button
            {
                Content = text,
                Tag = target,
                IsEnabled = enabled,
                MinWidth = 38,
                Height = 34,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0),
                Style = (Style)FindResource(current ? "PlayButton" : "GhostButton")
            };
            b.FontWeight = current ? FontWeights.Bold : FontWeights.SemiBold;
            b.Click += (_, _) =>
            {
                if (target == _modsPage || target < 0) return;
                _modsPage = target;
                _ = SearchModsAsync();
            };
            ModsPager.Children.Add(b);
        }

        var show = new SortedSet<int> { 0, pages - 1 };
        for (var i = _modsPage - 2; i <= _modsPage + 2; i++)
            if (i >= 0 && i < pages) show.Add(i);

        Add("‹", _modsPage - 1, false, _modsPage > 0);
        var prev = -1;
        foreach (var i in show)
        {
            if (prev >= 0 && i - prev > 1)
            {
                var dots = new TextBlock
                {
                    Text = "…",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 2)
                };
                dots.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                ModsPager.Children.Add(dots);
            }
            Add((i + 1).ToString(), i, i == _modsPage);
            prev = i;
        }
        Add("›", _modsPage + 1, false, _modsPage < pages - 1);
    }

    // ---------- Meus modpacks ----------

    private List<ModpackInfo> _packs = new();
    private ModpackInfo? _selectedPack;
    private ModpackInfo? _pendingPack; // modpack que vai abrir quando o Minecraft iniciar
    private string _packLoader = "fabric";
    private bool _refreshingPacks;

    private void RefreshPackList()
    {
        if (PackList == null) return;
        _refreshingPacks = true;
        try
        {
            var keep = _selectedPack?.Dir;
            _packs = ModpackStore.Load();
            PackList.ItemsSource = _packs;
            PackEmpty.Visibility = _packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _selectedPack = _packs.FirstOrDefault(p => p.Dir == keep);
            PackList.SelectedItem = _selectedPack;
        }
        finally
        {
            _refreshingPacks = false;
        }
        ShowPackDetail();
    }

    private void PackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPacks) return;
        _selectedPack = PackList.SelectedItem as ModpackInfo;
        if (_selectedPack != null) PackForm.Visibility = Visibility.Collapsed;
        ShowPackDetail();
    }

    private void ShowPackDetail()
    {
        var p = _selectedPack;
        PackDetail.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        PackHint.Visibility = p == null && PackForm.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (p == null) return;

        PackTitle.Text = p.Name;
        PackSub.Text = p.Summary;
        PackStatus.Text = "";
        RefreshPackDetail();
    }

    private void RefreshPackDetail()
    {
        var p = _selectedPack;
        if (p == null) return;

        List<InstalledMod> mods;
        try { mods = ModLibrary.Scan(p.ModsDir); }
        catch (Exception ex)
        {
            LauncherLog.Write("Erro ao listar os mods do modpack", ex);
            mods = new List<InstalledMod>();
        }

        PackModsList.ItemsSource = mods;
        PackModsEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PackModsTitle.Text = $"MODS DESTE MODPACK  ·  {mods.Count}";
    }

    private void PackNew_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive) { GoTo("Golden"); return; }
        _refreshingPacks = true;
        PackList.SelectedItem = null;
        _refreshingPacks = false;
        _selectedPack = null;

        PackNameBox.Text = "";
        PackFormStatus.Text = "";
        if (VersionList.SelectedItem is string sel && Regex.IsMatch(sel, @"^\d+\.\d+(\.\d+)?$")) PackMcBox.Text = sel;
        PackForm.Visibility = Visibility.Visible;
        ShowPackDetail();
        PackNameBox.Focus();
    }

    private void PackCancel_Click(object sender, RoutedEventArgs e)
    {
        PackForm.Visibility = Visibility.Collapsed;
        ShowPackDetail();
    }

    private void PackLoader_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) _packLoader = rb.Tag?.ToString() ?? "fabric";
    }

    private void PackCreate_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive) { GoTo("Golden"); return; }
        var name = PackNameBox.Text.Trim();
        var mc = PackMcBox.Text.Trim();

        if (name.Length < 2) { PackFormStatus.Text = "Dê um nome para o modpack (pelo menos 2 letras)."; return; }
        if (!Regex.IsMatch(mc, @"^\d+\.\d+(\.\d+)?$"))
        {
            PackFormStatus.Text = "Escolha a versão do Minecraft (ex.: 1.20.1) ou toque na seta para ver a lista.";
            return;
        }
        if (_packs.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            PackFormStatus.Text = "Já existe um modpack com esse nome.";
            return;
        }

        try
        {
            var info = ModpackStore.Create(name, mc, _packLoader);
            PackForm.Visibility = Visibility.Collapsed;
            _selectedPack = info;
            RefreshPackList();
            PackList.SelectedItem = _packs.FirstOrDefault(p => p.Dir == info.Dir);
            PackStatus.Text = "Modpack criado! Clique em “Adicionar mods” para escolher os mods.";
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao criar modpack", ex);
            PackFormStatus.Text = "Não consegui criar: " + ex.Message;
        }
    }

    private void PackFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPack == null) return;
        try
        {
            Directory.CreateDirectory(_selectedPack.ModsDir);
            Process.Start(new ProcessStartInfo(_selectedPack.ModsDir) { UseShellExecute = true });
        }
        catch (Exception ex) { PackStatus.Text = "Não consegui abrir a pasta: " + ex.Message; }
    }

    private async void PackDelete_Click(object sender, RoutedEventArgs e)
    {
        var p = _selectedPack;
        if (p == null) return;

        var r = await ShowPopupAsync($"Apagar o modpack “{p.Name}”?",
            "Os mods guardados dentro dele serão apagados. Seus mundos e a pasta mods normal não são tocados.", null,
            ("Apagar", "yes"), ("Cancelar", "no"));
        if (r != "yes") return;

        try { Directory.Delete(p.Dir, true); }
        catch (Exception ex) { LauncherLog.Write("Falha ao apagar modpack", ex); }

        _selectedPack = null;
        RefreshPackList();
    }

    private void PackRemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not InstalledMod mod) return;
        try
        {
            File.Delete(mod.FilePath);
            PackStatus.Text = $"{mod.DisplayName} saiu do modpack.";
        }
        catch (Exception ex) { PackStatus.Text = "Não consegui tirar o mod: " + ex.Message; }
        RefreshPackDetail();
    }

    // "Adicionar mods": vai para a busca de mods, já com a versão e o loader do modpack.
    private void PackAddMods_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive) { GoTo("Golden"); return; }
        var p = _selectedPack;
        if (p == null) return;

        _packTarget = p;
        ContentItem.AddToPackMode = true;
        PackModeText.Text = $"🎒  Montando o modpack “{p.Name}”  ·  Minecraft {p.Mc}  ·  {p.LoaderLabel}.  Clique em “No modpack” nos mods que quiser.";
        PackModeBar.Visibility = Visibility.Visible;

        _modsUiReady = false;
        ModsMcBox.Text = p.Mc;
        _modsLoader = p.Loader;
        foreach (var rb in ModsLoaderPanel.Children.OfType<RadioButton>())
            rb.IsChecked = rb.Tag?.ToString() == p.Loader;
        _modsUiReady = true;

        SelectModsTab("mod");
        _modsPage = 0;
        _ = SearchModsAsync();
    }

    private void PackModeDone_Click(object sender, RoutedEventArgs e)
    {
        _packTarget = null;
        ContentItem.AddToPackMode = false;
        PackModeBar.Visibility = Visibility.Collapsed;
        ModsList.Items.Refresh();
        ModsCards.Items.Refresh();
        SelectModsTab("mypacks");
        RefreshPackDetail();
    }

    private void SelectModsTab(string tag)
    {
        foreach (var rb in FindLogical<RadioButton>(ModsBrowse))
            if (rb.GroupName == "modtype" && rb.Tag?.ToString() == tag)
                rb.IsChecked = true;
    }

    private static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            if (d is T t) yield return t;
            foreach (var x in FindLogical<T>(d)) yield return x;
        }
    }

    // ---------- Jogar direto por um modpack ----------

    private void PackPlay_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive) { GoTo("Golden"); return; }
        var p = _selectedPack;
        if (p == null) return;
        if (_busy) { PackStatus.Text = "Já estou abrindo o jogo, aguarde um instante."; return; }

        // Põe o loader e a versão do modpack na tela Jogar e clica em JOGAR.
        var tag = p.Loader switch { "forge" => "Forge", "neoforge" => "NeoForge", _ => "Fabric" };
        foreach (var rb in LoaderPanel.Children.OfType<RadioButton>())
            if (rb.Tag?.ToString() == tag) rb.IsChecked = true;

        SearchBox.Text = "";
        FilterList();
        if (!_current.Contains(p.Mc))
        {
            PackStatus.Text = $"O {p.LoaderLabel} não tem a versão {p.Mc} na lista do launcher.";
            return;
        }

        VersionList.SelectedItem = p.Mc;
        _pendingPack = p;
        GoTo("Play");
        Play_Click(this, new RoutedEventArgs());
    }

    // Os mods do modpack entram na pasta mods só enquanto o jogo está aberto; a pasta original fica guardada
    // e volta sozinha quando o jogo fecha (ou na próxima vez que o launcher abrir, se algo travar).
    private static readonly string PackSessionFile = IOPath.Combine(AppDataDir, "MoonlightLauncher", "modpack-session.txt");
    private string LiveModsDir => IOPath.Combine(_root, "mods");
    private string BackupModsDir => IOPath.Combine(_root, "mods_moonlight_backup");

    private void BeginModpackSession(ModpackInfo pack)
    {
        try
        {
            RestoreMods(); // garante que não sobrou uma troca antiga
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(IOPath.GetDirectoryName(PackSessionFile)!);

            if (Directory.Exists(LiveModsDir)) Directory.Move(LiveModsDir, BackupModsDir);
            else Directory.CreateDirectory(BackupModsDir);

            File.WriteAllText(PackSessionFile, pack.Name);
            Directory.CreateDirectory(LiveModsDir);
            if (Directory.Exists(pack.ModsDir))
                foreach (var f in Directory.GetFiles(pack.ModsDir))
                    File.Copy(f, IOPath.Combine(LiveModsDir, IOPath.GetFileName(f)), true);

            LauncherLog.Write($"Modpack “{pack.Name}” ligado (pasta mods original guardada).");
        }
        catch
        {
            try { RestoreMods(); } catch { /* ignora */ }
            throw;
        }
    }

    private void EndModpackSession()
    {
        try { RestoreMods(); }
        catch (Exception ex) { LauncherLog.Write("Não consegui devolver a pasta mods original", ex); }
    }

    private void RecoverModpackSession() => EndModpackSession();

    private void RestoreMods()
    {
        if (!File.Exists(PackSessionFile)) return;

        if (Directory.Exists(BackupModsDir))
        {
            if (Directory.Exists(LiveModsDir)) Directory.Delete(LiveModsDir, true);
            Directory.Move(BackupModsDir, LiveModsDir);
        }
        File.Delete(PackSessionFile);
        LauncherLog.Write("Pasta mods original devolvida.");
    }
}
