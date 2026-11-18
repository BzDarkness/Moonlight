using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MoonlightLauncher;

/// <summary>Item simples das listas da interface leve (o texto aparece, o valor fica guardado).</summary>
public sealed record LiteItem(string Text, object? Value)
{
    public override string ToString() => Text;
}

/// <summary>
/// Interface LEVE: uma janelinha simples, parecida com o Legacy Launcher (conta, versão, Jogar e quatro botões),
/// com o fundo da lua sobre as cerejeiras. Sem animações, sem estrelas, sem 3D, sem amigos na tela: quase não usa memória.
/// Toda a lógica (contas, versões, instalar e abrir o jogo) continua na MainWindow; esta janela só mostra e pede.
/// </summary>
public sealed class LiteWindow : Window
{
    private readonly MainWindow _main;
    private readonly ComboBox _accountBox = new();
    private readonly ComboBox _loaderBox = new();
    private readonly ComboBox _versionBox = new();
    private readonly CheckBox _onlyInstalled = new();
    private readonly CheckBox _hideOnPlay = new();
    private readonly Button _playBtn = new();
    private readonly ProgressBar _bar = new();
    private readonly TextBlock _status = new();
    private readonly DispatcherTimer _timer;

    private bool _updating;
    private bool _wasBusy;
    private string _lastStatus = "";

    /// <summary>A MainWindow liga isto antes de fechar esta janela de propósito (troca de modo): o launcher não fecha.</summary>
    internal bool Switching { get; set; }

    public LiteWindow(MainWindow main)
    {
        _main = main;

        Title = "Moonlight Launcher " + Updater.CurrentText + " [Leve]";
        Width = 960;
        Height = 580;
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x1A));
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico")); }
        catch { /* sem ícone: não é crítico */ }

        LiteTheme.Apply(this);

        var root = new Grid();
        var bg = LoadBackground();
        if (bg != null)
        {
            var img = new Image { Source = bg, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
            root.Children.Add(img);
        }
        // Véu em degradê: deixa a lua e as cerejeiras claras em cima e escurece embaixo, para o painel destacar.
        root.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x10, 0x05, 0x07, 0x18), Color.FromArgb(0x80, 0x05, 0x07, 0x18), 90)
        });
        root.Children.Add(BuildPanel());
        Content = root;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Tick();

        Loaded += (_, _) =>
        {
            RefreshAccounts();
            RefreshVersions();
            Tick();
            _timer.Start();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start(); else _timer.Stop();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (!Switching) _main.Close(); // fechar a janelinha fecha o launcher
        };
    }

    // ---------- Visual ----------

    private static BitmapImage? LoadBackground()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/lite/bg.jpg");
            bmp.DecodePixelWidth = 1472;            // tamanho da própria imagem: não guarda pixels a mais
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Interface leve: fundo não carregou", ex);
            return null;
        }
    }

    private static BitmapImage? LoadLogo()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/logo.png");
            bmp.DecodePixelWidth = 112;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Botão de vidro usado nos diálogos e nos ícones (mantido com o mesmo nome e assinatura de antes).</summary>
    internal static Style FlatButton(string bg, string hover, string press, string fg) =>
        LiteTheme.Glass(bg, hover, press, fg);

    /// <summary>Linha fina com degradê (some nas pontas), no lugar da linha branca reta.</summary>
    private static Border Rule() => new()
    {
        Height = 1,
        Margin = new Thickness(0, 0, 0, 0),
        Background = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0x00, 0xF7, 0xA8, 0xD3), 0),
            new GradientStop(Color.FromArgb(0xB0, 0xF7, 0xA8, 0xD3), 0.5),
            new GradientStop(Color.FromArgb(0x00, 0xF7, 0xA8, 0xD3), 1)
        }, 0)
    };

    /// <summary>Rótulo pequeno em cima de cada campo (CONTA, MODO, VERSÃO).</summary>
    private static TextBlock Caption(string text, Thickness margin) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = LiteTheme.B(LiteTheme.Label),
        Margin = margin
    };

    private static UIElement BuildHeader()
    {
        var g = new Grid { Margin = new Thickness(0, 22, 0, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logo = LoadLogo();
        if (logo != null)
        {
            var img = new Image { Source = logo, Width = 46, Height = 46, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
        }

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = "MOONLIGHT",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = LiteTheme.B("#FFFFFF")
        });
        texts.Children.Add(new TextBlock
        {
            Text = "Launcher  ·  " + Updater.CurrentText + "  ·  modo leve",
            FontSize = 11.5,
            Foreground = LiteTheme.B(LiteTheme.Label),
            Margin = new Thickness(1, 1, 0, 0)
        });
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        return g;
    }

    private void StyleCombo(ComboBox c, string tip)
    {
        c.Height = 38;
        c.FontSize = 14;
        c.VerticalContentAlignment = VerticalAlignment.Center;
        c.ToolTip = tip;
    }

    private UIElement BuildPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildHeader());
        stack.Children.Add(Rule());

        // Conta
        stack.Children.Add(Caption("CONTA", new Thickness(2, 16, 0, 5)));
        StyleCombo(_accountBox, "Conta");
        _accountBox.Margin = new Thickness(0, 0, 0, 12);
        _accountBox.SelectionChanged += AccountBox_Changed;
        stack.Children.Add(_accountBox);

        // Modo (Vanilla, Fabric…) e versão
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var capLoader = Caption("MODO", new Thickness(2, 0, 0, 5));
        Grid.SetColumn(capLoader, 0);
        row.Children.Add(capLoader);
        var capVersion = Caption("VERSÃO", new Thickness(2, 0, 0, 5));
        Grid.SetColumn(capVersion, 2);
        row.Children.Add(capVersion);

        StyleCombo(_loaderBox, "Modo (Vanilla, Fabric, Forge…)");
        _loaderBox.ItemsSource = Enum.GetValues<Loader>()
            .Select(l => new LiteItem(MainWindow.LiteLoaderTitle(l), l)).ToList();
        _loaderBox.SelectionChanged += LoaderBox_Changed;
        Grid.SetRow(_loaderBox, 1);
        Grid.SetColumn(_loaderBox, 0);
        row.Children.Add(_loaderBox);

        StyleCombo(_versionBox, "Versão do Minecraft (✔ = já baixada)");
        _versionBox.SelectionChanged += VersionBox_Changed;
        Grid.SetRow(_versionBox, 1);
        Grid.SetColumn(_versionBox, 2);
        row.Children.Add(_versionBox);
        stack.Children.Add(row);

        // Opções
        _onlyInstalled.Content = "Só versões baixadas";
        _onlyInstalled.FontSize = 13;
        _onlyInstalled.Margin = new Thickness(2, 2, 0, 8);
        _onlyInstalled.Click += (_, _) => RefreshVersions();
        stack.Children.Add(_onlyInstalled);

        _hideOnPlay.Content = "Esconder o launcher ao jogar";
        _hideOnPlay.FontSize = 13;
        _hideOnPlay.Margin = new Thickness(2, 0, 0, 16);
        _hideOnPlay.IsChecked = true;
        stack.Children.Add(_hideOnPlay);

        stack.Children.Add(Rule());

        // Jogar
        _playBtn.Content = "JOGAR";
        _playBtn.Height = 52;
        _playBtn.FontSize = 19;
        _playBtn.FontWeight = FontWeights.Bold;
        _playBtn.Margin = new Thickness(0, 18, 0, 12);
        _playBtn.Style = LiteTheme.Primary();
        _playBtn.Click += Play_Click;
        stack.Children.Add(_playBtn);

        // Quatro botões de ícone
        var icons = new UniformGrid { Columns = 4, Height = 38, Margin = new Thickness(-3, 0, -3, 0) };
        icons.Children.Add(IconButton("\uE77B", "#8FBFFF", "Contas", () => OpenAccounts()));
        icons.Children.Add(IconButton("\uE8B7", "#FFC770", "Abrir a pasta do jogo (.minecraft)", () => _main.LiteOpenFolder("")));
        icons.Children.Add(IconButton("\uE72C", "#7FEBB0", "Atualizar as versões baixadas", () =>
        {
            _main.LiteRescan();
            RefreshVersions();
        }));
        var menuBtn = IconButton("\uE700", "#F7A8D3", "Menu", null);
        menuBtn.Click += (_, _) => ShowMenu(menuBtn);
        icons.Children.Add(menuBtn);
        stack.Children.Add(icons);

        // Progresso e mensagem
        _bar.Height = 6;
        _bar.Margin = new Thickness(0, 14, 0, 0);
        _bar.Visibility = Visibility.Hidden;
        _bar.Foreground = LiteTheme.B("#F27DBA");
        _bar.Background = LiteTheme.B("#33FFFFFF");
        _bar.BorderThickness = new Thickness(0);
        stack.Children.Add(_bar);

        _status.Foreground = LiteTheme.B(LiteTheme.Soft);
        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.MinHeight = 16;
        stack.Children.Add(_status);

        // Painel de vidro escuro no meio. A sombra fica numa camada separada (atrás), para não borrar o texto.
        var shape = new Func<Border>(() => new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xD8, 0x13, 0x18, 0x38), Color.FromArgb(0xE6, 0x0A, 0x0D, 0x22), 90)
        });

        var shadow = shape();
        shadow.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 40, ShadowDepth = 8, Opacity = 0.55
        };
        shadow.IsHitTestVisible = false;

        var panel = shape();
        panel.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(0x88, 0xF7, 0xA8, 0xD3), Color.FromArgb(0x30, 0x8B, 0x5C, 0xF6), 90);
        panel.BorderThickness = new Thickness(1);
        panel.Padding = new Thickness(26, 0, 26, 20);
        panel.Child = stack;

        var holder = new Grid
        {
            Width = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        holder.Children.Add(shadow);
        holder.Children.Add(panel);
        return holder;
    }

    private static Button IconButton(string glyph, string color, string tip, Action? onClick)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = LiteTheme.B(color)
            },
            ToolTip = tip,
            Margin = new Thickness(3, 0, 3, 0),
            Style = LiteTheme.Glass("#2BFFFFFF", "#4DF27DBA", "#66F27DBA", "#FFFFFF")
        };
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- Menu ☰ ----------

    private void ShowMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
        menu.Resources[typeof(MenuItem)] = LiteTheme.Parse(LiteTheme.MenuItemXaml);
        menu.Style = LiteTheme.Parse(LiteTheme.ContextMenuXaml);

        MenuItem Item(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => act();
            return mi;
        }

        menu.Items.Add(Item("⚙  Configurações…", OpenSettings));
        menu.Items.Add(Item("📦  Pasta de mods", () => _main.LiteOpenFolder("mods")));
        menu.Items.Add(Item("📜  Log do launcher", _main.LiteOpenLog));
        menu.Items.Add(new Border { Height = 1, Margin = new Thickness(8, 3, 8, 3), Background = LiteTheme.B("#33FFFFFF") });
        menu.Items.Add(Item("🖥  Usar a interface completa", () => _main.LiteSetPerfMode("Full")));
        menu.IsOpen = true;
    }

    private void OpenSettings()
    {
        var action = LiteDialogs.Settings(this, _main);
        if (action != null) _main.LiteSetPerfMode(action); // pode fechar esta janela: nada vem depois
    }

    // ---------- Contas ----------

    internal void OpenAccounts() => LiteDialogs.Accounts(this, _main);

    internal void RefreshAccounts()
    {
        _updating = true;
        var items = _main.LiteAccounts.Select(a => new LiteItem($"{a.Name}   ·   {a.TypeLabel}", a)).ToList();
        _accountBox.ItemsSource = items;
        var active = _main.LiteActiveAccount;
        if (items.Count == 0)
        {
            // Sem conta: mostra um aviso no lugar (não é uma conta, então não faz nada ao ser escolhido).
            _accountBox.ItemsSource = new[] { new LiteItem("Nenhuma conta: clique no primeiro botão de baixo", null) };
            _accountBox.SelectedIndex = 0;
        }
        else _accountBox.SelectedItem = items.FirstOrDefault(i => ((Account)i.Value!).Key == active?.Key);
        _updating = false;
    }

    private void AccountBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_accountBox.SelectedItem as LiteItem)?.Value is Account a) _main.LiteSelectAccount(a);
    }

    // ---------- Modo e versões ----------

    private void LoaderBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_loaderBox.SelectedItem as LiteItem)?.Value is Loader l)
            _main.LiteSetLoader(l); // a MainWindow remonta a lista e chama RefreshVersions
    }

    private void VersionBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_versionBox.SelectedItem as LiteItem)?.Value is string v) _main.LiteSelectVersion(v);
    }

    /// <summary>A MainWindow avisa quando a lista de versões mudou (modo trocado, versões carregadas…).</summary>
    internal void RefreshVersions()
    {
        _updating = true;
        try
        {
            // Modo
            var cur = _main.LiteLoader;
            _loaderBox.SelectedItem = _loaderBox.Items.OfType<LiteItem>().FirstOrDefault(i => i.Value is Loader l && l == cur);

            // Versões
            var wanted = (_versionBox.SelectedItem as LiteItem)?.Value as string ?? _main.LiteSelectedVersion;
            IEnumerable<string> all = _main.LiteVersions;
            if (_onlyInstalled.IsChecked == true) all = all.Where(_main.LiteIsInstalled);

            var items = all.Select(v => new LiteItem(_main.LiteIsInstalled(v) ? v + "   ✔" : v, v)).ToList();
            _versionBox.ItemsSource = items;
            _versionBox.SelectedItem = items.FirstOrDefault(i => (string?)i.Value == wanted) ?? items.FirstOrDefault();
        }
        finally { _updating = false; }

        if ((_versionBox.SelectedItem as LiteItem)?.Value is string v2) _main.LiteSelectVersion(v2);
        UpdatePlayEnabled(_main.LiteBusy);
    }

    // ---------- Jogar ----------

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_main.LiteBusy) return;

        if (_main.LiteActiveAccount == null)
        {
            LiteDialogs.Message(this, "Escolha uma conta",
                "Para jogar você precisa de uma conta. Crie uma conta Moonlight (só o nome) ou entre com a conta Microsoft.",
                null, new[] { ("Ok", "ok") });
            OpenAccounts();
            if (_main.LiteActiveAccount == null) return;
        }

        if ((_versionBox.SelectedItem as LiteItem)?.Value is not string v) return;

        _main.LiteSelectVersion(v);
        _playBtn.IsEnabled = false;
        _playBtn.Content = "AGUARDE…";
        _main.LitePlay();
        Tick();
    }

    private void UpdatePlayEnabled(bool busy)
    {
        _playBtn.IsEnabled = !busy && _versionBox.Items.Count > 0;
        _playBtn.Content = busy ? "AGUARDE…" : "JOGAR";
    }

    // Lê o andamento que a MainWindow já mantém (mensagem e barra) e mostra aqui.
    private void Tick()
    {
        var busy = _main.LiteBusy;

        var text = _main.LiteStatus;
        if (text != _lastStatus)
        {
            _lastStatus = text;
            _status.Text = text;
        }

        if (busy)
        {
            _bar.Visibility = Visibility.Visible;
            var (indeterminate, max, value) = _main.LiteProgress;
            _bar.IsIndeterminate = indeterminate;
            if (!indeterminate)
            {
                _bar.Maximum = Math.Max(max, 1);
                _bar.Value = value;
            }
        }
        else if (_bar.Visibility != Visibility.Hidden)
        {
            _bar.IsIndeterminate = false;
            _bar.Visibility = Visibility.Hidden;
        }

        if (busy != _wasBusy)
        {
            _wasBusy = busy;
            UpdatePlayEnabled(busy);
            if (!busy) RefreshVersions(); // a versão que acabou de baixar já ganha o ✔
        }
    }

    // ---------- Jogo aberto / fechado ----------

    internal void OnGameStarted()
    {
        if (_hideOnPlay.IsChecked == true) Hide();
        else WindowState = WindowState.Minimized;
    }

    internal void Reappear()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Tick();
    }

    internal string ShowMessage(string title, string message, string? details, (string Label, string Key)[] buttons) =>
        LiteDialogs.Message(this, title, message, details, buttons);
}
