using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MoonlightLauncher;

// ====================================================================================
//  As duas janelinhas que aparecem ao clicar em "Compartilhar a tela":
//   1) Configurar compartilhamento: resolução, quadros por segundo e áudio
//      (1080p, 4K e 60 fps só com o Golden Moon; sem ele aparece o cadeado).
//   2) Escolha o que compartilhar: uma janela ou a tela inteira (com miniaturas).
// ====================================================================================

internal static class ShareUi
{
    private static ControlTemplate? _bare;

    /// <summary>Botão sem moldura nenhuma: só mostra o que a gente colocar dentro.</summary>
    public static ControlTemplate Bare => _bare ??= (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\"><ContentPresenter/></ControlTemplate>");

    /// <summary>O cartão arredondado (com sombra) que dá o visual das janelinhas.</summary>
    public static Border Card(double width)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(18), Margin = new Thickness(18), Padding = new Thickness(24, 22, 24, 22),
            BorderThickness = new Thickness(1), Width = width,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 3, Opacity = 0.65, Color = Colors.Black }
        };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return card;
    }

    public static TextBlock Title(string text, double size = 20)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        return t;
    }

    public static TextBlock Muted(string text, double size = 12.5)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    public static TextBlock Section(string text, double top)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    /// <summary>Botão grande (Compartilhar = cor de destaque; Cancelar = só contorno).</summary>
    public static Button ActionButton(string text, bool primary)
    {
        var tb = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        var bd = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(0, 12, 0, 12), Child = tb };
        if (primary)
        {
            bd.SetResourceReference(Border.BackgroundProperty, "AccB1_FF");
            bd.BorderBrush = Brushes.Transparent;
            tb.Foreground = Brushes.White;
        }
        else
        {
            bd.Background = Brushes.Transparent;
            bd.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        }
        var b = new Button { Template = Bare, Content = bd, Cursor = Cursors.Hand, Focusable = false };
        b.MouseEnter += (_, _) => { if (b.IsEnabled) bd.Opacity = 0.88; };
        b.MouseLeave += (_, _) => bd.Opacity = b.IsEnabled ? 1 : 0.4;
        b.IsEnabledChanged += (_, _) => bd.Opacity = b.IsEnabled ? 1 : 0.4;
        return b;
    }

    /// <summary>Arrasta a janelinha pelo cartão (menos quando o clique foi num botão, caixa ou na lista).</summary>
    public static void MakeDraggable(Window w, UIElement card)
    {
        card.MouseLeftButtonDown += (_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d != null; d = ParentOf(d))
                if (d is ButtonBase || d is CheckBox || d is ScrollViewer || d is ScrollBar) return;
            try { w.DragMove(); } catch { /* soltou rápido demais */ }
        };
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
}

/// <summary>Botãozinho de escolha (1080p, 60 fps…). Selecionado = cor de destaque; bloqueado = cadeado.</summary>
internal sealed class ChipButton : Button
{
    private readonly Border _bd = new()
    {
        CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        Padding = new Thickness(0, 12, 0, 12), Margin = new Thickness(4, 0, 4, 0)
    };
    private readonly TextBlock _tx = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _selected;

    public bool Locked { get; }

    public ChipButton(string text, bool locked)
    {
        Locked = locked;
        _tx.Text = locked ? "🔒 " + text : text;
        _bd.Child = _tx;
        Template = ShareUi.Bare;
        Content = _bd;
        Cursor = Cursors.Hand;
        Focusable = false;
        MouseEnter += (_, _) => { if (!_selected) _bd.SetResourceReference(Border.BorderBrushProperty, "AccB1_80"); };
        MouseLeave += (_, _) => Paint();
        Paint();
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Paint(); }
    }

    private void Paint()
    {
        if (_selected)
        {
            _bd.SetResourceReference(Border.BackgroundProperty, "AccB1_FF");
            _bd.SetResourceReference(Border.BorderBrushProperty, "AccB1_FF");
            _tx.Foreground = Brushes.White;
        }
        else
        {
            _bd.Background = Brushes.Transparent;
            _bd.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            _tx.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        }
        _bd.Opacity = Locked && !_selected ? 0.6 : 1;
    }
}

// ------------------------------------------------------------------------------------
//  1) Configurar compartilhamento
// ------------------------------------------------------------------------------------
internal sealed class ShareSetupWindow : Window
{
    public bool Accepted { get; private set; }
    public bool WantsGolden { get; private set; }
    public ShareOptions Result { get; private set; } = new();

    private int _height, _fps;
    private readonly List<(ChipButton Chip, int Value)> _resChips = new(), _fpsChips = new();
    private readonly CheckBox _audio;
    private readonly Border _hint;

    /// <param name="height">Última resolução usada (0 = ainda não escolheu).</param>
    /// <param name="fps">Último fps usado (0 = ainda não escolheu).</param>
    public ShareSetupWindow(bool premium, int height, int fps, bool audio)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Com o Golden Moon começa em 1080p/60; sem ele, 720p/30 (o máximo liberado).
        _height = height > 0 ? height : premium ? 1080 : 720;
        _fps = fps > 0 ? fps : premium ? 60 : 30;
        if (!premium) { _height = Math.Min(_height, 720); _fps = Math.Min(_fps, 30); }

        var resGrid = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 0) };
        var fpsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 0) };

        // Aviso do cadeado (só aparece quando alguém toca numa opção bloqueada).
        var hintText = new TextBlock { Text = "🌙 4K, 1080p e 60 fps são exclusivos do Golden Moon.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = CallUi.B("#FFC94A") };
        var hintBtn = ShareUi.ActionButton("Conhecer o Golden Moon", false);
        hintBtn.Margin = new Thickness(0, 10, 0, 0);
        hintBtn.Click += (_, _) => { WantsGolden = true; Close(); };
        var hintStack = new StackPanel();
        hintStack.Children.Add(hintText);
        hintStack.Children.Add(hintBtn);
        _hint = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 16, 0, 0), Background = CallUi.B("#1AFFC94A"), BorderBrush = CallUi.B("#66FFC94A"),
            Visibility = Visibility.Collapsed, Child = hintStack
        };

        void AddRes(string text, int h)
        {
            var locked = !premium && h > 720;
            var c = new ChipButton(text, locked) { Selected = h == _height };
            c.Click += (_, _) =>
            {
                if (locked) { _hint.Visibility = Visibility.Visible; return; }
                _height = h;
                foreach (var r in _resChips) r.Chip.Selected = r.Value == h;
                _hint.Visibility = Visibility.Collapsed;
            };
            _resChips.Add((c, h));
            resGrid.Children.Add(c);
        }

        void AddFps(int f)
        {
            var locked = !premium && f > 30;
            var c = new ChipButton(f + " fps", locked) { Selected = f == _fps };
            c.Click += (_, _) =>
            {
                if (locked) { _hint.Visibility = Visibility.Visible; return; }
                _fps = f;
                foreach (var r in _fpsChips) r.Chip.Selected = r.Value == f;
                _hint.Visibility = Visibility.Collapsed;
            };
            _fpsChips.Add((c, f));
            fpsGrid.Children.Add(c);
        }

        AddRes("4K", 2160); AddRes("1080p", 1080); AddRes("720p", 720); AddRes("480p", 480);
        AddFps(15); AddFps(30); AddFps(60);

        _audio = new CheckBox
        {
            Content = "Compartilhar áudio (o som do PC)", IsChecked = audio, FontSize = 13, Margin = new Thickness(0, 18, 0, 0),
            Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center
        };
        _audio.SetResourceReference(Control.ForegroundProperty, "Muted");

        var cancel = ShareUi.ActionButton("Cancelar", false);
        var go = ShareUi.ActionButton("Compartilhar", true);
        cancel.Margin = new Thickness(0, 0, 6, 0);
        go.Margin = new Thickness(6, 0, 0, 0);
        cancel.Click += (_, _) => Close();
        go.Click += (_, _) =>
        {
            Result = new ShareOptions { Height = _height, Fps = _fps, Audio = _audio.IsChecked == true }.Clamp(premium);
            Accepted = true;
            Close();
        };
        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        var root = new StackPanel();
        root.Children.Add(ShareUi.Title("Configurar compartilhamento"));
        root.Children.Add(ShareUi.Section("RESOLUÇÃO", 18));
        root.Children.Add(resGrid);
        root.Children.Add(ShareUi.Section("TAXA DE QUADROS", 18));
        root.Children.Add(fpsGrid);
        root.Children.Add(_hint);
        root.Children.Add(_audio);
        root.Children.Add(buttons);

        var card = ShareUi.Card(470);
        card.Child = root;
        Content = card;
        ShareUi.MakeDraggable(this, card);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}

// ------------------------------------------------------------------------------------
//  2) Escolha o que compartilhar (Janela / Tela inteira)
// ------------------------------------------------------------------------------------
internal sealed class ShareSourcePickerWindow : Window
{
    /// <summary>A janela ou tela escolhida (null = a pessoa cancelou).</summary>
    public ShareSource? Chosen { get; private set; }

    private readonly CancellationTokenSource _cts = new();
    private readonly WrapPanel _winPanel = new(), _scrPanel = new();
    private readonly ScrollViewer _scroll = new()
    {
        Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly Button _ok = ShareUi.ActionButton("Compartilhar", true);
    private readonly TextBlock _tabWin, _tabScr;
    private readonly Border _lineWin, _lineScr;
    private readonly TextBlock _tip;
    private readonly List<(ShareSource Src, Border Card, Image Img)> _cards = new();
    private ShareSource? _sel;

    public ShareSourcePickerWindow(string friendName)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = ShareUi.Title($"Escolha o que compartilhar com {friendName}", 18);
        var sub = ShareUi.Muted("O Moonlight terá acesso ao conteúdo da sua tela enquanto você compartilhar.");
        sub.Margin = new Thickness(0, 4, 0, 0);

        // abas
        _tabWin = MakeTab("Janela", out _lineWin);
        _tabScr = MakeTab("Tela inteira", out _lineScr);
        var tabs = new UniformGrid { Columns = 2, Margin = new Thickness(0, 16, 0, 0) };
        tabs.Children.Add(TabButton(_tabWin, _lineWin, true));
        tabs.Children.Add(TabButton(_tabScr, _lineScr, false));
        ShowTabPaint(true);

        // conteúdo
        var list = ShareCapture.ListWindows();
        foreach (var s in list) _winPanel.Children.Add(MakeCard(s));
        if (list.Count == 0)
            _winPanel.Children.Add(new TextBlock { Text = "Nenhuma janela aberta foi encontrada.", Margin = new Thickness(12), Foreground = CallUi.B("#9DA2BF") });
        foreach (var s in ShareCapture.ListScreens()) _scrPanel.Children.Add(MakeCard(s));

        var listHost = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 0), Child = _scroll, BorderThickness = new Thickness(1) };
        listHost.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        listHost.Background = CallUi.B("#22000000");
        _scroll.Content = _winPanel;

        _tip = ShareUi.Muted("Janelas minimizadas não aparecem: abra a janela antes. Se ela aparecer preta para o seu amigo, compartilhe a tela inteira.", 11.5);
        _tip.Margin = new Thickness(0, 10, 0, 0);

        // botões
        var cancel = ShareUi.ActionButton("Cancelar", false);
        cancel.Margin = new Thickness(0, 0, 6, 0);
        _ok.Margin = new Thickness(6, 0, 0, 0);
        _ok.IsEnabled = false;
        cancel.Click += (_, _) => Close();
        _ok.Click += (_, _) => Confirm();
        var buttons = new UniformGrid { Columns = 2, Width = 320, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var root = new StackPanel();
        root.Children.Add(title);
        root.Children.Add(sub);
        root.Children.Add(tabs);
        root.Children.Add(new Border { Height = 1, Background = CallUi.B("#22FFFFFF") });
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(listHost);
        root.Children.Add(_tip);
        root.Children.Add(buttons);

        var card = ShareUi.Card(720);
        card.Child = root;
        Content = card;
        ShareUi.MakeDraggable(this, card);

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => LoadThumbs();
        Closed += (_, _) => { try { _cts.Cancel(); } catch { /* */ } };
    }

    // ---- abas ----

    private static TextBlock MakeTab(string text, out Border line)
    {
        var t = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        line = new Border { Height = 2, Visibility = Visibility.Collapsed };
        line.SetResourceReference(Border.BackgroundProperty, "AccB1_FF");
        return t;
    }

    private Button TabButton(TextBlock label, Border line, bool windows)
    {
        var st = new StackPanel();
        st.Children.Add(label);
        st.Children.Add(line);
        var b = new Button { Template = ShareUi.Bare, Content = st, Cursor = Cursors.Hand, Focusable = false };
        b.Click += (_, _) => ShowTab(windows);
        return b;
    }

    private void ShowTab(bool windows)
    {
        _scroll.Content = windows ? _winPanel : _scrPanel;
        _scroll.ScrollToTop();
        _tip.Visibility = windows ? Visibility.Visible : Visibility.Collapsed;
        Select(null);
        ShowTabPaint(windows);
    }

    private void ShowTabPaint(bool windows)
    {
        _lineWin.Visibility = windows ? Visibility.Visible : Visibility.Collapsed;
        _lineScr.Visibility = windows ? Visibility.Collapsed : Visibility.Visible;
        Paint(_tabWin, windows);
        Paint(_tabScr, !windows);

        static void Paint(TextBlock t, bool on)
        {
            if (on) t.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
            else t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }
    }

    // ---- cartões ----

    private Border MakeCard(ShareSource s)
    {
        var img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var thumb = new Border
        {
            Width = 200, Height = 112, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Background = CallUi.B("#0B0C14"), Child = img
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 7, 0, 0) };
        if (s.Icon != null)
            row.Children.Add(new Image { Source = s.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        else
            row.Children.Add(new TextBlock { Text = s.IsScreen ? "🖥" : "🗔", FontSize = 14, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBlock { Text = s.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        row.Children.Add(name);

        var stack = new StackPanel { Width = 200 };
        stack.Children.Add(thumb);
        stack.Children.Add(row);

        var card = new Border
        {
            Padding = new Thickness(8), Margin = new Thickness(6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = stack, ToolTip = s.Title
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Select(s);
            if (e.ClickCount >= 2) Confirm(); // duplo clique = compartilhar já
        };
        _cards.Add((s, card, img));
        return card;
    }

    private void Select(ShareSource? s)
    {
        _sel = s;
        foreach (var c in _cards)
        {
            if (ReferenceEquals(c.Src, s))
            {
                c.Card.SetResourceReference(Border.BorderBrushProperty, "AccB1_FF");
                c.Card.Background = CallUi.B("#22FFFFFF");
            }
            else
            {
                c.Card.BorderBrush = Brushes.Transparent;
                c.Card.Background = Brushes.Transparent;
            }
        }
        _ok.IsEnabled = s != null;
    }

    private void Confirm()
    {
        if (_sel == null) return;
        Chosen = _sel;
        Close();
    }

    // ---- miniaturas (em segundo plano, para a janelinha abrir na hora) ----

    private void LoadThumbs()
    {
        var items = _cards.Select(c => (c.Src, c.Img)).ToList();
        var ct = _cts.Token;
        _ = Task.Run(() =>
        {
            foreach (var (src, img) in items)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    byte[]? px = null;
                    var r = ShareCapture.Grab(src, 400, 225, false, ref px, out var w, out var h);
                    if (r != GrabResult.Ok || px == null) continue;
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
                    bmp.Freeze();
                    Dispatcher.InvokeAsync(() => { if (!ct.IsCancellationRequested) img.Source = bmp; });
                }
                catch { /* sem miniatura, só o ícone */ }
            }
        }, ct);
    }
}
