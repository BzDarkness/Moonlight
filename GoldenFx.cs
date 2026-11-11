using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MoonlightLauncher;

// Animações do Golden Moon:
//  • ao entrar na aba Golden Moon: a lua dourada sobe, o título brilha e os benefícios aparecem um por um;
//  • ao ativar o Golden Moon: clarão dourado, ondas de luz, explosão de confetes e o mesmo desfile de benefícios.
// Clique em qualquer lugar para pular. Some sozinha depois de alguns segundos.
public partial class MainWindow
{
    private sealed record GoldBenefit(string Icon, string Title, string Desc);

    private static readonly GoldBenefit[] GoldBenefits =
    {
        new("🎨", "2 paletas de cores", "Um degradê só seu no launcher inteiro: fundo, botões, abas e detalhes."),
        new("🎭", "Criador de skin completo", "Pinte em 2D com o boneco 3D ao vivo, camadas, ajustes de cor e rascunho automático."),
        new("📦", "Criador de modpacks", "Monte seus modpacks com a versão e o loader que quiser e jogue direto por eles."),
        new("🖼", "Ícones exclusivos", "Troque o ícone do aplicativo e a mini lua por 6 desenhos: Redstone, Anjo, Mel, Órbita, Obsidiana e Rei."),
        new("✨", "Animações douradas", "Entrada especial na aba Golden Moon e uma explosão de estrelas quando você ativa."),
        new("💛", "Apoie o Moonlight", "Sua assinatura ajuda o Moonlight Launcher a continuar crescendo."),
    };

    private static readonly Color[] FxColors =
    {
        Color.FromRgb(0xFF, 0xE9, 0xA6), Color.FromRgb(0xFF, 0xC9, 0x4A), Color.FromRgb(0xFF, 0x9A, 0x1F),
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xD5, 0x4F)
    };

    private DispatcherTimer? _fxTimer;
    private int _fxId;
    private readonly Random _fxRnd = new();

    private static Color FxGold(byte alpha) => Color.FromArgb(alpha, 0xFF, 0xC9, 0x4A);

    private void GoldenFx_Click(object sender, MouseButtonEventArgs e)
    {
        HideGoldenFx();
        e.Handled = true;
    }

    private void GoldenReplay_Click(object sender, RoutedEventArgs e) => ShowGoldenFx(false);

    private void FxTimerTick(object? sender, EventArgs e)
    {
        _fxTimer?.Stop();
        HideGoldenFx();
    }

    private void HideGoldenFx()
    {
        _fxTimer?.Stop();
        if (GoldenFx.Visibility != Visibility.Visible) return;

        var id = ++_fxId;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
        fade.Completed += (_, _) =>
        {
            if (id != _fxId) return; // começou outra animação enquanto esta sumia
            GoldenFx.BeginAnimation(OpacityProperty, null);
            GoldenFx.Opacity = 0;
            GoldenFx.Visibility = Visibility.Collapsed;
            GoldenFxHost.Children.Clear();
        };
        GoldenFx.BeginAnimation(OpacityProperty, fade);
    }

    /// <param name="celebrate">true = acabou de ativar (explosão de confetes); false = só entrou na aba.</param>
    private void ShowGoldenFx(bool celebrate)
    {
        ++_fxId; // invalida qualquer "sumir" que ainda esteja rodando
        var light = Perf.Light;
        var active = GoldenActive;

        GoldenFxHost.Children.Clear();
        GoldenFx.BeginAnimation(OpacityProperty, null);
        GoldenFx.Opacity = 0;
        GoldenFx.Visibility = Visibility.Visible;
        GoldenFx.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };

        // ---- Brilho de fundo que pulsa ----
        var glowScale = new ScaleTransform(1, 1);
        var glow = new Ellipse
        {
            Width = 780,
            Height = 780,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -190, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = glowScale,
            Fill = new RadialGradientBrush(FxGold(0x58), FxGold(0x00))
        };
        var pulse = new DoubleAnimation(0.92, 1.08, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = sine
        };
        glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        GoldenFxHost.Children.Add(glow);

        // ---- Faíscas douradas subindo ----
        var sparks = new Canvas
        {
            Width = 1100,
            Height = 690,
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var sparkCount = light ? 12 : 36;
        for (var i = 0; i < sparkCount; i++)
        {
            var size = 3 + _fxRnd.NextDouble() * 6;
            var drift = new TranslateTransform();
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Opacity = 0,
                Fill = new SolidColorBrush(FxColors[_fxRnd.Next(FxColors.Length)]),
                RenderTransform = drift
            };
            Canvas.SetLeft(dot, _fxRnd.NextDouble() * 1100);
            Canvas.SetTop(dot, 700);

            var life = TimeSpan.FromSeconds(3.5 + _fxRnd.NextDouble() * 3.5);
            var delay = TimeSpan.FromMilliseconds(_fxRnd.Next(0, 2600));

            dot.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(700, -30, life)
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever
            });

            var fade = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = delay,
                Duration = life,
                RepeatBehavior = RepeatBehavior.Forever
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.15)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.8)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            dot.BeginAnimation(OpacityProperty, fade);

            drift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-18, 18, TimeSpan.FromSeconds(2 + _fxRnd.NextDouble() * 2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = sine
                });

            sparks.Children.Add(dot);
        }
        GoldenFxHost.Children.Add(sparks);

        // ---- Conteúdo central ----
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 900
        };

        // Lua dourada com anel girando
        var headScale = new ScaleTransform(0.2, 0.2);
        var headFloat = new TranslateTransform();
        var head = new Grid
        {
            Width = 130,
            Height = 130,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { headScale, headFloat } }
        };

        // Ondas de luz (só na ativação)
        if (celebrate)
        {
            for (var r = 0; r < 3; r++)
            {
                var ringScale = new ScaleTransform(0.3, 0.3);
                var wave = new Ellipse
                {
                    Width = 130,
                    Height = 130,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)),
                    StrokeThickness = 3,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = ringScale
                };
                var begin = TimeSpan.FromMilliseconds(150 + 300 * r);
                var dur = TimeSpan.FromMilliseconds(1500);
                ringScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.3, 4.6, dur) { BeginTime = begin, EasingFunction = easeOut });
                ringScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.3, 4.6, dur) { BeginTime = begin, EasingFunction = easeOut });
                wave.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.9, 0, dur) { BeginTime = begin });
                head.Children.Add(wave);
            }
        }

        var ringRot = new RotateTransform(0);
        var ring = new Ellipse
        {
            Width = 128,
            Height = 128,
            StrokeThickness = 2,
            Stroke = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE9, 0xA6), FxGold(0x10), 45),
            StrokeDashArray = new DoubleCollection { 2, 3 },
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = ringRot
        };
        ringRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14)) { RepeatBehavior = RepeatBehavior.Forever });
        head.Children.Add(ring);

        var moon = new Image
        {
            Source = LoadAsset("golden_moon.png"),
            Width = 104,
            Height = 104,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(moon, BitmapScalingMode.HighQuality);
        head.Children.Add(moon);

        // Confetes (só na ativação)
        if (celebrate)
        {
            var burst = new Canvas
            {
                Width = 0,
                Height = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            var pieces = light ? 30 : 72;
            for (var i = 0; i < pieces; i++)
            {
                var rot = new RotateTransform(0);
                var move = new TranslateTransform();
                var piece = new Rectangle
                {
                    Width = 5 + _fxRnd.NextDouble() * 4,
                    Height = 8 + _fxRnd.NextDouble() * 6,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = new SolidColorBrush(FxColors[_fxRnd.Next(FxColors.Length)]),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new TransformGroup { Children = { rot, move } }
                };

                var angle = _fxRnd.NextDouble() * Math.PI * 2;
                var dist = 180 + _fxRnd.NextDouble() * 360;
                var dx = Math.Cos(angle) * dist;
                var dy = Math.Sin(angle) * dist * 0.8 - 60;
                var dur = TimeSpan.FromMilliseconds(1700 + _fxRnd.Next(0, 1300));
                var delay = TimeSpan.FromMilliseconds(120 + _fxRnd.Next(0, 250));

                move.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, dx, dur) { BeginTime = delay, EasingFunction = easeOut });

                var fall = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = dur };
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromPercent(0.45),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(dy + 260, KeyTime.FromPercent(1),
                    new CubicEase { EasingMode = EasingMode.EaseIn }));
                move.BeginAnimation(TranslateTransform.YProperty, fall);

                rot.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, (_fxRnd.NextDouble() * 2 - 1) * 720, dur) { BeginTime = delay });

                var vanish = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = dur };
                vanish.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
                vanish.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
                vanish.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                piece.Opacity = 0;
                piece.BeginAnimation(OpacityProperty, vanish);

                burst.Children.Add(piece);
            }
            head.Children.Add(burst);
        }

        headScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(750)) { EasingFunction = back });
        headScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(750)) { EasingFunction = back });
        headFloat.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(2))
            {
                BeginTime = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = sine
            });
        stack.Children.Add(head);

        // Título com brilho que corre
        var shimmer = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var stopMid = new GradientStop(Color.FromRgb(0xFF, 0xC9, 0x4A), 0.5);
        shimmer.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF1, 0xC4), 0.0));
        shimmer.GradientStops.Add(stopMid);
        shimmer.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x9A, 0x1F), 1.0));
        stopMid.BeginAnimation(GradientStop.OffsetProperty,
            new DoubleAnimation(0.15, 0.85, TimeSpan.FromSeconds(1.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });

        var titleScale = new ScaleTransform(celebrate ? 0.3 : 0.7, celebrate ? 0.3 : 0.7);
        var title = new TextBlock
        {
            Text = celebrate ? "GOLDEN MOON ATIVADO!" : "GOLDEN MOON",
            FontSize = celebrate ? 32 : 34,
            FontWeight = FontWeights.Bold,
            Foreground = shimmer,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = titleScale
        };
        var titleBegin = TimeSpan.FromMilliseconds(350);
        title.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { BeginTime = titleBegin });
        titleScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(titleScale.ScaleX, 1, TimeSpan.FromMilliseconds(650)) { BeginTime = titleBegin, EasingFunction = back });
        titleScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(titleScale.ScaleY, 1, TimeSpan.FromMilliseconds(650)) { BeginTime = titleBegin, EasingFunction = back });
        stack.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = celebrate
                ? "Obrigado por apoiar o Moonlight Launcher 💛  Tudo isto já está liberado:"
                : active ? "Você é assinante ✔  Tudo isto já está liberado:" : "Tudo o que você libera ao assinar:",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xDC, 0xBE)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0
        };
        subtitle.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(650) });
        stack.Children.Add(subtitle);

        // Benefícios entrando um por um
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 0) };
        for (var i = 0; i < GoldBenefits.Length; i++)
            grid.Children.Add(MakeFxCard(GoldBenefits[i], i, active, easeOut));
        stack.Children.Add(grid);

        var hint = new TextBlock
        {
            Text = active ? "Clique em qualquer lugar para continuar" : "🔒  Cole seu código de ativação na aba Golden Moon  ·  clique para continuar",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xB2, 0xD6)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
            Opacity = 0
        };
        hint.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 0.95, TimeSpan.FromMilliseconds(500))
            {
                BeginTime = TimeSpan.FromMilliseconds(950 + 260 * GoldBenefits.Length + 200)
            });
        stack.Children.Add(hint);

        GoldenFxHost.Children.Add(stack);

        // ---- Clarão dourado na ativação ----
        if (celebrate)
        {
            var flash = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xBB)),
                IsHitTestVisible = false,
                Opacity = 0.95
            };
            flash.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = easeOut });
            GoldenFxHost.Children.Add(flash);
        }

        // Some sozinha depois de um tempo (ou ao clicar).
        _fxTimer ??= new DispatcherTimer();
        _fxTimer.Stop();
        _fxTimer.Interval = TimeSpan.FromSeconds(celebrate ? 8 : 9);
        _fxTimer.Tick -= FxTimerTick;
        _fxTimer.Tick += FxTimerTick;
        _fxTimer.Start();
    }

    private Border MakeFxCard(GoldBenefit b, int index, bool active, IEasingFunction ease)
    {
        var sc = new ScaleTransform(0.94, 0.94);
        var tt = new TranslateTransform(0, 26);
        var edge = new SolidColorBrush(FxGold(0x70));

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(6),
            BorderThickness = new Thickness(1),
            BorderBrush = edge,
            Background = new LinearGradientBrush(FxGold(0x28), Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 45),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { sc, tt } }
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = b.Icon,
            FontSize = 28,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = b.Title,
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0xA6))
        });
        if (active)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = "  ✔ liberado",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xE3, 0xA8))
            });
        }
        info.Children.Add(titleRow);
        info.Children.Add(new TextBlock
        {
            Text = b.Desc,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xF2))
        });
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        card.Child = row;

        var begin = TimeSpan.FromMilliseconds(950 + 260 * index);
        card.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340)) { BeginTime = begin });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = ease });

        // A borda dá um lampejo branco quando o cartão aparece.
        edge.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Colors.White, FxGold(0x70), TimeSpan.FromMilliseconds(750)) { BeginTime = begin });

        return card;
    }
}
