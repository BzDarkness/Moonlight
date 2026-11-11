using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.ProcessBuilder;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public partial class MainWindow : Window
{
    private static readonly Regex VanillaName = new(@"^\d+\.\d+(\.\d+)?$");
    // Propriedade (e não campo): campos estáticos de arquivos parciais diferentes não têm ordem garantida
    // e Skins.cs/Modpacks.cs usam isto ao iniciar.
    private static string AppDataDir => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string StateFile = IOPath.Combine(AppDataDir, "MoonlightLauncher", "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Dictionary<Loader, (string Title, string Desc)> Info = new()
    {
        [Loader.Vanilla]       = ("Vanilla", "Minecraft puro. Todas as versões oficiais, da 1.0 até a mais recente."),
        [Loader.OptiFine]      = ("OptiFine", "Mais FPS e suporte a shaders. Escolha a versão: o OptiFine certo é baixado e instalado sozinho."),
        [Loader.Forge]         = ("Forge", "O modloader clássico. O Forge é baixado e instalado automaticamente."),
        [Loader.Fabric]        = ("Fabric", "Leve e moderno, a partir da 1.14. Instalado automaticamente."),
        [Loader.NeoForge]      = ("NeoForge", "Sucessor do Forge, a partir da 1.20.2. Instalado automaticamente."),
        [Loader.ForgeOptiFine] = ("Forge + OptiFine", "Forge com o OptiFine já incluso na versão exata do Minecraft. Sem perguntas."),
    };

    private readonly string _root = IOPath.Combine(AppDataDir, ".minecraft");
    private readonly MinecraftPath _path;
    private readonly MinecraftLauncher _launcher;

    // Versões que sempre aparecem na lista (Vanilla, Forge, Fabric, NeoForge e Forge + OptiFine).
    private static readonly string[] ExtraVersions = { "26.1", "26.1.1", "26.1.2", "26.2", "26.3" };

    // Lista de reserva (sem internet): todas as versões oficiais de 1.0 em diante.
    private static readonly string[] BuiltInReleases =
    {
        "1.0", "1.1", "1.2.1", "1.2.2", "1.2.3", "1.2.4", "1.2.5", "1.3.1", "1.3.2", "1.4.2", "1.4.4",
        "1.4.5", "1.4.6", "1.4.7", "1.5", "1.5.1", "1.5.2", "1.6.1", "1.6.2", "1.6.4", "1.7.2", "1.7.4",
        "1.7.5", "1.7.6", "1.7.7", "1.7.8", "1.7.9", "1.7.10", "1.8", "1.8.1", "1.8.2", "1.8.3", "1.8.4",
        "1.8.5", "1.8.6", "1.8.7", "1.8.8", "1.8.9", "1.9", "1.9.1", "1.9.2", "1.9.3", "1.9.4", "1.10",
        "1.10.1", "1.10.2", "1.11", "1.11.1", "1.11.2", "1.12", "1.12.1", "1.12.2", "1.13", "1.13.1",
        "1.13.2", "1.14", "1.14.1", "1.14.2", "1.14.3", "1.14.4", "1.15", "1.15.1", "1.15.2", "1.16",
        "1.16.1", "1.16.2", "1.16.3", "1.16.4", "1.16.5", "1.17", "1.17.1", "1.18", "1.18.1", "1.18.2",
        "1.19", "1.19.1", "1.19.2", "1.19.3", "1.19.4", "1.20", "1.20.1", "1.20.2", "1.20.3", "1.20.4",
        "1.20.5", "1.20.6", "1.21", "1.21.1", "1.21.2", "1.21.3", "1.21.4", "1.21.5", "1.21.6", "1.21.7",
        "1.21.8", "1.21.9", "1.21.10", "1.21.11"
    };

    private const string MaxMc = "26.3"; // última versão que aparece nas listas

    private List<string> _releases = new();
    private List<string> _current = new();
    private Loader _loader = Loader.Vanilla;
    private bool _ready;
    private bool _busy;
    private AppState _state = new();

    private readonly Queue<string> _log = new();
    private string? _lastCrashFile;
    private TaskCompletionSource<string>? _popupTcs;

    public MainWindow()
    {
        InitializeComponent();

        _path = new MinecraftPath(_root);
        _launcher = new MinecraftLauncher(_path);

        _launcher.FileProgressChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            Progress.IsIndeterminate = false;
            Progress.Maximum = Math.Max(e.TotalTasks, 1);
            Progress.Value = e.ProgressedTasks;
            StatusText.Text = $"Baixando: {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})";
        });

        LoadState();
        Loaded += OnLoaded;
    }

    // ---------- Inicialização ----------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Abertura: janela branca balançando/tremendo. Fica no mínimo ~1,8 s, depois abre soltando brilhinhos.
        // (No modo leve: só um clarão rápido, sem tremor e sem brilhinhos.)
        // Se as versões demorarem, abre mesmo assim aos 4,5 s.
        var splashMin = Task.Delay(Perf.Light ? 600 : 1800);
        var splashCap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        splashCap.Tick += (_, _) =>
        {
            splashCap.Stop();
            _ = FinishSplashAsync();
        };
        splashCap.Start();

        InitSpace();
        InitSettingsPage();
        InitThemeUi();
        InitModsExtras();
        InitPlayView();
        RecoverModpackSession();
        VersionText.Text = Updater.CurrentText;
        PrepareIntro();
        PlayWindowIntro();
        StartIntroShake();
        RamSlider.Value = Math.Clamp(_state.Ram, 1024, 16384);
        RefreshAccountsUi();
        InitFriends();
        QueueShortcutSync(); // atalho na área de trabalho (1ª abertura)

        try
        {
            var all = await _launcher.GetAllVersionsAsync();
            _releases = all.Select(v => v.Name).Where(n => VanillaName.IsMatch(n)).ToList();
            StatusText.Text = "Pronto.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Erro ao carregar versões (sem internet?): " + ex.Message;
        }

        if (_releases.Count == 0) _releases = BuiltInReleases.ToList(); // sem internet: usa a lista embutida

        foreach (var extra in ExtraVersions)
            if (!_releases.Contains(extra)) _releases.Add(extra);

        _releases = _releases.Where(n => AtMost(n, MaxMc)).ToList(); // nada acima da 26.3

        _releases = _releases
            .Select(n => (Name: n, Ver: Version.TryParse(n, out var v) ? v : new Version(0, 0)))
            .OrderByDescending(x => x.Ver)
            .Select(x => x.Name)
            .ToList();

        _ready = true;

        foreach (var rb in LoaderPanel.Children.OfType<RadioButton>())
            if ((rb.Tag?.ToString() ?? "") == _state.Modo)
                rb.IsChecked = true;

        ApplyLoader();
        if (_loader is Loader.OptiFine or Loader.ForgeOptiFine) _ = EnsureOptiFineCatalogAsync();

        await splashMin;
        splashCap.Stop();
        await FinishSplashAsync();

        _ = AfterStartupUpdateAsync(); // aviso de "atualizado" + vigia novas versões (não trava nada sem internet)
        _ = Dispatcher.BeginInvoke(() => Perf.TrimMemory(), DispatcherPriority.ApplicationIdle);
    }

    private bool _splashDone;

    // O branco some com um fade e libera a tela de vez (Collapsed = não gasta mais nada).
    private void HideSplash()
    {
        if (_splashDone) return;
        _splashDone = true;

        Splash.IsHitTestVisible = false;

        if (Perf.Light)
        {
            // Modo leve: só um fade curto e tudo aparece de uma vez (sem cascata, sem zoom, sem brilhinhos).
            var quick = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            quick.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
            Splash.BeginAnimation(OpacityProperty, quick);

            TitleBar.Opacity = 1;
            NavPanel.Opacity = 1;
            foreach (var child in PagePlay.Children.OfType<FrameworkElement>()) child.Opacity = 1;
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        fade.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, fade);

        var zoom = new DoubleAnimation(1, 1.4, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        SplashContentS.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        SplashContentS.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);

        // Os menus entram em cascata e cada um solta brilhinhos quando aparece.
        Reveal(TitleBar, -14, 150);
        SparkleBurstAfter(TitleBar, 300, 8);
        Reveal(NavPanel, -10, 250);
        SparkleBurstAfter(NavPanel, 400, 16);
        int i = 0;
        foreach (var child in PagePlay.Children.OfType<FrameworkElement>())
        {
            Reveal(child, 22, 350 + 120 * i);
            SparkleBurstAfter(child, 500 + 120 * i, 10);
            i++;
        }
    }

    // Deixa a interface invisível enquanto a abertura cobre a tela (evita "piscar").
    private void PrepareIntro()
    {
        var items = new List<FrameworkElement> { TitleBar, NavPanel };
        items.AddRange(PagePlay.Children.OfType<FrameworkElement>());
        foreach (var el in items) el.Opacity = 0;
    }

    // A janela inteira entra com fade + zoom suave + leve subida (modo leve: só um fade curtinho).
    private void PlayWindowIntro()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (Perf.Light)
        {
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            WindowSlide.BeginAnimation(TranslateTransform.YProperty, null);
            WindowScale.ScaleX = WindowScale.ScaleY = 1;
            WindowSlide.Y = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            return;
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });

        var scale = new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
        };
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        WindowSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = ease });
    }

    // Aparece com fade + deslize, depois de um pequeno atraso (efeito cascata).
    private static void Reveal(FrameworkElement el, double fromY, int delayMs)
    {
        if (el.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            el.RenderTransform = tt;
        }

        var begin = TimeSpan.FromMilliseconds(delayMs);
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(550))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // ---------- Navegação ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PagePlay == null || PageAccounts == null || PageSkins == null || PageFolders == null ||
            PageMods == null || PageSettings == null || PageGolden == null || PageFriends == null) return;
        var tag = (sender as RadioButton)?.Tag?.ToString();

        var pages = new (FrameworkElement Page, string Tag)[]
        {
            (PagePlay, "Play"), (PageMods, "Mods"), (PageAccounts, "Accounts"),
            (PageSkins, "Skins"), (PageFolders, "Folders"), (PageSettings, "Settings"),
            (PageGolden, "Golden"), (PageFriends, "Friends")
        };

        foreach (var (page, t) in pages)
        {
            var show = tag == t;
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show && !Perf.Light) FadeIn(page);
        }

        if (tag != "Mods") SetModsBars(true, instant: true); // título volta ao normal nas outras abas
        SetSettingsTimer(tag == "Settings");
        if (tag == "Mods") EnterModsPage();
        if (tag == "Golden" && _splashDone) ShowGoldenFx(false); // animação com todos os benefícios
    }

    // Transição das páginas: aparece suavemente subindo um pouquinho.
    private static void FadeIn(FrameworkElement el)
    {
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));

        if (el.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            el.RenderTransform = tt;
        }

        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void GoTo(string tag)
    {
        if (tag == "Golden") { GoldenNav.IsChecked = true; return; }
        foreach (var rb in NavPanel.Children.OfType<RadioButton>())
            if (rb.Tag?.ToString() == tag)
                rb.IsChecked = true;
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e) => GoTo("Accounts");

    // ---------- Lista de versões ----------

    private void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && Enum.TryParse<Loader>(rb.Tag?.ToString(), out var l))
            _loader = l;

        if (_ready) ApplyLoader();
    }

    private static bool AtLeast(string version, string min) =>
        Version.TryParse(version, out var a) && Version.TryParse(min, out var b) && a >= b;

    private static bool AtMost(string version, string max) =>
        Version.TryParse(version, out var a) && Version.TryParse(max, out var b) && a <= b;

    private void ApplyLoader()
    {
        var (title, desc) = Info[_loader];
        LoaderTitle.Text = title;
        LoaderDesc.Text = desc;
        if (_splashDone && LoaderTitle.Parent is FrameworkElement titleBox) FadeIn(titleBox);

        var optiMode = _loader is Loader.OptiFine or Loader.ForgeOptiFine;
        OptiBar.Visibility = optiMode ? Visibility.Visible : Visibility.Collapsed;
        InstallOptiBtn.Visibility = _loader == Loader.OptiFine ? Visibility.Visible : Visibility.Collapsed;

        if (optiMode && !_ofLoaded && !_ofTried) _ = EnsureOptiFineCatalogAsync(); // lista de versões do OptiFine
        RefreshInstalledSet(); // versões já baixadas ficam verdes

        IEnumerable<string> source = _loader switch
        {
            Loader.OptiFine or Loader.ForgeOptiFine => OptiFineVersionNames(),
            Loader.Forge => _releases.Where(v => AtLeast(v, "1.5.2")),   // 1.ª versão do Forge com instalador
            Loader.Fabric => _releases.Where(v => AtLeast(v, "1.14")),   // 1.ª versão do Fabric
            Loader.NeoForge => _releases.Where(v => AtLeast(v, "1.20.2")), // 1.ª versão do NeoForge
            _ => _releases                                              // Vanilla: da 1.0 em diante
        };

        _current = source.ToList();
        FilterList();
    }

    private void FilterList()
    {
        var q = SearchBox.Text.Trim();
        var items = string.IsNullOrEmpty(q)
            ? _current
            : _current.Where(v => v.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        var prev = VersionList.SelectedItem as string;
        VersionList.ItemsSource = items;
        VersionList.SelectedItem = prev != null && items.Contains(prev) ? prev : items.FirstOrDefault();

        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _loader is Loader.OptiFine or Loader.ForgeOptiFine && !_ofLoaded && string.IsNullOrEmpty(q)
            ? "Buscando as versões do OptiFine…\n(precisa de internet na primeira vez)"
            : "Nenhuma versão encontrada.";

        PlayButton.IsEnabled = items.Count > 0 && !_busy;

        RebuildVersionCards(); // visão detalhada (cartões com banner); não faz nada na visão em lista
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) FilterList();
    }

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamLabel != null)
            RamLabel.Text = $"Memória RAM: {(int)e.NewValue} MB";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PlayButton.IsEnabled = !busy && VersionList.Items.Count > 0;
        InstallOptiBtn.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
        if (!busy) Progress.Value = 0;
    }

    // ---------- Popup (avisos e erros) ----------

    private Task<string> ShowPopupAsync(string title, string message, string? details,
        params (string Label, string Key)[] buttons)
    {
        _popupTcs?.TrySetResult("close");

        PopupTitle.Text = title;
        PopupMessage.Text = message;
        PopupDetails.Text = details ?? "";
        PopupDetailsBox.Visibility = string.IsNullOrWhiteSpace(details) ? Visibility.Collapsed : Visibility.Visible;

        PopupButtons.Children.Clear();
        for (int i = 0; i < buttons.Length; i++)
        {
            var (label, key) = buttons[i];
            var btn = new Button { Content = label, Tag = key };
            if (i == 0)
            {
                btn.Style = (Style)FindResource("PlayButton");
                btn.FontSize = 14;
                btn.Padding = new Thickness(22, 10, 22, 10);
            }
            else
            {
                btn.Style = (Style)FindResource("GhostButton");
                btn.Padding = new Thickness(16, 10, 16, 10);
            }
            btn.Click += (s, _) => ClosePopup((string)((Button)s).Tag);
            PopupButtons.Children.Insert(0, btn); // o primeiro botão fica à direita
        }

        Overlay.Visibility = Visibility.Visible;
        _popupTcs = new TaskCompletionSource<string>();
        return _popupTcs.Task;
    }

    private void ClosePopup(string key)
    {
        Overlay.Visibility = Visibility.Collapsed;
        _popupTcs?.TrySetResult(key);
    }

    // ---------- Jogar ----------

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, this)) _pendingPack = null; // clique normal no JOGAR: sem modpack
        if (VersionList.SelectedItem is not string selected) return;

        if (_active == null)
        {
            await ShowPopupAsync("Escolha uma conta",
                "Para jogar você precisa de uma conta. Crie uma conta Moonlight (só o nome) ou entre com a conta Microsoft.",
                null, ("Ir para Contas", "ok"));
            GoTo("Accounts");
            return;
        }

        // Verifica se há mods de outro loader na pasta mods.
        if (_loader is not (Loader.Vanilla or Loader.OptiFine))
        {
            var modsDir = _pendingPack != null ? _pendingPack.ModsDir : IOPath.Combine(_root, "mods");
            var warnings = await Task.Run(() => ModScanner.FindWrongLoaderMods(modsDir, _loader, selected));
            if (warnings.Count > 0)
            {
                var r = await ShowPopupAsync("⚠ Possível incompatibilidade de mods",
                    $"Encontrei mods que não combinam com o {Info[_loader].Title} escolhido. O jogo provavelmente vai travar.",
                    string.Join("\n", warnings),
                    ("Jogar mesmo assim", "play"), ("Abrir pasta de mods", "mods"), ("Cancelar", "cancel"));

                if (r == "mods") OpenFolder("mods");
                if (r != "play") return;
            }
        }

        LauncherLog.Write($"Jogar: conta={_active.Type}, modo={_loader}, versão={selected}");

        SetBusy(true);
        try
        {
            if (_loader == Loader.ForgeOptiFine)
                await EnsureOptiFineModAsync(selected); // baixa o OptiFine da versão exata para a pasta mods

            // Versão já baixada (verde) abre direto, com qualquer conta; só baixa o que falta.
            var alreadyInstalled = InstalledVersionConverter.Installed.Contains(selected);
            StatusText.Text = alreadyInstalled || _loader is Loader.Vanilla or Loader.OptiFine
                ? "Preparando o jogo…"
                : $"Instalando {Info[_loader].Title} para {selected} (pode demorar na primeira vez)…";

            var versionName = await PrepareVersionAsync(selected);
            var (session, offline) = await ResolveSessionAsync(_active);
            if (session == null)
            {
                StatusText.Text = "Cancelado.";
                return;
            }

            var opts = new MLaunchOption
            {
                Session = session,
                MaximumRamMb = (int)RamSlider.Value
            };

            // Skin local: só entra em contas offline. Para contas online, a skin vem da Mojang.
            try { SyncSkinPack(_active, selected, offline); }
            catch (Exception ex) { LauncherLog.Write("Não consegui preparar a skin offline", ex); }

            // Se tudo já estiver baixado, esta etapa é rápida; o progresso de downloads aparece sozinho.
            StatusText.Text = "Verificando os arquivos do jogo…";
            var process = await _launcher.InstallAndBuildProcessAsync(versionName, opts);

            StatusText.Text = $"Iniciando Minecraft como {_active.Name} ({_active.TypeLabel})…";
            if (_pendingPack != null) BeginModpackSession(_pendingPack);
            _gameMc = selected;
            _gameLoader = _loader;
            WatchProcess(process, DateTime.Now);

            _state.Modo = _loader.ToString();
            _state.Ram = (int)RamSlider.Value;
            SaveState();
            RefreshVersionMarks(); // a versão que acabou de ser baixada já fica verde
            StatusText.Text = "Jogo iniciado. Bom jogo! 🌙";
        }
        catch (Exception ex)
        {
            EndModpackSession();
            LauncherLog.Write($"Falha ao iniciar (conta={_active?.Type}, modo={_loader}, versão={selected})", ex);
            StatusText.Text = "Erro: " + ex.Message;
            var friendly = ex is HttpRequestException
                ? "Não consegui baixar algum arquivo. Verifique sua internet e tente de novo."
                : ex.Message;
            await ShowPopupAsync("Não foi possível iniciar o jogo", friendly,
                $"{ex.GetType().Name}: {ex.Message}", ("Fechar", "close"));
        }
        finally
        {
            _pendingPack = null;
            SetBusy(false);
        }
    }

    private async Task<string> PrepareVersionAsync(string mc)
    {
        switch (_loader)
        {
            // Fabric / Forge / NeoForge: se já existe instalado, usa direto, sem consultar a internet
            // e sem baixar de novo. Vale para qualquer conta (a pasta .minecraft é compartilhada).
            case Loader.Fabric:
            {
                if (FindLocalVersion(mc, Loader.Fabric) is { } local) return local.Id;
                return await InstallFabricAsync(mc);
            }
            case Loader.Forge:
            case Loader.ForgeOptiFine:
            {
                if (FindLocalVersion(mc, Loader.Forge) is { } local) return local.Id;
                return await new ForgeInstaller(_launcher).Install(mc);
            }

            case Loader.NeoForge:
            {
                if (FindLocalVersion(mc, Loader.NeoForge) is { } local) return local.Id;
                return await new NeoForgeInstaller(_launcher).Install(mc, new NeoForgeInstallOptions());
            }

            case Loader.OptiFine:
                return await EnsureOptiFineVersionAsync(mc);

            default: // Vanilla
                return mc;
        }
    }

    // Fabric: baixa o perfil direto da API oficial (meta.fabricmc.net) e grava na pasta versions.
    private async Task<string> InstallFabricAsync(string mc)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher/1.0");

        var listJson = await http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mc}");
        using var doc = JsonDocument.Parse(listJson);

        string? loaderVersion = null;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var loader = entry.GetProperty("loader");
            loaderVersion ??= loader.GetProperty("version").GetString();
            if (loader.TryGetProperty("stable", out var st) && st.GetBoolean())
            {
                loaderVersion = loader.GetProperty("version").GetString();
                break;
            }
        }

        if (loaderVersion == null)
            throw new InvalidOperationException($"O Fabric não tem suporte para o Minecraft {mc}.");

        var profileJson = await http.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loaderVersion}/profile/json");
        using var profile = JsonDocument.Parse(profileJson);
        var id = profile.RootElement.GetProperty("id").GetString()
                 ?? throw new InvalidOperationException("Resposta inválida do Fabric.");

        var dir = IOPath.Combine(_root, "versions", id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(IOPath.Combine(dir, id + ".json"), profileJson);

        try { await _launcher.GetAllVersionsAsync(); } catch { /* só atualiza a lista */ }
        return id;
    }

    // ---------- Monitoramento do jogo / crash ----------

    private void WatchProcess(Process process, DateTime started)
    {
        lock (_log) _log.Clear();

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        process.Exited += (_, _) =>
        {
            process.WaitForExit(); // garante que toda a saída foi lida
            var code = process.ExitCode;
            _ = Dispatcher.InvokeAsync(() => OnGameExited(code, started));
        };

        var gameArgs = process.StartInfo.ArgumentList.Count > 0
            ? string.Join(" ", process.StartInfo.ArgumentList)
            : process.StartInfo.Arguments;
        var cmd = process.StartInfo.FileName + " " + gameArgs;
        cmd = Regex.Replace(cmd, @"(--accessToken\s+)\S+", "$1***");
        LauncherLog.Write($"Comando do jogo (conta={_active?.Type}): {cmd}");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _ = PlayLaunchAnimationAsync(); // treme, fica branco e some enquanto o Minecraft estiver aberto
    }

    private void AppendLog(string? line)
    {
        if (line == null) return;
        FriendsOnGameLog(line);
        lock (_log)
        {
            _log.Enqueue(line);
            while (_log.Count > 800) _log.Dequeue();
        }
    }

    private async Task OnGameExited(int code, DateTime started)
    {
        // Reabre o launcher quando o Minecraft fecha
        EndModpackSession();
        FriendsOnGameExited();
        Show();
        RestoreAfterGame();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

        if (code == 0)
        {
            StatusText.Text = "Jogo fechado. Até a próxima! 🌙";
            return;
        }

        StatusText.Text = $"O jogo fechou com erro (código {code}).";

        string text;
        lock (_log) text = string.Join("\n", _log);

        LauncherLog.Write($"O jogo fechou com código {code} (conta={_active?.Type}). Últimas linhas:\n" +
                          string.Join("\n", text.Split('\n').TakeLast(60)));

        _lastCrashFile = FindCrashFile(started);
        if (_lastCrashFile != null)
        {
            try { text += "\n" + string.Join("\n", File.ReadLines(_lastCrashFile).Take(400)); }
            catch { /* ignora */ }
        }

        var info = CrashAnalyzer.Analyze(text, code);
        var r = await ShowPopupAsync("💥 " + info.Title, info.Explanation,
            string.Join("\n", info.Evidence),
            ("Abrir relatório", "report"), ("Abrir pasta de mods", "mods"), ("Fechar", "close"));

        if (r == "report") OpenReport();
        else if (r == "mods") OpenFolder("mods");
    }

    private string? FindCrashFile(DateTime started)
    {
        var cutoff = started.AddSeconds(-2);
        var files = new List<string>();

        var crashDir = IOPath.Combine(_root, "crash-reports");
        if (Directory.Exists(crashDir)) files.AddRange(Directory.GetFiles(crashDir, "crash-*.txt"));
        if (Directory.Exists(_root)) files.AddRange(Directory.GetFiles(_root, "hs_err_pid*.log"));

        return files.Where(f => File.GetLastWriteTime(f) >= cutoff)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();
    }

    private void OpenReport()
    {
        try
        {
            var target = _lastCrashFile;
            if (target == null || !File.Exists(target))
                target = IOPath.Combine(_root, "logs", "latest.log");

            if (File.Exists(target))
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                OpenFolder("logs");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível abrir o relatório: " + ex.Message;
        }
    }

    // ---------- Pastas ----------

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder((sender as FrameworkElement)?.Tag?.ToString() ?? "");

    private void OpenFolder(string sub)
    {
        var dir = string.IsNullOrEmpty(sub) ? _root : IOPath.Combine(_root, sub);
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível abrir a pasta: " + ex.Message;
        }
    }

    private void OpenLauncherLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(LauncherLog.FilePath))
                Process.Start(new ProcessStartInfo(LauncherLog.FilePath) { UseShellExecute = true });
            else
                StatusText.Text = "Ainda não existe nenhum log (nada deu errado até agora 🌙).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível abrir o log: " + ex.Message;
        }
    }

    // ---------- OptiFine ----------

    private void OpenOptiFineSite_Click(object sender, RoutedEventArgs e) => OpenUrl("https://optifine.net/downloads");

    // OptiFine puro: roda o instalador oficial do .jar.
    private async void InstallOptiFine_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Selecione o .jar do OptiFine", Filter = "OptiFine (*.jar)|*.jar" };
        if (dlg.ShowDialog(this) != true) return;

        var m = Regex.Match(IOPath.GetFileName(dlg.FileName), @"OptiFine_(\d+\.\d+(?:\.\d+)?)_");
        if (!m.Success)
        {
            MessageBox.Show(this, "Não consegui descobrir a versão do Minecraft pelo nome do arquivo.",
                "Moonlight Launcher");
            return;
        }

        var mc = m.Groups[1].Value;
        SetBusy(true);
        try
        {
            // O OptiFine precisa do Minecraft vanilla instalado e do Java.
            StatusText.Text = $"Preparando Minecraft {mc} (necessário para o OptiFine)…";
            var probe = await _launcher.InstallAndBuildProcessAsync(mc, new MLaunchOption
            {
                Session = MSession.CreateOfflineSession("Jogador")
            });

            var java = probe.StartInfo.FileName;
            var javaw = java.Replace("java.exe", "javaw.exe");
            if (File.Exists(javaw)) java = javaw;

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo(java, $"-jar \"{dlg.FileName}\"") { UseShellExecute = false },
                EnableRaisingEvents = true
            };
            proc.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                ApplyLoader();
                StatusText.Text = "Lista de OptiFine atualizada.";
            });
            proc.Start();

            StatusText.Text = "Na janela do OptiFine, clique em “Install”.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Erro: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Estado salvo ----------

    private void LoadState()
    {
        try
        {
            if (File.Exists(StateFile))
                _state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StateFile), JsonOpts) ?? new AppState();
        }
        catch { _state = new AppState(); }

        // O Ely.by foi removido: some com as contas antigas dele, mantendo todo o resto.
        if (_state.Accounts.RemoveAll(a => a.Type == AccountType.ElyBy) > 0)
        {
            if (_state.ActiveKey.StartsWith("ElyBy:", StringComparison.Ordinal) ||
                _state.Accounts.All(a => a.Key != _state.ActiveKey))
                _state.ActiveKey = _state.Accounts.FirstOrDefault()?.Key ?? "";
            SaveState();
        }

        CleanupElyLeftovers();
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(_state, JsonOpts));
        }
        catch { /* não é crítico */ }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---------- Janela ----------

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
