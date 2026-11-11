using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MoonlightLauncher;

/// <summary>
/// Criador de skin: textura 64×64 em 2D (com guias de cada parte do corpo) e o boneco em 3D atualizando junto.
/// Ferramentas: lápis, borracha, balde, conta-gotas, linha, retângulos, círculos, clarear, escurecer, ruído,
/// espelhar braço/perna, camadas (base / externa), ajustes de cor da skin inteira, atalhos de teclado,
/// desfazer/refazer e rascunho automático (a skin não se perde se você fechar o launcher).
/// </summary>
public sealed class SkinEditor : UserControl
{
    private enum Tool { Pencil, Eraser, Fill, Picker, Line, Rect, RectFill, Circle, CircleFill, Lighten, Darken, Noise }

    // Partes da textura do Minecraft (x, y, largura, altura, é camada externa?)
    private sealed record Part(string Name, int X, int Y, int W, int H, bool Overlay);

    private static readonly Part[] Parts =
    {
        new("Cabeça", 0, 0, 32, 16, false),        new("Chapéu", 32, 0, 32, 16, true),
        new("Perna D", 0, 16, 16, 16, false),      new("Corpo", 16, 16, 24, 16, false),
        new("Braço D", 40, 16, 16, 16, false),     new("Calça D", 0, 32, 16, 16, true),
        new("Casaco", 16, 32, 24, 16, true),       new("Manga D", 40, 32, 16, 16, true),
        new("Perna E", 16, 48, 16, 16, false),     new("Braço E", 32, 48, 16, 16, false),
        new("Calça E", 0, 48, 16, 16, true),       new("Manga E", 48, 48, 16, 16, true),
    };

    private const int Scale = 7;
    private const int Side = 64 * Scale;

    public event Action<byte[], bool, string>? SaveRequested;
    public event Func<byte[], bool, string>? UseRequested;
    public Func<(byte[]? Png, bool Slim)>? CurrentSkinProvider;

    private readonly byte[] _px = new byte[64 * 64 * 4]; // BGRA
    private readonly WriteableBitmap _bmp = new(64, 64, 96, 96, PixelFormats.Bgra32, null);
    private readonly SkinViewer3D _view3d = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();
    private readonly List<Color> _recent = new();
    private readonly Random _rnd = new();

    private Grid _stage = null!;
    private Canvas _guides = null!;
    private Border _gridLines = null!;
    private Border _cursor = null!;
    private Border _colorPrev = null!;
    private TextBox _hexBox = null!;
    private Slider _sr = null!, _sg = null!, _sb = null!;
    private WrapPanel _recentPanel = null!;
    private TextBlock _info = null!;
    private TextBlock _status = null!;
    private TextBox _nameBox = null!;

    private bool _built, _busy, _dirty3d = true, _slim, _paused;
    private Tool _tool = Tool.Pencil;
    private int _size = 1;
    private int _layerMode; // 0 tudo, 1 só base, 2 só camada externa
    private Color _color = Color.FromRgb(0xFF, 0xD5, 0x4F);

    private bool _drag;
    private int _sx, _sy, _lx, _ly;

    // Pintura em 3D por parte do corpo
    private PartPainter3D? _pp;
    private Border _stageCard = null!;
    private StackPanel _ppPanel = null!;
    private RadioButton[] _layerPills = new RadioButton[3];
    private RadioButton[] _ppLayerPills = new RadioButton[2];
    private bool _mode3d, _syncingLayer;
    private (int X0, int Y0, int X1, int Y1)? _clip; // durante o traço 3D: só pinta dentro da face onde ele começou
    private const string HintText = "Botão esquerdo pinta · botão direito pega a cor · teclas: P E B I L R F O K C D N · Ctrl+Z desfaz.";
    private const string Hint3D = "3D: botão esquerdo pinta · botão direito ARRASTADO gira a peça (clique rápido = conta-gotas) · rodinha = zoom.";

    // No 3D, as formas (linha, retângulo, círculo) viram lápis: elas só fazem sentido no desenho plano.
    private Tool EffTool => (_mode3d && (_tool is Tool.Line or Tool.Rect or Tool.RectFill or Tool.Circle or Tool.CircleFill)) ? Tool.Pencil : _tool;
    private byte[]? _snap;

    // Atalhos de teclado e botões de ferramenta / tamanho
    private readonly Dictionary<Tool, RadioButton> _toolButtons = new();
    private readonly Dictionary<Key, Tool> _toolKeys = new();
    private readonly Dictionary<int, RadioButton> _sizeButtons = new();

    // Rascunho automático
    private bool _draftDirty;
    private static readonly string DraftDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher");
    private static readonly string DraftPng = System.IO.Path.Combine(DraftDir, "skin-draft.png");
    private static readonly string DraftSlim = System.IO.Path.Combine(DraftDir, "skin-draft.slim");

    public SkinEditor()
    {
        Array.Copy(SkinTools.PlaceholderPixels(), _px, _px.Length);
        Loaded += (_, _) =>
        {
            if (!_built) Build();
            UpdateTimer();
        };
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
        _timer.Tick += (_, _) =>
        {
            if (_dirty3d)
            {
                _dirty3d = false;
                try { _view3d.SetSkin(EncodePng(), _slim); }
                catch (Exception ex) { LauncherLog.Write("Editor de skin: falha na prévia 3D", ex); }
            }
            if (_draftDirty && !_drag) SaveDraft();
        };
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _view3d.Paused = value;
            if (_pp != null) _pp.Paused = value;
            UpdateTimer();
        }
    }

    private void UpdateTimer()
    {
        if (_built && IsVisible && !_paused) _timer.Start(); else _timer.Stop();
    }

    // ---------- Montagem da tela ----------

    private Button Btn(string text, string tip, Action act, bool primary = false)
    {
        var b = new Button { Content = text, ToolTip = tip };
        b.Style = (Style)FindResource(primary ? "PlayButton" : "GhostButton");
        b.FontSize = 12.5;
        b.Padding = new Thickness(11, 7, 11, 7);
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Click += (_, _) => act();
        return b;
    }

    private TextBlock Label(string text, double top = 10)
    {
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, top, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private RadioButton Pill(string text, string group, object tag, bool on, Action<object> chosen)
    {
        var rb = new RadioButton { Content = text, GroupName = group, Tag = tag, IsChecked = on };
        rb.Style = (Style)FindResource("PillButton");
        rb.FontSize = 12;
        rb.Margin = new Thickness(0, 0, 6, 6);
        rb.Checked += (_, _) => chosen(tag);
        return rb;
    }

    private void Build()
    {
        _built = true;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ===== Coluna 1: ferramentas =====
        var toolsPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };

        void AddTool(WrapPanel into, string text, Tool t, Key key, string tip)
        {
            var rb = Pill(text, "skintool", t, t == Tool.Pencil, o => _tool = (Tool)o);
            rb.ToolTip = $"{tip}  (atalho: {key})";
            _toolButtons[t] = rb;
            _toolKeys[key] = t;
            into.Children.Add(rb);
        }

        toolsPanel.Children.Add(Label("PINTAR", 0));
        var paintWrap = new WrapPanel();
        AddTool(paintWrap, "✏ Lápis", Tool.Pencil, Key.P, "Pinta pixel por pixel");
        AddTool(paintWrap, "🧽 Borracha", Tool.Eraser, Key.E, "Apaga (na camada externa fica transparente)");
        AddTool(paintWrap, "🪣 Balde", Tool.Fill, Key.B, "Preenche a área da mesma cor");
        AddTool(paintWrap, "💧 Cores", Tool.Picker, Key.I, "Pega a cor de um pixel (ou clique com o botão direito)");
        toolsPanel.Children.Add(paintWrap);

        toolsPanel.Children.Add(Label("FORMAS"));
        var shapeWrap = new WrapPanel();
        AddTool(shapeWrap, "╱ Linha", Tool.Line, Key.L, "Arraste para traçar uma linha");
        AddTool(shapeWrap, "▭ Retângulo", Tool.Rect, Key.R, "Contorno de retângulo");
        AddTool(shapeWrap, "■ Cheio", Tool.RectFill, Key.F, "Retângulo preenchido");
        AddTool(shapeWrap, "◯ Círculo", Tool.Circle, Key.O, "Contorno de círculo / elipse");
        AddTool(shapeWrap, "⬤ Redondo", Tool.CircleFill, Key.K, "Círculo / elipse preenchido");
        toolsPanel.Children.Add(shapeWrap);

        toolsPanel.Children.Add(Label("EFEITOS"));
        var fxWrap = new WrapPanel();
        AddTool(fxWrap, "☀ Clarear", Tool.Lighten, Key.C, "Deixa os pixels mais claros");
        AddTool(fxWrap, "🌑 Escurecer", Tool.Darken, Key.D, "Deixa os pixels mais escuros");
        AddTool(fxWrap, "🎲 Ruído", Tool.Noise, Key.N, "Variação leve de cor (dá textura)");
        toolsPanel.Children.Add(fxWrap);

        toolsPanel.Children.Add(Label("TAMANHO DO PINCEL  ( [ e ] )"));
        var sizeWrap = new WrapPanel();
        for (var s = 1; s <= 4; s++)
        {
            var rb = Pill($"{s}px", "skinsize", s, s == 1, o => _size = (int)o);
            _sizeButtons[s] = rb;
            sizeWrap.Children.Add(rb);
        }
        toolsPanel.Children.Add(sizeWrap);

        toolsPanel.Children.Add(Label("CAMADAS"));
        var layerWrap = new WrapPanel();
        _layerPills[0] = Pill("Tudo", "skinlayer", 0, true, o => { _layerMode = (int)o; Refresh(); SyncPpLayer(); });
        _layerPills[1] = Pill("Só base", "skinlayer", 1, false, o => { _layerMode = (int)o; Refresh(); SyncPpLayer(); });
        _layerPills[2] = Pill("Só externa", "skinlayer", 2, false, o => { _layerMode = (int)o; Refresh(); SyncPpLayer(); });
        foreach (var lp in _layerPills) layerWrap.Children.Add(lp);
        toolsPanel.Children.Add(layerWrap);

        toolsPanel.Children.Add(Label("VISUAL"));
        var guidesCheck = new CheckBox { Content = "Guias das partes", IsChecked = true, Margin = new Thickness(2, 0, 0, 6) };
        guidesCheck.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        guidesCheck.Click += (_, _) => _guides.Visibility = guidesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var gridCheck = new CheckBox { Content = "Grade de pixels", IsChecked = true, Margin = new Thickness(2, 0, 0, 6) };
        gridCheck.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        gridCheck.Click += (_, _) => _gridLines.Visibility = gridCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var slimCheck = new CheckBox { Content = "Braços finos (Alex)", Margin = new Thickness(2, 0, 0, 6) };
        slimCheck.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        slimCheck.Click += (_, _) => { _slim = slimCheck.IsChecked == true; _dirty3d = true; _draftDirty = true; if (_pp != null) _pp.Slim = _slim; };
        toolsPanel.Children.Add(guidesCheck);
        toolsPanel.Children.Add(gridCheck);
        toolsPanel.Children.Add(slimCheck);

        var toolsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = toolsPanel };
        root.Children.Add(toolsScroll);

        // ===== Coluna 2: textura em 2D =====
        var checker = BuildChecker();
        var stageCard = new Border
        {
            Width = Side + 2,
            Height = Side + 2,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Background = checker
        };
        stageCard.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        _stage = new Grid { Width = Side, Height = Side, Background = Brushes.Transparent, Cursor = Cursors.Cross };
        var img = new Image { Source = _bmp, Width = Side, Height = Side, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        _stage.Children.Add(img);

        _gridLines = new Border { IsHitTestVisible = false, Background = BuildGridBrush() };
        _stage.Children.Add(_gridLines);

        _guides = new Canvas { IsHitTestVisible = false };
        foreach (var p in Parts)
        {
            var box = new Border
            {
                Width = p.W * Scale,
                Height = p.H * Scale,
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(p.Overlay ? Color.FromArgb(200, 255, 170, 60) : Color.FromArgb(200, 90, 200, 255)),
                Child = new TextBlock
                {
                    Text = p.Name,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(3, 1, 0, 0),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 }
                }
            };
            Canvas.SetLeft(box, p.X * Scale);
            Canvas.SetTop(box, p.Y * Scale);
            _guides.Children.Add(box);
        }
        _stage.Children.Add(_guides);

        var overlay = new Canvas { IsHitTestVisible = false };
        _cursor = new Border
        {
            Width = Scale,
            Height = Scale,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 }
        };
        overlay.Children.Add(_cursor);
        _stage.Children.Add(overlay);

        _stage.MouseLeftButtonDown += (_, e) => { _stage.CaptureMouse(); BeginStroke(e.GetPosition(_stage)); };
        _stage.MouseRightButtonDown += (_, e) => { var (x, y) = ToPixel(e.GetPosition(_stage), false); if (x >= 0) Pick(x, y); };
        _stage.MouseMove += (_, e) => OnMove(e.GetPosition(_stage));
        _stage.MouseLeftButtonUp += (_, _) => { _stage.ReleaseMouseCapture(); EndStroke(); };
        _stage.MouseLeave += (_, _) => { if (!_drag) _cursor.Visibility = Visibility.Collapsed; };

        stageCard.Child = _stage;
        _stageCard = stageCard;

        var mid = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        mid.Children.Add(BuildModeSwitch());
        mid.Children.Add(stageCard);
        mid.Children.Add(BuildPartPanel());
        _info = new TextBlock { FontSize = 11.5, Margin = new Thickness(2, 8, 0, 0), Text = HintText };
        _info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        mid.Children.Add(_info);
        Grid.SetColumn(mid, 1);
        root.Children.Add(mid);

        // ===== Coluna 3: 3D, cor e ações =====
        var right = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        var viewCard = new Border
        {
            Height = 250,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = _view3d
        };
        viewCard.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        viewCard.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        right.Children.Add(viewCard);

        right.Children.Add(Label("COR"));
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        _colorPrev = new Border
        {
            Width = 40,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0)
        };
        _colorPrev.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        colorRow.Children.Add(_colorPrev);
        _hexBox = new TextBox { Width = 100, MaxLength = 9, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        _hexBox.Style = (Style)FindResource("MoonTextBox");
        _hexBox.Padding = new Thickness(8, 6, 8, 6);
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        _hexBox.LostFocus += (_, _) => CommitHex();
        colorRow.Children.Add(_hexBox);
        right.Children.Add(colorRow);

        var sl = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        sl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        sl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) sl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Slider MakeSlider(string name, int row)
        {
            var l = new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Grid.SetRow(l, row);
            sl.Children.Add(l);
            var s = new Slider { Minimum = 0, Maximum = 255 };
            s.Style = (Style)FindResource("MoonSlider");
            Grid.SetRow(s, row);
            Grid.SetColumn(s, 1);
            s.ValueChanged += (_, _) =>
            {
                if (_busy) return;
                SetColor(Color.FromRgb((byte)Math.Round(_sr.Value), (byte)Math.Round(_sg.Value), (byte)Math.Round(_sb.Value)));
            };
            sl.Children.Add(s);
            return s;
        }
        _sr = MakeSlider("R", 0);
        _sg = MakeSlider("G", 1);
        _sb = MakeSlider("B", 2);
        right.Children.Add(sl);

        var palette = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var hex in new[]
                 {
                     "#FFDBB4", "#F1C27D", "#E0AC69", "#C68642", "#8D5524", "#5C3A21", "#2B1B10", "#4A2F1B",
                     "#B9792F", "#E5C27A", "#F0E6D2", "#1A1A1A", "#616161", "#BDBDBD", "#FFFFFF", "#E53935",
                     "#FB8C00", "#FDD835", "#43A047", "#00ACC1", "#1E88E5", "#5E35B1", "#D81B60", "#FFD54F"
                 })
            palette.Children.Add(Swatch(ThemeManager.ParseColor(hex, Colors.White)));
        right.Children.Add(palette);

        right.Children.Add(Label("USADAS AGORA", 6));
        _recentPanel = new WrapPanel { MinHeight = 28 };
        right.Children.Add(_recentPanel);

        right.Children.Add(Label("ESPELHAR (copia o lado direito no esquerdo e vice-versa)"));
        var mir = new WrapPanel();
        mir.Children.Add(Btn("Braço D → E", "Copia o braço direito (com a manga) para o esquerdo", () => Mirror(true, true)));
        mir.Children.Add(Btn("Perna D → E", "Copia a perna direita (com a calça) para a esquerda", () => Mirror(false, true)));
        mir.Children.Add(Btn("Braço E → D", "Copia o braço esquerdo para o direito", () => Mirror(true, false)));
        mir.Children.Add(Btn("Perna E → D", "Copia a perna esquerda para a direita", () => Mirror(false, false)));
        right.Children.Add(mir);

        right.Children.Add(Label("EDIÇÃO"));
        var edit = new WrapPanel();
        edit.Children.Add(Btn("↶ Desfazer", "Ctrl+Z", Undo));
        edit.Children.Add(Btn("↷ Refazer", "Ctrl+Y", Redo));
        edit.Children.Add(Btn("🧹 Limpar camada externa", "Apaga chapéu, casaco, mangas e calças", ClearOverlay));
        right.Children.Add(edit);

        right.Children.Add(Label("AJUSTAR A SKIN (camada escolhida)"));
        var adjust = new WrapPanel();
        adjust.Children.Add(Btn("☀ Mais claro", "Clareia todos os pixels pintados", () => AdjustAll("Mais claro", c => Brighter(c))));
        adjust.Children.Add(Btn("🌑 Mais escuro", "Escurece todos os pixels pintados", () => AdjustAll("Mais escuro", c => Darker(c))));
        adjust.Children.Add(Btn("🌈 Matiz +30°", "Gira as cores para a frente no círculo de cores", () => AdjustAll("Matiz +30°", c => ShiftHue(c, 30))));
        adjust.Children.Add(Btn("🌈 Matiz −30°", "Gira as cores para trás no círculo de cores", () => AdjustAll("Matiz −30°", c => ShiftHue(c, -30))));
        adjust.Children.Add(Btn("🔄 Inverter", "Inverte todas as cores", () => AdjustAll("Cores invertidas", c => Color.FromRgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)))));
        adjust.Children.Add(Btn("⚫ Cinza", "Tira a cor (preto e branco)", () => AdjustAll("Preto e branco", c =>
        {
            var g = (byte)Math.Round(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
            return Color.FromRgb(g, g, g);
        })));
        right.Children.Add(adjust);

        right.Children.Add(Label("COMEÇAR DE"));
        var start = new WrapPanel();
        start.Children.Add(Btn("🌙 Modelo lunar", "Boneco base com rosto", () => ResetTo(SkinTools.PlaceholderPixels(), "Modelo lunar carregado.")));
        start.Children.Add(Btn("⬜ Em branco", "Corpo cinza claro, sem camada externa", Blank));
        start.Children.Add(Btn("👤 Skin da conta", "Carrega a skin da conta ativa", ImportFromAccount));
        start.Children.Add(Btn("📂 Abrir .png", "Abre um arquivo de skin", OpenFile));
        right.Children.Add(start);

        right.Children.Add(Label("SALVAR"));
        _nameBox = new TextBox { Text = "minha-skin", MaxLength = 24, FontSize = 13 };
        _nameBox.Style = (Style)FindResource("MoonTextBox");
        _nameBox.Padding = new Thickness(10, 7, 10, 7);
        right.Children.Add(_nameBox);
        var save = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        save.Children.Add(Btn("💾 Guardar na biblioteca", "Aparece em “Minha biblioteca” (aba Minhas skins)", SaveLib, true));
        save.Children.Add(Btn("✔ Usar na conta", "Aplica à conta ativa", UseOnAccount));
        save.Children.Add(Btn("⬇ Exportar .png", "Salva um arquivo .png", Export));
        right.Children.Add(save);

        _status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 12) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        right.Children.Add(_status);

        var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = right };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Content = root;

        // Atalhos: Ctrl+Z / Ctrl+Y, uma letra para cada ferramenta e [ ] para o tamanho do pincel
        PreviewKeyDown += (_, e) =>
        {
            if (_hexBox.IsKeyboardFocused || _nameBox.IsKeyboardFocused) return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z) { Undo(); e.Handled = true; }
                else if (e.Key == Key.Y) { Redo(); e.Handled = true; }
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            if (_toolKeys.TryGetValue(e.Key, out var t) && _toolButtons.TryGetValue(t, out var tb))
            {
                tb.IsChecked = true;
                e.Handled = true;
            }
            else if (e.Key == Key.OemOpenBrackets && _sizeButtons.TryGetValue(_size - 1, out var smaller))
            {
                smaller.IsChecked = true;
                e.Handled = true;
            }
            else if (e.Key == Key.OemCloseBrackets && _sizeButtons.TryGetValue(_size + 1, out var bigger))
            {
                bigger.IsChecked = true;
                e.Handled = true;
            }
        };
        Focusable = true;
        _stage.MouseDown += (_, _) => Focus();

        SetColor(_color);
        Refresh();

        // Começa com a skin da conta, se houver.
        try
        {
            var cur = CurrentSkinProvider?.Invoke();
            if (cur?.Png != null) LoadSkin(cur.Value.Png, cur.Value.Slim);
        }
        catch (Exception ex) { LauncherLog.Write("Editor de skin: não consegui abrir a skin da conta", ex); }
        TryRestoreDraft();
        slimCheck.IsChecked = _slim;
        _timer.Start();
    }

    private static Brush BuildChecker()
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), null,
            new RectangleGeometry(new Rect(0, 0, Scale * 2, Scale * 2))));
        var dark = new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6));
        dg.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, Scale, Scale))));
        dg.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(Scale, Scale, Scale, Scale))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, Scale * 2, Scale * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, Scale * 2, Scale * 2),
            ViewboxUnits = BrushMappingMode.Absolute
        };
    }

    private static Brush BuildGridBrush()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 90, 90, 90)), 0.7);
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(Scale, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, Scale)));
        return new DrawingBrush(new GeometryDrawing(null, pen, lines))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, Scale, Scale),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, Scale, Scale),
            ViewboxUnits = BrushMappingMode.Absolute
        };
    }

    private Border Swatch(Color c)
    {
        var chip = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 5, 5),
            Background = new SolidColorBrush(c),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = ThemeManager.HexOf(c)
        };
        chip.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        chip.MouseLeftButtonUp += (_, _) => SetColor(c);
        return chip;
    }

    // ---------- Cor ----------

    private void SetColor(Color c)
    {
        _color = c;
        _busy = true;
        try
        {
            _colorPrev.Background = new SolidColorBrush(c);
            _hexBox.Text = ThemeManager.HexOf(c);
            _sr.Value = c.R;
            _sg.Value = c.G;
            _sb.Value = c.B;
        }
        finally { _busy = false; }
    }

    private void CommitHex()
    {
        if (_busy) return;
        var parsed = ThemeManager.ParseColor(_hexBox.Text, _color);
        SetColor(parsed);
    }

    private void RememberColor()
    {
        if (_recent.Contains(_color)) _recent.Remove(_color);
        _recent.Insert(0, _color);
        while (_recent.Count > 10) _recent.RemoveAt(_recent.Count - 1);

        _recentPanel.Children.Clear();
        foreach (var c in _recent) _recentPanel.Children.Add(Swatch(c));
    }

    // ---------- Regiões da textura ----------

    // 0 = área que o jogo não usa, 1 = camada de base, 2 = camada externa
    private static int RegionAt(int x, int y)
    {
        foreach (var p in Parts)
            if (x >= p.X && x < p.X + p.W && y >= p.Y && y < p.Y + p.H) return p.Overlay ? 2 : 1;
        return 0;
    }

    private static Part? PartAt(int x, int y)
    {
        foreach (var p in Parts)
            if (x >= p.X && x < p.X + p.W && y >= p.Y && y < p.Y + p.H) return p;
        return null;
    }

    private bool Allowed(int x, int y)
    {
        if (x < 0 || y < 0 || x > 63 || y > 63) return false;
        if (_clip is { } c && (x < c.X0 || x >= c.X1 || y < c.Y0 || y >= c.Y1)) return false;
        var r = RegionAt(x, y);
        if (r == 0) return false;
        if (_layerMode == 1 && r == 2) return false;
        if (_layerMode == 2 && r == 1) return false;
        return true;
    }

    // ---------- Mouse ----------

    private static (int X, int Y) ToPixel(Point p, bool clamp)
    {
        var x = (int)Math.Floor(p.X / Scale);
        var y = (int)Math.Floor(p.Y / Scale);
        if (clamp) return (Math.Clamp(x, 0, 63), Math.Clamp(y, 0, 63));
        return x < 0 || y < 0 || x > 63 || y > 63 ? (-1, -1) : (x, y);
    }

    private void OnMove(Point pos)
    {
        var (x, y) = ToPixel(pos, _drag);
        if (x < 0) { _cursor.Visibility = Visibility.Collapsed; return; }

        var n = _tool is Tool.Fill or Tool.Picker ? 1 : _size;
        _cursor.Width = n * Scale;
        _cursor.Height = n * Scale;
        Canvas.SetLeft(_cursor, (x - n / 2) * Scale);
        Canvas.SetTop(_cursor, (y - n / 2) * Scale);
        _cursor.Visibility = Visibility.Visible;

        var part = PartAt(x, y);
        var i = (y * 64 + x) * 4;
        _info.Text = $"x {x}, y {y}  ·  {(part == null ? "área não usada pelo jogo" : part.Name)}  ·  " +
                     $"#{_px[i + 2]:X2}{_px[i + 1]:X2}{_px[i]:X2}";

        if (_drag) ContinueStroke(x, y);
    }

    private void BeginStroke(Point pos)
    {
        var (x, y) = ToPixel(pos, false);
        if (x < 0) return;

        if (_tool == Tool.Picker) { Pick(x, y); return; }

        PushUndo();
        _drag = true;
        _sx = _lx = x;
        _sy = _ly = y;

        if (_tool is Tool.Line or Tool.Rect or Tool.RectFill or Tool.Circle or Tool.CircleFill)
        {
            _snap = (byte[])_px.Clone();
            DrawShape(x, y, x, y);
        }
        else if (_tool == Tool.Fill)
        {
            FloodFill(x, y);
            _drag = false;
            RememberColor();
        }
        else
        {
            Stamp(x, y);
        }
        Changed();
    }

    private void ContinueStroke(int x, int y)
    {
        if (x == _lx && y == _ly) return;

        if (_snap != null)
        {
            Array.Copy(_snap, _px, _px.Length);
            DrawShape(_sx, _sy, x, y);
        }
        else
        {
            Bresenham(_lx, _ly, x, y, Stamp);
        }
        _lx = x;
        _ly = y;
        Changed();
    }

    private void EndStroke()
    {
        if (!_drag) return;
        _drag = false;
        _snap = null;
        if (_tool is not (Tool.Eraser or Tool.Lighten or Tool.Darken or Tool.Noise)) RememberColor();
        Changed();
    }

    private void Pick(int x, int y)
    {
        var i = (y * 64 + x) * 4;
        if (_px[i + 3] == 0) { _info.Text = "Esse pixel é transparente."; return; }
        SetColor(Color.FromRgb(_px[i + 2], _px[i + 1], _px[i]));
    }

    // ---------- Pintura ----------

    private void Stamp(int cx, int cy)
    {
        var n = _size;
        var x0 = cx - n / 2;
        var y0 = cy - n / 2;
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
                PaintPixel(x0 + i, y0 + j);
    }

    private void PaintPixel(int x, int y)
    {
        if (!Allowed(x, y)) return;
        var i = (y * 64 + x) * 4;

        switch (EffTool)
        {
            case Tool.Eraser:
                if (RegionAt(x, y) == 2)
                {
                    _px[i] = _px[i + 1] = _px[i + 2] = 0;
                    _px[i + 3] = 0;
                }
                else // a camada de base nunca pode ser transparente no jogo
                {
                    _px[i] = _px[i + 1] = _px[i + 2] = 0xCF;
                    _px[i + 3] = 255;
                }
                break;
            case Tool.Lighten:
            case Tool.Darken:
                if (_px[i + 3] == 0) break;
                for (var c = 0; c < 3; c++)
                {
                    var v = _px[i + c];
                    v = _tool == Tool.Lighten ? (byte)Math.Min(255, v + 18 + (255 - v) / 10) : (byte)Math.Max(0, v - 18 - v / 10);
                    _px[i + c] = v;
                }
                break;
            case Tool.Noise:
                if (_px[i + 3] == 0) break;
                var d = _rnd.Next(-16, 17);
                for (var c = 0; c < 3; c++) _px[i + c] = (byte)Math.Clamp(_px[i + c] + d, 0, 255);
                break;
            default:
                SetPx(x, y, _color);
                break;
        }
    }

    private void SetPx(int x, int y, Color c)
    {
        if (!Allowed(x, y)) return;
        var i = (y * 64 + x) * 4;
        _px[i] = c.B;
        _px[i + 1] = c.G;
        _px[i + 2] = c.R;
        _px[i + 3] = 255;
    }

    private void DrawShape(int x0, int y0, int x1, int y1)
    {
        void Dot(int x, int y)
        {
            var n = _size;
            for (var j = 0; j < n; j++)
                for (var i = 0; i < n; i++)
                    SetPx(x - n / 2 + i, y - n / 2 + j, _color);
        }

        if (_tool == Tool.Line)
        {
            Bresenham(x0, y0, x1, y1, Dot);
            return;
        }

        var ax = Math.Min(x0, x1);
        var bx = Math.Max(x0, x1);
        var ay = Math.Min(y0, y1);
        var by = Math.Max(y0, y1);

        if (_tool == Tool.RectFill)
        {
            for (var y = ay; y <= by; y++)
                for (var x = ax; x <= bx; x++) SetPx(x, y, _color);
        }
        else if (_tool is Tool.Circle or Tool.CircleFill)
        {
            // Elipse que cabe dentro do retângulo arrastado.
            var cx = (ax + bx) / 2.0;
            var cy = (ay + by) / 2.0;
            var rx = (bx - ax) / 2.0 + 0.5;
            var ry = (by - ay) / 2.0 + 0.5;

            bool Inside(int x, int y)
            {
                var nx = (x - cx) / rx;
                var ny = (y - cy) / ry;
                return nx * nx + ny * ny <= 1.0;
            }

            for (var y = ay; y <= by; y++)
                for (var x = ax; x <= bx; x++)
                {
                    if (!Inside(x, y)) continue;
                    var edge = !Inside(x + 1, y) || !Inside(x - 1, y) || !Inside(x, y + 1) || !Inside(x, y - 1);
                    if (_tool == Tool.CircleFill) SetPx(x, y, _color);
                    else if (edge) Dot(x, y);
                }
        }
        else
        {
            Bresenham(ax, ay, bx, ay, Dot);
            Bresenham(ax, by, bx, by, Dot);
            Bresenham(ax, ay, ax, by, Dot);
            Bresenham(bx, ay, bx, by, Dot);
        }
    }

    private static void Bresenham(int x0, int y0, int x1, int y1, Action<int, int> plot)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // Balde: preenche a área da mesma cor, sem sair da parte do corpo onde você clicou.
    private void FloodFill(int x, int y)
    {
        if (!Allowed(x, y)) return;
        var part = PartAt(x, y);
        if (part == null) return;

        // Por padrão não sai da parte do corpo; no 3D não sai da face clicada.
        int bx0 = part.X, by0 = part.Y, bx1 = part.X + part.W, by1 = part.Y + part.H;
        if (_clip is { } cl) { bx0 = cl.X0; by0 = cl.Y0; bx1 = cl.X1; by1 = cl.Y1; }

        var ti = (y * 64 + x) * 4;
        var tb = _px[ti]; var tg = _px[ti + 1]; var tr = _px[ti + 2]; var ta = _px[ti + 3];
        if (ta == 255 && tb == _color.B && tg == _color.G && tr == _color.R) return;

        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            if (px < bx0 || px >= bx1 || py < by0 || py >= by1) continue;
            var i = (py * 64 + px) * 4;
            if (_px[i] != tb || _px[i + 1] != tg || _px[i + 2] != tr || _px[i + 3] != ta) continue;

            _px[i] = _color.B;
            _px[i + 1] = _color.G;
            _px[i + 2] = _color.R;
            _px[i + 3] = 255;

            stack.Push((px + 1, py));
            stack.Push((px - 1, py));
            stack.Push((px, py + 1));
            stack.Push((px, py - 1));
        }
    }

    // ---------- Espelhar braço / perna ----------

    // Cada face do membro direito corresponde a uma face do esquerdo, virada ao contrário.
    private static readonly (int Sx, int Sy, int Dx, int Dy, int W, int H)[] ArmFaces =
    {
        // base: topo, baixo, lado externo, frente, lado interno, costas (direito -> esquerdo)
        (44, 16, 36, 48, 4, 4), (48, 16, 40, 48, 4, 4), (40, 20, 40, 52, 4, 12),
        (44, 20, 36, 52, 4, 12), (48, 20, 32, 52, 4, 12), (52, 20, 44, 52, 4, 12),
        // manga (camada externa)
        (44, 32, 52, 48, 4, 4), (48, 32, 56, 48, 4, 4), (40, 36, 56, 52, 4, 12),
        (44, 36, 52, 52, 4, 12), (48, 36, 48, 52, 4, 12), (52, 36, 60, 52, 4, 12),
    };

    private static readonly (int Sx, int Sy, int Dx, int Dy, int W, int H)[] LegFaces =
    {
        (4, 16, 20, 48, 4, 4), (8, 16, 24, 48, 4, 4), (0, 20, 24, 52, 4, 12),
        (4, 20, 20, 52, 4, 12), (8, 20, 16, 52, 4, 12), (12, 20, 28, 52, 4, 12),
        // calça (camada externa)
        (4, 32, 4, 48, 4, 4), (8, 32, 8, 48, 4, 4), (0, 36, 8, 52, 4, 12),
        (4, 36, 4, 52, 4, 12), (8, 36, 0, 52, 4, 12), (12, 36, 12, 52, 4, 12),
    };

    private void Mirror(bool arm, bool rightToLeft)
    {
        PushUndo();
        foreach (var (sx, sy, dx, dy, w, h) in arm ? ArmFaces : LegFaces)
        {
            var (ax, ay, bx, by) = rightToLeft ? (sx, sy, dx, dy) : (dx, dy, sx, sy);
            var copy = new byte[w * h * 4];
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                    Buffer.BlockCopy(_px, ((ay + j) * 64 + ax + (w - 1 - i)) * 4, copy, (j * w + i) * 4, 4);
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                    Buffer.BlockCopy(copy, (j * w + i) * 4, _px, ((by + j) * 64 + bx + i) * 4, 4);
        }
        Say(arm ? "Braço espelhado ✔" : "Perna espelhada ✔");
        Changed();
    }

    // ---------- Desfazer / refazer ----------

    private void PushUndo()
    {
        _undo.Add((byte[])_px.Clone());
        if (_undo.Count > 40) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add((byte[])_px.Clone());
        Array.Copy(_undo[^1], _px, _px.Length);
        _undo.RemoveAt(_undo.Count - 1);
        Changed();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add((byte[])_px.Clone());
        Array.Copy(_redo[^1], _px, _px.Length);
        _redo.RemoveAt(_redo.Count - 1);
        Changed();
    }

    // ---------- Atualização da tela ----------

    private void Changed()
    {
        Refresh();
        _dirty3d = true;
        _draftDirty = true;

        if (_pp != null)
        {
            _pp.Slim = _slim;
            _pp.OverlayLayer = _layerMode == 2;
            _pp.Invalidate();
        }
    }

    // ---------- Pintura em 3D por parte do corpo ----------

    private UIElement BuildModeSwitch()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(Pill("🎨  Textura 2D", "skinmode", 0, true, _ => SetMode3d(false)));
        row.Children.Add(Pill("🧍  Pintar em 3D por parte", "skinmode", 1, false, _ => SetMode3d(true)));
        return row;
    }

    private StackPanel BuildPartPanel()
    {
        _pp = new PartPainter3D { Texture = _px, Slim = _slim };
        _pp.PaintBegin += Pp_Begin;
        _pp.PaintMove += Pp_Move;
        _pp.PaintEnd += Pp_End;
        _pp.PickRequested += h => Pick(h.X, h.Y);
        _pp.HoverChanged += h =>
        {
            if (h is not { } hit) { _info.Text = Hint3D; return; }
            var i = (hit.Y * 64 + hit.X) * 4;
            _info.Text = $"x {hit.X}, y {hit.Y}  ·  {hit.Face}  ·  {(hit.Overlay ? "camada externa" : "base")}  ·  " +
                         $"#{_px[i + 2]:X2}{_px[i + 1]:X2}{_px[i]:X2}";
        };

        var panel = new StackPanel { Visibility = Visibility.Collapsed };

        // 1) Qual parte do corpo
        panel.Children.Add(Label("ESCOLHA A PARTE DO CORPO", 0));
        var parts = new WrapPanel();
        var names = new[] { "Cabeça", "Corpo", "Braço D", "Braço E", "Perna D", "Perna E" };
        for (var k = 0; k < names.Length; k++)
            parts.Children.Add(Pill(names[k], "pppart", (BodyPart)k, k == 0, o => { _pp!.Part = (BodyPart)o; }));
        panel.Children.Add(parts);

        // 2) Qual camada + ângulos rápidos
        var opts = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        _ppLayerPills[0] = Pill("Base", "pplayer", 1, true, o => { if (!_syncingLayer) _layerPills[1].IsChecked = true; });
        _ppLayerPills[1] = Pill("Camada externa", "pplayer", 2, false, o => { if (!_syncingLayer) _layerPills[2].IsChecked = true; });
        opts.Children.Add(_ppLayerPills[0]);
        opts.Children.Add(_ppLayerPills[1]);
        var gridCheck3d = new CheckBox { Content = "Grade", IsChecked = true, Margin = new Thickness(6, 3, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        gridCheck3d.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        gridCheck3d.Click += (_, _) => _pp!.ShowGrid = gridCheck3d.IsChecked == true;
        opts.Children.Add(gridCheck3d);
        panel.Children.Add(opts);

        var views = new WrapPanel();
        views.Children.Add(Btn("Frente", "Ver de frente", () => _pp!.SetView(0, 0)));
        views.Children.Add(Btn("Costas", "Ver por trás", () => _pp!.SetView(180, 0)));
        views.Children.Add(Btn("Lado D", "Lado direito do personagem", () => _pp!.SetView(90, 0)));
        views.Children.Add(Btn("Lado E", "Lado esquerdo do personagem", () => _pp!.SetView(-90, 0)));
        views.Children.Add(Btn("Topo", "Ver de cima", () => _pp!.SetView(0, 89)));
        views.Children.Add(Btn("Baixo", "Ver de baixo", () => _pp!.SetView(0, -89)));
        views.Children.Add(Btn("↺", "Voltar ao ângulo e zoom iniciais", () => _pp!.ResetView()));
        panel.Children.Add(views);

        // 3) A peça em 3D
        var card = new Border
        {
            Width = Side + 2,
            Height = 330,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _pp
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        panel.Children.Add(card);

        _ppPanel = panel;
        return panel;
    }

    private void SetMode3d(bool on)
    {
        if (_mode3d == on || _pp == null) return;
        _mode3d = on;
        _stageCard.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _ppPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _cursor.Visibility = Visibility.Collapsed;

        // No 3D só se pinta uma camada por vez ("Tudo" vira "Só base").
        if (on && _layerMode == 0) _layerPills[1].IsChecked = true;
        SyncPpLayer();

        _info.Text = on ? Hint3D : HintText;
        Changed();
    }

    private void SyncPpLayer()
    {
        if (_pp == null || _ppLayerPills[0] == null) return;
        _syncingLayer = true;
        try
        {
            _ppLayerPills[0].IsChecked = _layerMode != 2;
            _ppLayerPills[1].IsChecked = _layerMode == 2;
            _pp.OverlayLayer = _layerMode == 2;
        }
        finally { _syncingLayer = false; }
    }

    private void Pp_Begin(PartHit h)
    {
        if (EffTool == Tool.Picker) { Pick(h.X, h.Y); return; }

        PushUndo();
        _drag = true; // segura o salvamento do rascunho durante o traço
        _clip = (h.FX0, h.FY0, h.FX1, h.FY1);

        if (EffTool == Tool.Fill)
        {
            FloodFill(h.X, h.Y);
            _drag = false;
            _clip = null;
            RememberColor();
        }
        else Stamp(h.X, h.Y);

        Changed();
    }

    private void Pp_Move(PartHit h)
    {
        if (!_drag) return;
        _clip = (h.FX0, h.FY0, h.FX1, h.FY1);
        Stamp(h.X, h.Y);
        Changed();
    }

    private void Pp_End()
    {
        _clip = null;
        if (!_drag) return;
        _drag = false;
        if (EffTool is not (Tool.Eraser or Tool.Lighten or Tool.Darken or Tool.Noise)) RememberColor();
        Changed();
    }

    // ---------- Ajustes de cor da skin inteira ----------

    private static Color Brighter(Color c) => Color.FromRgb(
        (byte)Math.Min(255, c.R + 18 + (255 - c.R) / 10),
        (byte)Math.Min(255, c.G + 18 + (255 - c.G) / 10),
        (byte)Math.Min(255, c.B + 18 + (255 - c.B) / 10));

    private static Color Darker(Color c) => Color.FromRgb(
        (byte)Math.Max(0, c.R - 18 - c.R / 10),
        (byte)Math.Max(0, c.G - 18 - c.G / 10),
        (byte)Math.Max(0, c.B - 18 - c.B / 10));

    // Gira o matiz (a "cor" em si) mantendo o brilho e a saturação.
    private static Color ShiftHue(Color c, double degrees)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d < 1e-9) return c; // cinza não tem matiz

        double h;
        if (max == r) h = (g - b) / d % 6;
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;

        var s = d / max;
        var v = max;
        h = (h + degrees) % 360;
        if (h < 0) h += 360;

        var chroma = v * s;
        var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - chroma;
        double r1, g1, b1;
        if (h < 60) { r1 = chroma; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = chroma; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = chroma; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = chroma; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = chroma; }
        else { r1 = chroma; g1 = 0; b1 = x; }

        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private void AdjustAll(string what, Func<Color, Color> change)
    {
        PushUndo();
        var n = 0;
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                if (!Allowed(x, y)) continue; // respeita "Só base" / "Só externa"
                var i = (y * 64 + x) * 4;
                if (_px[i + 3] == 0) continue;

                var c = change(Color.FromRgb(_px[i + 2], _px[i + 1], _px[i]));
                _px[i] = c.B;
                _px[i + 1] = c.G;
                _px[i + 2] = c.R;
                n++;
            }
        Say($"{what} ✔ ({n} pixels). Não gostou? Ctrl+Z desfaz.");
        Changed();
    }

    // ---------- Rascunho automático ----------

    private void SaveDraft()
    {
        _draftDirty = false;
        try
        {
            Directory.CreateDirectory(DraftDir);
            File.WriteAllBytes(DraftPng, EncodePng());
            if (_slim) File.WriteAllText(DraftSlim, "1");
            else if (File.Exists(DraftSlim)) File.Delete(DraftSlim);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Editor de skin: não consegui salvar o rascunho", ex);
        }
    }

    private void TryRestoreDraft()
    {
        try
        {
            if (!File.Exists(DraftPng)) return;
            LoadSkin(File.ReadAllBytes(DraftPng), File.Exists(DraftSlim));
            Say("Rascunho anterior recuperado ✔ (sua última edição foi salva sozinha). Use “Começar de” para recomeçar.");
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Editor de skin: rascunho ilegível", ex);
        }
    }

    private void Refresh()
    {
        var d = (byte[])_px.Clone();
        if (_layerMode != 0)
        {
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var r = RegionAt(x, y);
                    if ((_layerMode == 1 && r == 2) || (_layerMode == 2 && r == 1))
                        d[(y * 64 + x) * 4 + 3] = 0;
                }
        }
        _bmp.WritePixels(new Int32Rect(0, 0, 64, 64), d, 64 * 4, 0);
    }

    private byte[] EncodePng()
    {
        var bs = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, _px, 64 * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bs));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private void Say(string text) => _status.Text = text;

    // ---------- Começar de… / salvar ----------

    public void LoadSkin(byte[] png, bool slim)
    {
        var px = SkinTools.ToPixels(png);
        Array.Copy(px, _px, _px.Length);
        _slim = slim;
        _undo.Clear();
        _redo.Clear();
        Changed();
    }

    private void ResetTo(byte[] pixels, string message)
    {
        PushUndo();
        Array.Copy(pixels, _px, _px.Length);
        Say(message);
        Changed();
    }

    private void Blank()
    {
        var px = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                if (RegionAt(x, y) == 1)
                {
                    var i = (y * 64 + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = 0xE0;
                    px[i + 3] = 255;
                }
        ResetTo(px, "Skin em branco pronta para pintar.");
    }

    private void ImportFromAccount()
    {
        try
        {
            var cur = CurrentSkinProvider?.Invoke();
            if (cur?.Png == null) { Say("A conta ativa ainda não tem skin."); return; }
            PushUndo();
            var px = SkinTools.ToPixels(cur.Value.Png);
            Array.Copy(px, _px, _px.Length);
            _slim = cur.Value.Slim;
            Say("Skin da conta carregada.");
            Changed();
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Editor de skin: importar da conta falhou", ex);
            Say("Não consegui abrir a skin da conta.");
        }
    }

    private void OpenFile()
    {
        var dlg = new OpenFileDialog { Title = "Abrir skin", Filter = "Skin (*.png)|*.png", CheckFileExists = true };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var png = SkinTools.NormalizeToPng(File.ReadAllBytes(dlg.FileName));
            PushUndo();
            Array.Copy(SkinTools.ToPixels(png), _px, _px.Length);
            _nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            Say("Skin aberta.");
            Changed();
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    private void ClearOverlay()
    {
        PushUndo();
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                if (RegionAt(x, y) == 2)
                {
                    var i = (y * 64 + x) * 4;
                    _px[i] = _px[i + 1] = _px[i + 2] = _px[i + 3] = 0;
                }
        Say("Camada externa limpa.");
        Changed();
    }

    private byte[]? FinalPng()
    {
        try { return SkinTools.NormalizeToPng(EncodePng()); }
        catch (Exception ex) { Say("Não consegui montar a skin: " + ex.Message); return null; }
    }

    private void SaveLib()
    {
        var png = FinalPng();
        if (png == null) return;
        var name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "minha-skin" : _nameBox.Text.Trim();
        SaveRequested?.Invoke(png, _slim, name);
        Say($"Guardada na biblioteca como “{name}” ✔ (aba Minhas skins).");
    }

    private void UseOnAccount()
    {
        var png = FinalPng();
        if (png == null) return;
        var msg = UseRequested?.Invoke(png, _slim);
        Say(msg ?? "Pronto.");
    }

    private void Export()
    {
        var png = FinalPng();
        if (png == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Exportar skin",
            Filter = "Skin do Minecraft (*.png)|*.png",
            FileName = (string.IsNullOrWhiteSpace(_nameBox.Text) ? "minha-skin" : _nameBox.Text.Trim()) + ".png"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, png);
            Say("Exportada ✔");
        }
        catch (Exception ex) { Say("Não consegui salvar: " + ex.Message); }
    }
}
