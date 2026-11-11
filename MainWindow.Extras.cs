using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// Novidades da interface: céu estrelado, abertura com a lua piscando, animação ao abrir o jogo,
// aba Ajustes (desempenho) e o OptiFine automático.
public partial class MainWindow
{
    private SpaceBackground? _bg;
    private DispatcherTimer? _parallaxTimer;
    private DispatcherTimer? _memTimer;
    private double _px, _py;
    private bool _renderPaused;
    private bool _splashStarted;
    private bool _settingsReady;
    private int _launchAnimId;

    private IReadOnlyList<OptiFineBuild> _ofBuilds = Array.Empty<OptiFineBuild>();
    private bool _ofLoaded;
    private bool _ofLoading;
    private bool _ofTried; // evita ficar tentando de novo sem parar quando está sem internet

    // ---------- Céu e economia de recursos ----------

    private void InitSpace()
    {
        _bg = new SpaceBackground(StarCanvas, StarCanvasNear, SparkleCanvas,
            new[] { Blob1T, Blob2T, Blob3T }, MoonT, LogoT,
            new (Border, TranslateTransform)[] { (Shoot1, Shoot1T), (Shoot2, Shoot2T) });
        _bg.Build(Perf.Light);

        Perf.Changed += () => Dispatcher.Invoke(() =>
        {
            _bg?.Build(Perf.Light);
            if (_renderPaused || ThemeManager.SkyOpacity < 0.01) _bg?.Pause();
            ApplyLightRendering();
            UpdatePerfTexts();
        });

        _parallaxTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _parallaxTimer.Tick += ParallaxTick;
        ApplyLightRendering();

        StateChanged += (_, _) => UpdateRenderState();
        IsVisibleChanged += (_, _) => UpdateRenderState();

        // Janela em segundo plano (você está em outro programa): nada se mexe e nada é redesenhado.
        Activated += (_, _) => UpdateRenderState();
        Deactivated += (_, _) => UpdateRenderState();
    }

    // Ajustes que só valem no modo leve: sem efeito de profundidade do mouse e imagens com qualidade simples.
    private void ApplyLightRendering()
    {
        if (_parallaxTimer != null)
        {
            if (Perf.Light) _parallaxTimer.Stop();
            else if (!_parallaxTimer.IsEnabled) _parallaxTimer.Start();
        }

        if (Perf.Light) RenderOptions.SetBitmapScalingMode(WindowRoot, BitmapScalingMode.LowQuality);
        else WindowRoot.ClearValue(RenderOptions.BitmapScalingModeProperty);
    }

    // Efeito de profundidade: o céu se mexe um pouquinho ao contrário do mouse.
    private void ParallaxTick(object? sender, EventArgs e)
    {
        try { ParallaxStep(); }
        catch (Exception ex)
        {
            LauncherLog.Write("Parallax falhou (efeito desligado)", ex);
            _parallaxTimer?.Stop();
        }
    }

    private void ParallaxStep()
    {
        if (_renderPaused || Perf.Light || _bg == null || ThemeManager.SkyOpacity < 0.01 || WindowRoot.ActualWidth < 10) return;
        if (!CursorUtil.TryGetPosition(WindowRoot, out var p)) return;

        var nx = Math.Clamp(p.X / WindowRoot.ActualWidth * 2 - 1, -1.2, 1.2);
        var ny = Math.Clamp(p.Y / WindowRoot.ActualHeight * 2 - 1, -1.2, 1.2);
        if (Math.Abs(nx - _px) < 0.003 && Math.Abs(ny - _py) < 0.003) return; // parado: não faz nada

        _px += (nx - _px) * 0.15;
        _py += (ny - _py) * 0.15;
        _bg.SetParallax(_px, _py);
    }

    // Minimizado ou escondido (jogando): para as animações e o 3D e devolve memória ao Windows.
    private void UpdateRenderState()
    {
        var active = IsVisible && WindowState != WindowState.Minimized && (IsActive || !_splashDone);
        if (active == !_renderPaused) return;

        _renderPaused = !active;
        if (_renderPaused || ThemeManager.SkyOpacity < 0.01) _bg?.Pause(); else _bg?.Resume();
        SkinView.Paused = _renderPaused;
        SkinEditorView.Paused = _renderPaused;
        HomeSkinView.Paused = _renderPaused;

        if (_renderPaused)
            Dispatcher.BeginInvoke(() => Perf.TrimMemory(), DispatcherPriority.ApplicationIdle);
    }

    // ---------- Abertura: janela branca balançando e tremendo; depois abre soltando brilhinhos ----------

    private async Task FinishSplashAsync()
    {
        if (_splashStarted) return;
        _splashStarted = true;

        if (_introShaking)
        {
            _introFinal = true;
            _introTargetAmp = IntroFinalAmp;   // o tremor fica MUITO forte um instante antes de abrir
            await Task.Delay(750);
            StopIntroShake();
        }

        HideSplash();
    }

    // ---------- Animação ao abrir o Minecraft: treme, fica branco e some ----------

    private async Task PlayLaunchAnimationAsync()
    {
        var id = ++_launchAnimId;

        if (WindowState == WindowState.Minimized || double.IsNaN(Left) || double.IsNaN(Top))
        {
            Hide();
            return;
        }

        // Modo leve: sem tremor e sem clarão, o launcher só some.
        if (Perf.Light)
        {
            Hide();
            return;
        }

        var startLeft = Left;
        var startTop = Top;
        const int shakeMs = 850;

        try
        {
            FlashOverlay.Visibility = Visibility.Visible;
            FlashOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(shakeMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

            var rnd = new Random();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < shakeMs && id == _launchAnimId)
            {
                var t = sw.ElapsedMilliseconds / (double)shakeMs;
                var amp = 4 + 22 * t; // vai tremendo cada vez mais forte
                Left = startLeft + (rnd.NextDouble() * 2 - 1) * amp;
                Top = startTop + (rnd.NextDouble() * 2 - 1) * amp;
                await Task.Delay(16);
            }
        }
        finally
        {
            Left = startLeft;
            Top = startTop;
        }

        if (id != _launchAnimId) return;

        // Tudo branco por um instante e então some.
        await Task.Delay(140);
        if (id != _launchAnimId) return;

        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
        fade.Completed += (_, _) => tcs.TrySetResult();
        BeginAnimation(OpacityProperty, fade);
        await tcs.Task;

        if (id != _launchAnimId) return;
        Hide();
    }

    // Quando o jogo fecha: tira o branco e o launcher volta suave.
    private void RestoreAfterGame()
    {
        _launchAnimId++; // cancela qualquer animação ainda rodando

        FlashOverlay.BeginAnimation(OpacityProperty, null);
        FlashOverlay.Opacity = 0;
        FlashOverlay.Visibility = Visibility.Collapsed;

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        PlayWindowIntro();
    }

    // ---------- Popup ----------

    private void Overlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Overlay.Visibility != Visibility.Visible || Perf.Light) return;

        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };
        PopupScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        PopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // ---------- Aba Ajustes ----------

    private void InitSettingsPage()
    {
        foreach (var rb in PerfPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (rb.Tag?.ToString() ?? "") == Perf.Setting;

        // Sub-abas de Ajustes: ligadas aqui (e não no XAML) para nada disparar durante a criação da janela.
        SettingsTabGeneral.Checked += SettingsTab_Checked;
        SettingsTabCustomize.Checked += SettingsTab_Checked;
        SettingsTabGeneral.IsChecked = true;

        InitUpdateSettings();

        _settingsReady = true;
        UpdatePerfTexts();
    }

    // Sub-abas de Ajustes: Geral (desempenho) e Personalizar (temas, paletas, ícones e mouse).
    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        // Durante o InitializeComponent os painéis ainda não existem.
        if (SettingsGeneral == null || SettingsCustomize == null || sender is not RadioButton rb) return;

        var custom = rb.Tag?.ToString() == "Customize";
        SettingsGeneral.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        SettingsCustomize.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

        SetSettingsTimer(PageSettings.Visibility == Visibility.Visible && !custom);

        FrameworkElement shown = custom ? SettingsCustomize : SettingsGeneral;
        if (!Perf.Light) FadeIn(shown);
        PageSettings.ScrollToTop();
    }

    private void PerfMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady || sender is not RadioButton rb) return;

        var mode = rb.Tag?.ToString() ?? "Auto";
        _state.PerfMode = mode;
        SaveState();
        Perf.Apply(mode);
    }

    private void UpdatePerfTexts()
    {
        if (PerfInfo == null) return;
        PerfInfo.Text = Perf.EffectiveText;
        PerfMemText.Text = $"Memória usada pelo launcher agora: {Perf.WorkingSetMb} MB";
    }

    private void FreeMemory_Click(object sender, RoutedEventArgs e)
    {
        var before = Perf.WorkingSetMb;
        Perf.TrimMemory();
        var after = Perf.WorkingSetMb;
        PerfMemText.Text = $"Memória usada pelo launcher agora: {after} MB (antes: {before} MB)";
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var mb = OptiFineCatalog.ClearCache();
        _ofLoaded = false;
        _ofTried = false;
        _ofBuilds = Array.Empty<OptiFineBuild>();
        CacheInfo.Text = $"Cache limpo ({mb:0.#} MB liberados). Ele é refeito sozinho quando precisar.";
        if (_loader is Loader.OptiFine or Loader.ForgeOptiFine) _ = EnsureOptiFineCatalogAsync();
    }

    // Só atualiza o número de memória enquanto a aba Ajustes está aberta.
    private void SetSettingsTimer(bool on)
    {
        // O contador de memória só existe na sub-aba Geral; na Personalizar o timer fica parado.
        if (on && SettingsGeneral != null && SettingsGeneral.Visibility != Visibility.Visible) on = false;

        if (on)
        {
            UpdatePerfTexts();
            _memTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _memTimer.Tick -= MemTick;
            _memTimer.Tick += MemTick;
            _memTimer.Start();
        }
        else
        {
            _memTimer?.Stop();
        }
    }

    private void MemTick(object? sender, EventArgs e) => UpdatePerfTexts();

    // ---------- OptiFine automático ----------

    private async Task EnsureOptiFineCatalogAsync()
    {
        if (_ofLoaded || _ofLoading) return;
        _ofLoading = true;
        try
        {
            _ofBuilds = await OptiFineCatalog.GetBestPerMcAsync();
            _ofLoaded = _ofBuilds.Count > 0;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("OptiFine: erro ao carregar a lista", ex);
        }
        finally
        {
            _ofLoading = false;
            _ofTried = true;
        }

        if (_loader is Loader.OptiFine or Loader.ForgeOptiFine) ApplyLoader();
    }

    // Versões do Minecraft que têm OptiFine (a lista oficial + as que já estão instaladas).
    private IEnumerable<string> OptiFineVersionNames()
    {
        var set = new HashSet<string>(_ofBuilds.Select(b => b.Mc));
        foreach (var v in ExtraVersions) set.Add(v); // 26.1 … 26.3 sempre aparecem (o OptiFine é baixado quando existir)
        if (_loader == Loader.OptiFine)
            foreach (var v in ScanLocalVersions().Where(v => v.Kind == Loader.OptiFine)) set.Add(v.Mc);

        return set
            .Select(n => (Name: n, Ver: Version.TryParse(n, out var v) ? v : new Version(0, 0)))
            .OrderByDescending(x => x.Ver)
            .Select(x => x.Name);
    }

    // Acha o Java que o Minecraft dessa versão usa (e garante o Minecraft original instalado).
    private async Task<string> FindJavaAsync(string mc)
    {
        var probe = await _launcher.InstallAndBuildProcessAsync(mc, new MLaunchOption
        {
            Session = MSession.CreateOfflineSession("Jogador")
        });
        return probe.StartInfo.FileName;
    }

    // OptiFine puro: baixa o .jar da versão exata e instala. Se já está instalado, usa direto.
    private async Task<string> EnsureOptiFineVersionAsync(string mc)
    {
        if (FindLocalVersion(mc, Loader.OptiFine) is { } local) return local.Id;

        var build = await FindOptiFineBuildAsync(mc);

        StatusText.Text = $"Baixando o OptiFine para o Minecraft {mc}…";
        var jar = await OptiFineCatalog.EnsureJarAsync(build);

        StatusText.Text = $"Preparando o Minecraft {mc}…";
        var java = await FindJavaAsync(mc);

        try
        {
            return await OptiFineInstaller.InstallAsync(_root, build, java, jar, s => StatusText.Text = s);
        }
        catch (Exception ex)
        {
            // Plano B: o instalador oficial (uma janela com o botão Install).
            LauncherLog.Write("OptiFine: instalação automática falhou, abrindo o instalador oficial", ex);
            StatusText.Text = "Na janela do OptiFine, clique em “Install”…";
            await OptiFineInstaller.RunGuiAsync(java, jar);

            return FindLocalVersion(mc, Loader.OptiFine)?.Id
                   ?? throw new InvalidOperationException("A instalação do OptiFine não foi concluída.", ex);
        }
    }

    // Acha o OptiFine da versão exata: na lista em cache; se não estiver (ex.: 26.1 … 26.3), pergunta direto por ela.
    private async Task<OptiFineBuild> FindOptiFineBuildAsync(string mc)
    {
        await EnsureOptiFineCatalogAsync();
        var build = _ofBuilds.FirstOrDefault(b => b.Mc == mc) ?? await OptiFineCatalog.FindForMcAsync(mc);
        return build ?? throw new InvalidOperationException(
            $"O OptiFine ainda não lançou uma versão para o Minecraft {mc}. Assim que sair, o launcher baixa sozinho: é só tentar de novo mais tarde.");
    }

    // Forge + OptiFine: baixa o OptiFine da versão exata direto para a pasta mods.
    private async Task EnsureOptiFineModAsync(string mc)
    {
        var mods = IOPath.Combine(_root, "mods");
        Directory.CreateDirectory(mods);
        var disabled = IOPath.Combine(mods, "_desativados_moonlight");

        // OptiFine de outra versão do Minecraft sai da pasta mods (senão o jogo trava).
        foreach (var f in Directory.GetFiles(mods, "*OptiFine*.jar"))
        {
            if (IOPath.GetFileName(f).Contains($"_{mc}_")) continue;
            Directory.CreateDirectory(disabled);
            File.Move(f, IOPath.Combine(disabled, IOPath.GetFileName(f)), true);
        }

        if (Directory.GetFiles(mods, "*OptiFine*.jar").Length > 0) return; // já tem o da versão certa

        var build = await FindOptiFineBuildAsync(mc);

        StatusText.Text = $"Baixando o OptiFine para o Minecraft {mc}…";
        var jar = await OptiFineCatalog.EnsureJarAsync(build);
        File.Copy(jar, IOPath.Combine(mods, build.FileName), true);
    }
}
