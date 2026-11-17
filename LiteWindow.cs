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
/// com o fundo da noite chuvosa. Sem animações, sem estrelas, sem 3D, sem amigos na tela: quase não usa memória.
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
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x20));
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico")); }
        catch { /* sem ícone: não é crítico */ }

        var root = new Grid();
        var bg = LoadBackground();
        if (bg != null)
        {
            var img = new Image { Source = bg, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);
            root.Children.Add(img);
        }
        // Um véu escuro bem leve, só para o painel e o texto sempre ficarem legíveis.
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), IsHitTestVisible = false });
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
            bmp.DecodePixelWidth = 1280;            // não guarda mais pixels do que precisa
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

    private static Style ParseStyle(string xaml, params (string Key, string Value)[] map)
    {
        foreach (var (k, v) in map) xaml = xaml.Replace(k, v);
        return (Style)XamlReader.Parse(xaml);
    }

    // Botão chapado (sem o brilho azul padrão do Windows): fundo, fundo ao passar o mouse e ao apertar.
    private const string FlatButtonXaml = @"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Setter Property=""Background"" Value=""@BG""/>
  <Setter Property=""Foreground"" Value=""@FG""/>
  <Setter Property=""BorderBrush"" Value=""#40000000""/>
  <Setter Property=""BorderThickness"" Value=""1""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Button"">
        <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                BorderThickness=""{TemplateBinding BorderThickness}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""@HOVER""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""@PRESS""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""Bd"" Property=""Opacity"" Value=""0.55""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    internal static Style FlatButton(string bg, string hover, string press, string fg) =>
        ParseStyle(FlatButtonXaml, ("@BG", bg), ("@HOVER", hover), ("@PRESS", press), ("@FG", fg));

    private static Border Rule() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(0, 0, 0, 0)
    };

    private UIElement BuildPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(Rule());

        // Conta
        _accountBox.Height = 32;
        _accountBox.FontSize = 14;
        _accountBox.VerticalContentAlignment = VerticalAlignment.Center;
        _accountBox.Margin = new Thickness(0, 14, 0, 6);
        _accountBox.ToolTip = "Conta";
        _accountBox.SelectionChanged += AccountBox_Changed;
        stack.Children.Add(_accountBox);

        // Modo (Vanilla, Fabric…) e versão
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _loaderBox.Height = 32;
        _loaderBox.FontSize = 14;
        _loaderBox.VerticalContentAlignment = VerticalAlignment.Center;
        _loaderBox.ToolTip = "Modo (Vanilla, Fabric, Forge…)";
        _loaderBox.ItemsSource = Enum.GetValues<Loader>()
            .Select(l => new LiteItem(MainWindow.LiteLoaderTitle(l), l)).ToList();
        _loaderBox.SelectionChanged += LoaderBox_Changed;
        Grid.SetColumn(_loaderBox, 0);
        row.Children.Add(_loaderBox);

        _versionBox.Height = 32;
        _versionBox.FontSize = 14;
        _versionBox.VerticalContentAlignment = VerticalAlignment.Center;
        _versionBox.ToolTip = "Versão do Minecraft (✔ = já baixada)";
        _versionBox.SelectionChanged += VersionBox_Changed;
        Grid.SetColumn(_versionBox, 2);
        row.Children.Add(_versionBox);
        stack.Children.Add(row);

        // Opções
        _onlyInstalled.Content = "Só versões baixadas";
        _onlyInstalled.Foreground = Brushes.White;
        _onlyInstalled.FontSize = 13;
        _onlyInstalled.Margin = new Thickness(2, 2, 0, 3);
        _onlyInstalled.Click += (_, _) => RefreshVersions();
        stack.Children.Add(_onlyInstalled);

        _hideOnPlay.Content = "Esconder o launcher ao jogar";
        _hideOnPlay.Foreground = Brushes.White;
        _hideOnPlay.FontSize = 13;
        _hideOnPlay.Margin = new Thickness(2, 0, 0, 12);
        _hideOnPlay.IsChecked = true;
        stack.Children.Add(_hideOnPlay);

        stack.Children.Add(Rule());

        // Jogar
        _playBtn.Content = "Jogar";
        _playBtn.Height = 46;
        _playBtn.FontSize = 18;
        _playBtn.FontWeight = FontWeights.Bold;
        _playBtn.Margin = new Thickness(0, 14, 0, 6);
        _playBtn.Style = FlatButton("#FFFFFF", "#E8EEF8", "#CAD4E6", "#111111");
        _playBtn.Click += Play_Click;
        stack.Children.Add(_playBtn);

        // Quatro botões, como no Legacy Launcher
        var icons = new UniformGrid { Columns = 4, Height = 34, Margin = new Thickness(0, 0, 0, 0) };
        icons.Children.Add(IconButton("", "#2F7DE1", "Contas", () => OpenAccounts()));
        icons.Children.Add(IconButton("", "#F29A1D", "Abrir a pasta do jogo (.minecraft)", () => _main.LiteOpenFolder("")));
        icons.Children.Add(IconButton("", "#2FA84F", "Atualizar as versões baixadas", () =>
        {
            _main.LiteRescan();
            RefreshVersions();
        }));
        var menuBtn = IconButton("", "#111111", "Menu", null);
        menuBtn.Click += (_, _) => ShowMenu(menuBtn);
        icons.Children.Add(menuBtn);
        stack.Children.Add(icons);

        // Progresso e mensagem
        _bar.Height = 5;
        _bar.Margin = new Thickness(0, 10, 0, 0);
        _bar.Visibility = Visibility.Hidden;
        _bar.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF));
        _bar.Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        _bar.BorderThickness = new Thickness(0);
        stack.Children.Add(_bar);

        _status.Foreground = Brushes.White;
        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.MinHeight = 16;
        stack.Children.Add(_status);

        // Painel translúcido no meio (como o do Legacy Launcher)
        return new Border
        {
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 0, 0, 14),
            Background = new SolidColorBrush(Color.FromArgb(0x58, 0xFF, 0xFF, 0xFF)),
            Child = new Border { Padding = new Thickness(22, 0, 22, 0), Child = stack }
        };
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
                Foreground = (Brush)new BrushConverter().ConvertFromString(color)!
            },
            ToolTip = tip,
            Margin = new Thickness(0, 0, 4, 0),
            Style = FlatButton("#FFFFFF", "#E8EEF8", "#CAD4E6", "#111111")
        };
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- Menu ☰ ----------

    private void ShowMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };

        MenuItem Item(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => act();
            return mi;
        }

        menu.Items.Add(Item("⚙  Configurações…", OpenSettings));
        menu.Items.Add(Item("📦  Pasta de mods", () => _main.LiteOpenFolder("mods")));
        menu.Items.Add(Item("📜  Log do launcher", _main.LiteOpenLog));
        menu.Items.Add(new Separator());
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
            _accountBox.ItemsSource = new[] { new LiteItem("Nenhuma conta: clique no botão azul de baixo", null) };
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
        _playBtn.Content = "Aguarde…";
        _main.LitePlay();
        Tick();
    }

    private void UpdatePlayEnabled(bool busy)
    {
        _playBtn.IsEnabled = !busy && _versionBox.Items.Count > 0;
        _playBtn.Content = busy ? "Aguarde…" : "Jogar";
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
