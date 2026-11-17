using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using IOPath = System.IO.Path;
using Sh = System.Windows.Shapes;

namespace MoonlightLauncher;

/// <summary>
/// Tema secreto "PlayStation 1" (ativado na aba Golden Moon digitando o código secreto).
/// Aqui ficam as imagens, os sons e a regra dos cantos quadrados; a tela de menu e a abertura estão
/// logo abaixo, na parte do MainWindow.
/// </summary>
public static class Ps1
{
    /// <summary>true = o tema PS1 está ligado.</summary>
    public static bool On { get; set; }

    private static bool _handlers;
    private static readonly ConditionalWeakTable<Border, object> Radii = new();
    private static readonly Dictionary<string, byte[]> SoundCache = new();
    private static WaveOutEvent? _bootOut;

    /// <summary>O código vale com qualquer maiúscula/minúscula e com ou sem hífen: "Playstation-one".</summary>
    public static bool IsCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString() == "playstationone";
    }

    // ---------- Cantos quadrados (tudo no PS1 era "blocão") ----------

    public static void RegisterHandlers()
    {
        if (_handlers) return;
        _handlers = true;

        // Todo Border que aparece na tela (inclusive os de listas e abas abertas depois) vira quadrado.
        EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (On && s is Border b) Square(b);
            }));
    }

    public static void Square(Border b)
    {
        if (b.CornerRadius == new CornerRadius(0)) return;
        Radii.AddOrUpdate(b, b.CornerRadius);
        b.CornerRadius = new CornerRadius(0);
    }

    public static void Restore(Border b)
    {
        if (Radii.TryGetValue(b, out var o) && o is CornerRadius cr)
        {
            b.CornerRadius = cr;
            Radii.Remove(b);
        }
    }

    /// <summary>Percorre a janela inteira deixando os cantos quadrados (ou devolvendo os originais).</summary>
    public static void Walk(DependencyObject root, bool square)
    {
        try
        {
            if (root is Border b)
            {
                if (square) Square(b); else Restore(b);
            }

            var n = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < n; i++)
                Walk(VisualTreeHelper.GetChild(root, i), square);
        }
        catch { /* um pedaço que não é visual: ignora */ }
    }

    // ---------- Imagens ----------

    public static ImageSource Img(string name, int decodeWidth = 0)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri("pack://application:,,,/Assets/ps1/" + name, UriKind.Absolute);
        if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth; // imagem grande vira bitmap menor na memória (mais leve)
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    // ---------- Sons ----------

    private static byte[]? Load(string file)
    {
        if (SoundCache.TryGetValue(file, out var cached)) return cached;
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ps1/" + file, UriKind.Absolute));
            if (info == null) return null;
            using var s = info.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            SoundCache[file] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Toca um som curto (vários podem tocar ao mesmo tempo). Se não houver placa de som, não faz nada.</summary>
    public static void Play(string file)
    {
        try
        {
            byte[]? data = null;

            // O som de abertura pode ser trocado por um arquivo seu: coloque "ps1_boot.wav" na pasta de dados do launcher.
            if (file == "boot.wav")
            {
                var custom = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "ps1_boot.wav");
                if (File.Exists(custom)) data = File.ReadAllBytes(custom);
            }

            data ??= Load(file);
            if (data == null) return;

            if (file == "boot.wav") StopBoot();

            var reader = new WaveFileReader(new MemoryStream(data));
            var wo = new WaveOutEvent();
            wo.Init(reader);
            wo.PlaybackStopped += (_, _) =>
            {
                try { wo.Dispose(); reader.Dispose(); } catch { /* ignora */ }
                if (ReferenceEquals(_bootOut, wo)) _bootOut = null;
            };
            if (file == "boot.wav") _bootOut = wo;
            wo.Play();
        }
        catch (Exception ex)
        {
            LauncherLog.Write("O som do tema PS1 falhou", ex);
        }
    }

    /// <summary>
    /// Vídeo de abertura do console. Se existir %AppData%\MoonlightLauncher\ps1_boot.mp4 usa esse arquivo (dá para trocar);
    /// senão grava ali o que veio dentro do launcher (só na primeira vez) e usa.
    /// </summary>
    public static string? CustomBootVideo()
    {
        try
        {
            var dir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher");
            var p = IOPath.Combine(dir, "ps1_boot.mp4");
            if (File.Exists(p)) return p;

            var bytes = Load("boot_sony.mp4");
            if (bytes == null) return null;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(p, bytes);
            return p;
        }
        catch
        {
            return null;
        }
    }

    public static void StopBoot()
    {
        try { _bootOut?.Stop(); } catch { /* ignora */ }
        _bootOut = null;
    }
}

public partial class MainWindow
{
    private sealed class Ps1Row
    {
        public string Tag = "";
        public Grid Root = null!;
        public TranslateTransform Move = null!;
        public Sh.Polygon Poly = null!;
        public TextBlock Star = null!;
        public TextBlock Label = null!;
        public TextBlock Cursor = null!;
    }

    private static readonly FontFamily Ps1Font = new("Lucida Console");
    private static readonly FontFamily Ps1Symbols = new("Segoe UI Symbol");

    // Logotipo do menu: tamanho e posição final (canto superior esquerdo).
    private const double PsLogoW = 340, PsLogoH = 227, PsLogoLeft = 34, PsLogoTop = 18;

    private bool _ps1Built, _ps1MenuOpen, _ps1IntroRunning, _ps1Skip;
    private int _ps1Sel;
    private string _ps1CurTag = "Play";

    private Grid _ps1BgLayer = null!, _ps1Menu = null!, _ps1FxLayer = null!, _ps1Boot = null!, _ps1LogoHost = null!;
    private Grid _ps1Footer = null!, _ps1TopBar = null!;
    private Border _ps1Fade = null!, _ps1Card = null!;
    private TextBlock _ps1CardName = null!, _ps1CardVer = null!, _ps1CardRam = null!;
    private Image _ps1BootImg = null!;
    private ScaleTransform _ps1LogoScale = null!;
    private TranslateTransform _ps1LogoMove = null!;
    private StackPanel _ps1Header = null!;
    private TextBlock _ps1HeaderTitle = null!;
    private readonly List<Ps1Row> _ps1Rows = new();

    private static SolidColorBrush Ps1Br(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static readonly Brush Ps1IdleFill = Ps1Br("#CC0B1238");
    private static readonly Brush Ps1IdleLine = Ps1Br("#9076A0FF");
    private static readonly Brush Ps1SelLine = Ps1Br("#FFFFFFFF");
    private static readonly Brush Ps1IdleText = Ps1Br("#FFCFD8FF");
    private static readonly Brush Ps1SelText = Ps1Br("#FFFFFFFF");
    private static readonly Brush Ps1IdleStar = Ps1Br("#FF8FB4FF");
    private static readonly Brush Ps1SelFill = MakeSelFill();

    private static Brush MakeSelFill()
    {
        var g = new LinearGradientBrush(Color.FromRgb(0x62, 0x84, 0xFF), Color.FromRgb(0x25, 0x37, 0xC2), new Point(0, 0), new Point(0, 1));
        g.Freeze();
        return g;
    }

    private static PointCollection Ps1Chamfer(double w, double h, double c) => new()
    {
        new Point(c, 0), new Point(w - c, 0), new Point(w, c), new Point(w, h - c),
        new Point(w - c, h), new Point(c, h), new Point(0, h - c), new Point(0, c)
    };

    private static readonly (string Label, string Tag)[] Ps1Items =
    {
        ("JOGAR", "Play"), ("MODS", "Mods"), ("CONTAS", "Accounts"), ("SKINS", "Skins"), ("PASTAS", "Folders"),
        ("AMIGOS", "Friends"), ("AJUSTES", "Settings"), ("GOLDEN MOON", "Golden"), ("SAIR", "Exit")
    };

    // ---------- Montagem das telas (feita uma vez só) ----------

    private void BuildPs1Ui()
    {
        if (_ps1Built) return;
        if (WindowRoot.Child is not Grid root) return;
        _ps1Built = true;
        Ps1.RegisterHandlers();

        // Fundo que aparece atrás das abas (céu retrô, bem escurecido).
        _ps1BgLayer = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _ps1BgLayer.Children.Add(new Image { Source = Ps1.Img("menu_bg.png"), Stretch = Stretch.UniformToFill, Opacity = 0.55, CacheMode = new BitmapCache(1) });
        _ps1BgLayer.Children.Add(new Border { Background = Ps1Br("#A0000014") });
        if (Ps1MakeAmbient() is { } ambBg) _ps1BgLayer.Children.Add(ambBg); // partículas por cima do escurecido, atrás das abas
        root.Children.Insert(0, _ps1BgLayer);

        // ===== Tela de menu (título) =====
        _ps1Menu = new Grid { Visibility = Visibility.Collapsed, Background = Brushes.Black };
        TextOptions.SetTextFormattingMode(_ps1Menu, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_ps1Menu, TextRenderingMode.Aliased);

        var artMove = new TranslateTransform();
        var artGroup = new TransformGroup();
        artGroup.Children.Add(new ScaleTransform(1.07, 1.07));
        artGroup.Children.Add(artMove);
        _ps1Menu.Children.Add(new Image
        {
            Source = Ps1.Img("menu_bg.png"), Stretch = Stretch.UniformToFill,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = artGroup,
            CacheMode = new BitmapCache(1) // o céu vira "figurinha": deslizar só move a imagem pronta
        });
        if (!Perf.Light)
        {
            // O céu vai deslizando bem devagar de um lado para o outro (30 quadros bastam).
            var slide = new DoubleAnimation(-20, 20, TimeSpan.FromSeconds(18))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(slide, 30);
            artMove.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        if (Ps1MakeAmbient() is { } ambMenu) _ps1Menu.Children.Add(ambMenu);
        _ps1Menu.Children.Add(new Sh.Rectangle
        {
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromArgb(0xE6, 0, 0, 0x14), Color.FromArgb(0, 0, 0, 0x14), new Point(0, 0), new Point(0.62, 0))
        });

        // Tela preta que vai clareando na abertura.
        _ps1Fade = new Border { Background = Brushes.Black, IsHitTestVisible = false };
        _ps1Menu.Children.Add(_ps1Fade);

        // Logotipo 3D (gira de leve) com um brilho atrás.
        _ps1LogoScale = new ScaleTransform(1, 1);
        _ps1LogoMove = new TranslateTransform();
        var tg = new TransformGroup();
        tg.Children.Add(_ps1LogoScale);
        tg.Children.Add(_ps1LogoMove);

        _ps1LogoHost = new Grid
        {
            Width = PsLogoW, Height = PsLogoH,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(PsLogoLeft, PsLogoTop, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = tg,
            IsHitTestVisible = false
        };
        _ps1LogoHost.Children.Add(Ps1MakeLogo2D());
        _ps1Menu.Children.Add(_ps1LogoHost);

        // Botões do menu (as categorias de verdade do launcher).
        var list = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(46, 262, 0, 0) };
        for (var i = 0; i < Ps1Items.Length; i++)
        {
            var row = Ps1MakeRow(Ps1Items[i].Label, Ps1Items[i].Tag, i);
            _ps1Rows.Add(row);
            list.Children.Add(row.Root);
        }
        _ps1Menu.Children.Add(list);

        // Rodapé: dicas e versão.
        _ps1Footer = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(50, 0, 24, 14), IsHitTestVisible = false, Opacity = 0 };
        var hints = new TextBlock
        {
            FontFamily = Ps1Font, FontSize = 11, Foreground = Ps1Br("#B9C6FF"), HorizontalAlignment = HorizontalAlignment.Left
        };
        hints.Inlines.Add("▲▼ SELECIONAR      ");
        hints.Inlines.Add(new Run("✕") { Foreground = Ps1Br("#6EA8FF"), FontFamily = Ps1Symbols, FontWeight = FontWeights.Bold });
        hints.Inlines.Add(" CONFIRMAR (ENTER)      ");
        hints.Inlines.Add(new Run("○") { Foreground = Ps1Br("#FF6B6B"), FontFamily = Ps1Symbols, FontWeight = FontWeights.Bold });
        hints.Inlines.Add(" VOLTAR AO MENU (ESC)");
        _ps1Footer.Children.Add(hints);
        _ps1Footer.Children.Add(new TextBlock
        {
            Text = "MOONLIGHT LAUNCHER  " + Updater.CurrentText,
            FontFamily = Ps1Font, FontSize = 11, Foreground = Ps1Br("#B9C6FF"), HorizontalAlignment = HorizontalAlignment.Right
        });
        _ps1Menu.Children.Add(_ps1Footer);

        // "Memory card": mostra quem está jogando, a versão escolhida e a memória, como no BIOS do console.
        _ps1Card = new Border
        {
            Width = 280, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 34, 56), Padding = new Thickness(14, 10, 14, 12),
            Background = Ps1Br("#D00B1238"), BorderBrush = Ps1Br("#B07C98FF"), BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false, Opacity = 0
        };
        var cardStack = new StackPanel();
        cardStack.Children.Add(new TextBlock
        {
            Text = "▣ MEMORY CARD 1", FontFamily = Ps1Font, FontWeight = FontWeights.Bold, FontSize = 12,
            Foreground = Ps1Br("#8FB4FF"), Margin = new Thickness(0, 0, 0, 8)
        });
        _ps1CardName = Ps1CardLine(cardStack, "JOGADOR");
        _ps1CardVer = Ps1CardLine(cardStack, "VERSÃO");
        _ps1CardRam = Ps1CardLine(cardStack, "MEMÓRIA");
        _ps1Card.Child = cardStack;
        _ps1Menu.Children.Add(_ps1Card);

        // Barra de cima: arrastar a janela, minimizar e fechar.
        _ps1TopBar = new Grid { Height = 44, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Opacity = 0 };
        _ps1TopBar.MouseLeftButtonDown += TitleBar_MouseDown;
        var win = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        win.Children.Add(new Button { Style = (Style)FindResource("WinButton"), Content = "—" });
        win.Children.Add(new Button { Style = (Style)FindResource("WinButton"), Content = "✕" });
        ((Button)win.Children[0]).Click += Minimize_Click;
        ((Button)win.Children[1]).Click += Close_Click;
        _ps1TopBar.Children.Add(win);
        _ps1Menu.Children.Add(_ps1TopBar);

        // ===== Efeito de TV antiga: linhas finas e cantos escurecidos =====
        _ps1FxLayer = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, CacheMode = new BitmapCache(1) };
        var lines = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 3, 3),
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 3, 3),
            Drawing = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 2, 3, 1)))
        };
        RenderOptions.SetCachingHint(lines, CachingHint.Cache);
        lines.Freeze();
        _ps1FxLayer.Children.Add(new Sh.Rectangle { Fill = lines, IsHitTestVisible = false });
        var vignette = new RadialGradientBrush();
        vignette.GradientStops.Add(new GradientStop(Colors.Transparent, 0.62));
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0x10), 1));
        vignette.Freeze();
        _ps1FxLayer.Children.Add(new Sh.Rectangle { IsHitTestVisible = false, Fill = vignette });

        // ===== Tela de abertura do console =====
        _ps1Boot = new Grid { Visibility = Visibility.Collapsed, Background = Brushes.Black };
        _ps1BootImg = new Image { Source = Ps1.Img("boot.png"), Stretch = Stretch.Uniform, Margin = new Thickness(30), Opacity = 0 };
        _ps1Boot.Children.Add(_ps1BootImg);

        var at = root.Children.IndexOf(Overlay);
        if (at < 0) at = root.Children.Count;
        root.Children.Insert(at, _ps1Menu);
        root.Children.Insert(at + 1, _ps1FxLayer);
        var ov = root.Children.IndexOf(Overlay);
        root.Children.Insert(ov + 1, _ps1Boot); // acima dos avisos, abaixo do clarão/brilhinhos

        // ===== Cabeçalho que aparece em cada aba (voltar ao menu + logotipo + nome da aba) =====
        var back = Ps1MakeShapeButton("◀ MENU", 118, 30, out _, out _);
        back.MouseLeftButtonDown += (_, e) =>
        {
            if (_ps1IntroRunning) return;
            Ps1.Play("back.wav");
            Ps1ShowMenu(true);
            e.Handled = true;
        };

        _ps1Header = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0), Visibility = Visibility.Collapsed
        };
        _ps1Header.Children.Add(back);
        _ps1Header.Children.Add(new Image { Source = Ps1.Img("logo3d.png", 260), Height = 38, Margin = new Thickness(16, 0, 0, 0) });
        _ps1HeaderTitle = new TextBlock
        {
            FontFamily = Ps1Font, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0)
        };
        _ps1Header.Children.Add(_ps1HeaderTitle);
        TitleBar.Children.Add(_ps1Header);

        PreviewKeyDown += Ps1PreviewKeyDown;
        PreviewMouseDown += Ps1PreviewMouseDown;
    }

    // Logotipo parado, só a imagem (sem brilho, sem giro, sem Viewport3D). O fundo transparente do PNG deixa ver o céu.
    private Image Ps1MakeLogo2D()
    {
        var img = new Image
        {
            Source = Ps1.Img("logo3d.png", 700),
            Width = PsLogoW, Height = PsLogoH, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
        return img;
    }

    // Partículas do fundo: losangos coloridos subindo devagar (clima de BIOS do PS1). Poucos, 30 quadros, sem efeitos.
    private static readonly Color[] Ps1AmbientColors =
    {
        Color.FromRgb(0x62, 0x84, 0xFF), Color.FromRgb(0x9B, 0xBC, 0xFF),
        Color.FromRgb(0xC7, 0x7D, 0xFF), Color.FromRgb(0x4F, 0xC3, 0xF7)
    };

    private static Viewbox? Ps1MakeAmbient()
    {
        if (Perf.Light) return null;

        var canvas = new Canvas { Width = 1280, Height = 720, IsHitTestVisible = false };
        var rnd = new Random(1997);
        for (var i = 0; i < 14; i++)
        {
            var size = 8 + rnd.Next(0, 5) * 4;
            var col = Ps1AmbientColors[rnd.Next(Ps1AmbientColors.Length)];
            var fill = new SolidColorBrush(Color.FromArgb(0x55, col.R, col.G, col.B));
            fill.Freeze();
            var line = new SolidColorBrush(Color.FromArgb(0xA0, col.R, col.G, col.B));
            line.Freeze();

            var move = new TranslateTransform();
            var tg = new TransformGroup();
            tg.Children.Add(new RotateTransform(45));
            tg.Children.Add(move);
            var d = new Sh.Rectangle
            {
                Width = size, Height = size, Fill = fill, Stroke = line, StrokeThickness = 1,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = tg, Opacity = 0, IsHitTestVisible = false
            };
            Canvas.SetLeft(d, rnd.Next(0, 1260));
            Canvas.SetTop(d, 730);
            canvas.Children.Add(d);

            var secs = 20 + rnd.NextDouble() * 16;
            var begin = TimeSpan.FromSeconds(-rnd.NextDouble() * secs); // já começam espalhados pela tela
            var rise = new DoubleAnimation(0, -780, TimeSpan.FromSeconds(secs))
                { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            Timeline.SetDesiredFrameRate(rise, 30);
            move.BeginAnimation(TranslateTransform.YProperty, rise);

            var fade = new DoubleAnimationUsingKeyFrames
                { Duration = TimeSpan.FromSeconds(secs), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(fade, 30);
            d.BeginAnimation(OpacityProperty, fade);
        }
        return new Viewbox { Stretch = Stretch.UniformToFill, Child = canvas, IsHitTestVisible = false };
    }

    private static TextBlock Ps1CardLine(StackPanel host, string caption)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = caption, FontFamily = Ps1Font, FontSize = 11, Foreground = Ps1Br("#9FB0F0") });
        var v = new TextBlock
        {
            FontFamily = Ps1Font, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        host.Children.Add(g);
        return v;
    }

    private void Ps1RefreshCard()
    {
        if (!_ps1Built) return;
        _ps1CardName.Text = _active?.Name ?? "—";
        var ver = VersionList.SelectedItem as string;
        _ps1CardVer.Text = string.IsNullOrEmpty(ver) ? "—" : $"{_loader.ToString().ToUpperInvariant()} {ver}";
        _ps1CardRam.Text = $"{_state.Ram / 1024.0:0.#} GB";
    }

    // Botão "recortado" (cantos cortados) usado no cabeçalho.
    private static Grid Ps1MakeShapeButton(string text, double w, double h, out Sh.Polygon poly, out TextBlock label)
    {
        var g = new Grid { Width = w, Height = h, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        poly = new Sh.Polygon
        {
            Points = Ps1Chamfer(w, h, 7), Fill = Ps1IdleFill, Stroke = Ps1Br("#B07C98FF"),
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Miter
        };
        label = new TextBlock
        {
            Text = text, FontFamily = Ps1Font, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Ps1Br("#E6ECFF"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        g.Children.Add(poly);
        g.Children.Add(label);
        return g;
    }

    private Ps1Row Ps1MakeRow(string label, string tag, int index)
    {
        var row = new Ps1Row { Tag = tag, Move = new TranslateTransform() };

        row.Root = new Grid
        {
            Width = 336, Height = 34, Margin = new Thickness(0, 0, 0, 5), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = row.Move
        };
        row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Cursor = new TextBlock
        {
            Text = "▶", FontFamily = Ps1Symbols, FontSize = 15, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0
        };

        var body = new Grid { Width = 308, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(body, 1);
        row.Poly = new Sh.Polygon
        {
            Points = Ps1Chamfer(308, 34, 8), Fill = Ps1IdleFill, Stroke = Ps1IdleLine,
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Miter
        };
        row.Star = new TextBlock
        {
            Text = "✦", FontFamily = Ps1Symbols, FontSize = 16, Foreground = Ps1IdleStar,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 0, 0)
        };
        row.Label = new TextBlock
        {
            Text = label, FontFamily = Ps1Font, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Ps1IdleText,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(46, 0, 0, 0)
        };
        body.Children.Add(row.Poly);
        body.Children.Add(row.Star);
        body.Children.Add(row.Label);

        row.Root.Children.Add(row.Cursor);
        row.Root.Children.Add(body);

        var i = index;
        row.Root.MouseEnter += (_, _) =>
        {
            if (_ps1MenuOpen) Ps1Select(i, true);
        };
        row.Root.MouseLeftButtonDown += (_, e) =>
        {
            if (!_ps1MenuOpen) return;
            Ps1Activate(i);
            e.Handled = true;
        };
        return row;
    }

    // ---------- Seleção e escolha no menu ----------

    private void Ps1Select(int idx, bool sound)
    {
        if (_ps1Rows.Count == 0) return;
        idx = (idx % _ps1Rows.Count + _ps1Rows.Count) % _ps1Rows.Count;
        if (idx == _ps1Sel) return;
        _ps1Sel = idx;
        Ps1Paint();
        if (sound) Ps1.Play("tick.wav");
    }

    private void Ps1Paint()
    {
        for (var i = 0; i < _ps1Rows.Count; i++)
        {
            var r = _ps1Rows[i];
            var sel = i == _ps1Sel;

            r.Poly.Fill = sel ? Ps1SelFill : Ps1IdleFill;
            r.Poly.Stroke = sel ? Ps1SelLine : Ps1IdleLine;
            r.Poly.StrokeThickness = sel ? 2.5 : 1.5; // brilho barato: contorno mais grosso, sem DropShadowEffect
            r.Label.Foreground = sel ? Ps1SelText : Ps1IdleText;
            r.Star.Foreground = sel ? Ps1SelText : Ps1IdleStar;

            r.Move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(sel ? 10 : 0, TimeSpan.FromMilliseconds(110)));

            r.Cursor.BeginAnimation(OpacityProperty, null);
            if (sel)
            {
                r.Cursor.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                r.Cursor.Opacity = 0;
            }
        }
    }

    private void Ps1Activate(int idx)
    {
        if (idx < 0 || idx >= _ps1Rows.Count) return;
        var tag = _ps1Rows[idx].Tag;
        Ps1.Play("ok.wav");

        if (tag == "Exit")
        {
            Close();
            return;
        }

        Ps1ShowMenu(false);

        switch (tag)
        {
            case "Friends":
                GoTo("Play");
                ShowFriendsPage();
                break;
            case "Play":
                if (PageFriends.Visibility == Visibility.Visible) FriendsBack_Click(this, new RoutedEventArgs());
                GoTo("Play");
                break;
            default:
                GoTo(tag);
                break;
        }

        Ps1SetHeaderFor(tag);
    }

    private static string Ps1TitleOf(string tag) => tag switch
    {
        "Play" => "JOGAR",
        "Mods" => "MODS",
        "Accounts" => "CONTAS",
        "Skins" => "SKINS",
        "Folders" => "PASTAS",
        "Friends" => "AMIGOS",
        "Settings" => "AJUSTES",
        "Golden" => "GOLDEN MOON",
        _ => ""
    };

    /// <summary>Chamado quando a aba muda: atualiza o nome mostrado no cabeçalho.</summary>
    private void Ps1SetHeaderFor(string tag)
    {
        if (!_ps1Built || string.IsNullOrEmpty(tag)) return;
        _ps1CurTag = tag;
        _ps1HeaderTitle.Text = Ps1TitleOf(tag);
    }

    // Abre ou fecha a tela de menu.
    private void Ps1ShowMenu(bool show)
    {
        if (!_ps1Built || !Ps1.On) return;

        if (show)
        {
            Ps1SetMenuFinalState();
            _ps1MenuOpen = true;
            _ps1Menu.Visibility = Visibility.Visible;
            _ps1Header.Visibility = Visibility.Collapsed;

            Ps1RefreshCard();
            var i = _ps1Rows.FindIndex(r => r.Tag == _ps1CurTag);
            if (i >= 0) _ps1Sel = i;
            Ps1Paint();

            if (Perf.Light) _ps1Menu.Opacity = 1;
            else _ps1Menu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        }
        else
        {
            _ps1MenuOpen = false;
            _ps1Header.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) =>
            {
                if (!_ps1MenuOpen) _ps1Menu.Visibility = Visibility.Collapsed;
            };
            _ps1Menu.BeginAnimation(OpacityProperty, fade);
        }
    }

    // Deixa o menu no estado "pronto": sem tela preta, logotipo no canto, botões e rodapé visíveis.
    private void Ps1SetMenuFinalState()
    {
        _ps1Menu.BeginAnimation(OpacityProperty, null);
        _ps1Menu.Opacity = 1;

        _ps1Fade.BeginAnimation(OpacityProperty, null);
        _ps1Fade.Opacity = 0;

        _ps1LogoHost.BeginAnimation(OpacityProperty, null);
        _ps1LogoHost.Opacity = 1;
        _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _ps1LogoScale.ScaleX = 1;
        _ps1LogoScale.ScaleY = 1;
        _ps1LogoMove.BeginAnimation(TranslateTransform.XProperty, null);
        _ps1LogoMove.BeginAnimation(TranslateTransform.YProperty, null);
        _ps1LogoMove.X = 0;
        _ps1LogoMove.Y = 0;

        foreach (var r in _ps1Rows)
        {
            r.Root.BeginAnimation(OpacityProperty, null);
            r.Root.Opacity = 1;
            r.Move.BeginAnimation(TranslateTransform.XProperty, null);
            r.Move.X = 0;
        }

        _ps1Footer.BeginAnimation(OpacityProperty, null);
        _ps1Footer.Opacity = 1;
        _ps1Card.BeginAnimation(OpacityProperty, null);
        _ps1Card.Opacity = 1;
        _ps1TopBar.BeginAnimation(OpacityProperty, null);
        _ps1TopBar.Opacity = 1;
    }

    // ---------- Teclado e mouse ----------

    private void Ps1PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Ps1.On) return;

        // Qualquer tecla pula a abertura.
        if (_ps1IntroRunning)
        {
            _ps1Skip = true;
            e.Handled = true;
            return;
        }

        if (Overlay.Visibility == Visibility.Visible) return; // há um aviso na tela

        if (_ps1MenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Ps1Select(_ps1Sel - 1, true);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Ps1Select(_ps1Sel + 1, true);
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Space:
                    Ps1Activate(_ps1Sel);
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not (TextBox or PasswordBox))
        {
            Ps1.Play("back.wav");
            Ps1ShowMenu(true);
            e.Handled = true;
        }
    }

    private void Ps1PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Ps1.On || !_ps1IntroRunning) return;
        _ps1Skip = true; // clicar pula a abertura
        e.Handled = true;
    }

    // ---------- Ligar e desligar o tema ----------

    /// <summary>Liga/desliga o tema PS1. Com playIntro, mostra a abertura completa.</summary>
    private void Ps1Apply(bool on, bool playIntro)
    {
        Ps1.On = on;
        ThemeManager.Ps1 = on;
        _state.Ps1Mode = on;
        if (on) _state.Ps1Unlocked = true;
        SaveState();

        BuildPs1Ui();
        UpdatePs1ThemeButton();
        SyncThemeRadios(); // o botão PS1 em Ajustes acompanha (também quando liga pelo código secreto)
        if (!_ps1Built) return;

        Ps1.Walk(WindowRoot, on); // cantos quadrados (ou de volta ao normal)

        SideBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SideCol.Width = new GridLength(on ? 0 : 92);
        TitleText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        MiniBar.Margin = new Thickness(on ? 330 : 26, 0, 0, 0);
        _ps1BgLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _ps1FxLayer.Visibility = on && !Perf.Light ? Visibility.Visible : Visibility.Collapsed;

        ApplyThemeNow(); // troca as cores do launcher inteiro

        if (on)
        {
            Ps1SetHeaderFor(_ps1CurTag);
            if (playIntro)
            {
                _ = Ps1PlayIntroAsync();
            }
            else
            {
                _ps1Menu.Visibility = Visibility.Visible;
                Ps1SetMenuFinalState();
                _ps1MenuOpen = true;
                Ps1Paint();
            }
        }
        else
        {
            _ps1Skip = true;
            Ps1.StopBoot();
            _ps1MenuOpen = false;
            _ps1Menu.Visibility = Visibility.Collapsed;
            _ps1Boot.Visibility = Visibility.Collapsed;
            _ps1Header.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- Abertura: logotipo do PlayStation → logotipo 3D do Moonlight → menu ----------

    private async Task<bool> Ps1Wait(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            if (_ps1Skip) return true;
            await Task.Delay(30);
        }
        return _ps1Skip;
    }

    private void Ps1LogoCenterOffset(out double tx, out double ty)
    {
        var w = WindowRoot.ActualWidth;
        var h = WindowRoot.ActualHeight;
        if (w < 100) w = 1180;
        if (h < 100) h = 690;
        tx = w / 2 - (PsLogoLeft + PsLogoW / 2);
        ty = h / 2 - (PsLogoTop + PsLogoH / 2) - 10;
    }

    private async Task Ps1PlayIntroAsync()
    {
        if (_ps1IntroRunning || !_ps1Built) return;
        _ps1IntroRunning = true;
        _ps1Skip = false;
        _ps1MenuOpen = false;

        try
        {
            // Estado inicial: tudo preto, logotipo no centro (grande e invisível), botões escondidos.
            _ps1Menu.BeginAnimation(OpacityProperty, null);
            _ps1Menu.Opacity = 1;
            _ps1Menu.Visibility = Visibility.Visible;
            _ps1Header.Visibility = Visibility.Collapsed;

            _ps1Fade.BeginAnimation(OpacityProperty, null);
            _ps1Fade.Opacity = 1;

            Ps1LogoCenterOffset(out var tx, out var ty);
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _ps1LogoScale.ScaleX = 1.5;
            _ps1LogoScale.ScaleY = 1.5;
            _ps1LogoMove.BeginAnimation(TranslateTransform.XProperty, null);
            _ps1LogoMove.BeginAnimation(TranslateTransform.YProperty, null);
            _ps1LogoMove.X = tx;
            _ps1LogoMove.Y = ty;
            _ps1LogoHost.BeginAnimation(OpacityProperty, null);
            _ps1LogoHost.Opacity = 0;

            foreach (var r in _ps1Rows)
            {
                r.Root.BeginAnimation(OpacityProperty, null);
                r.Root.Opacity = 0;
                r.Move.BeginAnimation(TranslateTransform.XProperty, null);
                r.Move.X = -50;
                r.Cursor.BeginAnimation(OpacityProperty, null);
                r.Cursor.Opacity = 0;
            }
            _ps1Footer.BeginAnimation(OpacityProperty, null);
            _ps1Footer.Opacity = 0;
            _ps1Card.BeginAnimation(OpacityProperty, null);
            _ps1Card.Opacity = 0;
            _ps1TopBar.BeginAnimation(OpacityProperty, null);
            _ps1TopBar.Opacity = 0;

            _ps1Boot.Visibility = Visibility.Visible;
            _ps1BootImg.BeginAnimation(OpacityProperty, null);
            _ps1BootImg.Opacity = 0;

            // 1) Abertura do console. Se existir o vídeo da sua própria cópia (ps1_boot.mp4), toca ele inteiro;
            //    senão, logotipo do PlayStation + som sintetizado.
            var played = false;
            var video = Ps1.CustomBootVideo();
            if (video != null)
            {
                played = await Ps1PlayBootVideoAsync(video);
                if (_ps1Skip) return;
            }

            if (!played)
            {
                if (await Ps1Wait(350)) return;
                Ps1.Play("boot.wav");
                _ps1BootImg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
                if (await Ps1Wait(3600)) return;
                _ps1BootImg.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)));
                if (await Ps1Wait(750)) return;
            }
            _ps1Boot.Visibility = Visibility.Collapsed;
            if (await Ps1Wait(250)) return;

            // 2) Logotipo 3D do Moonlight Launcher aparece girando, no meio da tela preta.
            var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            _ps1LogoHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
            var grow = new DoubleAnimation(0.8, 1.5, TimeSpan.FromMilliseconds(1200)) { EasingFunction = back };
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            if (await Ps1Wait(1250)) return;

            if (await Ps1Wait(1900)) return;

            // 3) O fundo vai clareando e o logotipo sobe para o canto superior esquerdo.
            var dur = TimeSpan.FromMilliseconds(1500);
            var cube = new CubicEase { EasingMode = EasingMode.EaseInOut };
            _ps1LogoMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur) { EasingFunction = cube });
            _ps1LogoMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = cube });
            var shrink = new DoubleAnimation(1, dur) { EasingFunction = cube };
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            _ps1LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            _ps1Fade.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1800)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });

            if (await Ps1Wait(1600)) return;
            Ps1RefreshCard();
            _ps1Footer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
            _ps1Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
            _ps1TopBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));

            // 4) Os botões do menu entram um por um.
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var r in _ps1Rows)
            {
                r.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                r.Move.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-50, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = easeOut });
                Ps1.Play("tick.wav");
                if (await Ps1Wait(85)) return;
            }
            if (await Ps1Wait(450)) return;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("A abertura do tema PS1 falhou", ex);
        }
        finally
        {
            Ps1FinishIntro();
        }
    }

    // Toca o vídeo de abertura (com o som dele). Devolve false se o vídeo não abriu.
    private async Task<bool> Ps1PlayBootVideoAsync(string path)
    {
        MediaElement? me = null;
        try
        {
            var done = new TaskCompletionSource<bool>();
            me = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Volume = 1.0,
                IsHitTestVisible = false
            };
            me.MediaEnded += (_, _) => done.TrySetResult(true);
            me.MediaFailed += (_, _) => done.TrySetResult(false);

            _ps1BootImg.BeginAnimation(OpacityProperty, null);
            _ps1BootImg.Opacity = 0;
            _ps1Boot.Children.Add(me);
            me.Source = new Uri(path, UriKind.Absolute);
            me.Play();

            var end = Environment.TickCount64 + 20000; // trava de segurança
            while (!done.Task.IsCompleted)
            {
                if (_ps1Skip || Environment.TickCount64 > end) break;
                await Task.Delay(30);
            }

            return !done.Task.IsCompleted || done.Task.Result;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("O vídeo de abertura do PS1 falhou", ex);
            return false;
        }
        finally
        {
            if (me != null)
            {
                try { me.Stop(); me.Close(); } catch { /* ignora */ }
                _ps1Boot.Children.Remove(me);
            }
        }
    }

    private void Ps1FinishIntro()
    {
        if (_ps1Skip) Ps1.StopBoot();
        _ps1IntroRunning = false;

        if (!Ps1.On) return; // o tema foi desligado no meio da abertura

        _ps1Boot.Visibility = Visibility.Collapsed;
        _ps1BootImg.BeginAnimation(OpacityProperty, null);
        _ps1BootImg.Opacity = 0;

        _ps1Menu.Visibility = Visibility.Visible;
        Ps1SetMenuFinalState();
        _ps1MenuOpen = true;
        _ps1Sel = 0;
        Ps1Paint();
    }
}
