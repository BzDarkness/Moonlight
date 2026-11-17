using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sh = System.Windows.Shapes;

namespace MoonlightLauncher;

/// <summary>
/// Aviso de "conquista desbloqueada": painel escuro no alto da tela com uma linha reta em RGB
/// (as cores correm da esquerda para a direita) e um brilho branco passando por cima, com som de item raro.
/// Leve de propósito: 2 linhas, 30 quadros, some sozinho e se remove da tela.
/// </summary>
public partial class MainWindow
{
    private Grid? _achvBanner;
    private int _achvId;

    private static readonly Color[] AchvColors =
    {
        Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x91, 0x00), Color.FromRgb(0xFF, 0xEA, 0x00),
        Color.FromRgb(0x00, 0xE6, 0x76), Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x29, 0x79, 0xFF),
        Color.FromRgb(0xD5, 0x00, 0xF9), Color.FromRgb(0xFF, 0x17, 0x44)   // termina na cor do começo = laço perfeito
    };

    private static Brush AchvBr(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // Uma linha reta: faixa RGB fina + halo suave + brilho branco que varre da esquerda para a direita.
    private static Grid AchvMakeLine(Brush rainbow, double width, out ScaleTransform grow)
    {
        grow = new ScaleTransform(0, 1);
        var host = new Grid { Height = 18, ClipToBounds = true, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = grow };

        var soft = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        soft.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        soft.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.5));
        soft.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        soft.Freeze();

        host.Children.Add(new Sh.Rectangle { Height = 18, Fill = rainbow, Opacity = 0.4, OpacityMask = soft, IsHitTestVisible = false });
        host.Children.Add(new Sh.Rectangle { Height = 4, Fill = rainbow, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });

        var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        shine.Freeze();

        var shineMove = new TranslateTransform(-160, 0);
        host.Children.Add(new Sh.Rectangle
        {
            Width = 160, Height = 4, Fill = shine, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, RenderTransform = shineMove, IsHitTestVisible = false
        });
        var sweep = new DoubleAnimation(-160, width, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(sweep, 30);
        shineMove.BeginAnimation(TranslateTransform.XProperty, sweep);
        return host;
    }

    /// <summary>Mostra a conquista no alto da janela (com som). Some sozinha depois de uns 6 segundos.</summary>
    private async void ShowAchievement(string title, string name, string hint)
    {
        try
        {
            if (WindowRoot.Child is not Grid root) return;
            var id = ++_achvId;
            if (_achvBanner != null) { root.Children.Remove(_achvBanner); _achvBanner = null; }

            const double W = 500;

            // Pincel RGB que corre para a direita (o gradiente se repete e é deslocado um "período" por volta).
            var rainbow = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0.5, 0), SpreadMethod = GradientSpreadMethod.Repeat
            };
            for (var i = 0; i < AchvColors.Length; i++)
                rainbow.GradientStops.Add(new GradientStop(AchvColors[i], i / (double)(AchvColors.Length - 1)));
            var shift = new TranslateTransform();
            rainbow.RelativeTransform = shift;
            var flow = new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(flow, 30);
            shift.BeginAnimation(TranslateTransform.XProperty, flow);

            var topLine = AchvMakeLine(rainbow, W, out var topGrow);
            var botLine = AchvMakeLine(rainbow, W, out var botGrow);

            var texts = new StackPanel { Margin = new Thickness(22, 10, 22, 10) };
            texts.Children.Add(new TextBlock
            {
                Text = "🏆  " + title, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = AchvBr("#FFFFD54F")
            });
            texts.Children.Add(new TextBlock
            {
                Text = name, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0)
            });
            if (!string.IsNullOrWhiteSpace(hint))
                texts.Children.Add(new TextBlock
                {
                    Text = hint, FontSize = 12, Foreground = AchvBr("#FFC9CEF5"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
                });

            var stack = new StackPanel();
            stack.Children.Add(topLine);
            stack.Children.Add(texts);
            stack.Children.Add(botLine);

            var move = new TranslateTransform(0, -70);
            var banner = new Grid
            {
                Width = W, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 0, 0), IsHitTestVisible = false, Opacity = 0, RenderTransform = move
            };
            banner.Children.Add(new Border { Background = AchvBr("#F0090920"), CornerRadius = new CornerRadius(6) });
            banner.Children.Add(stack);
            Grid.SetRowSpan(banner, 99);
            Grid.SetColumnSpan(banner, 99);
            Panel.SetZIndex(banner, 9999);
            root.Children.Add(banner);
            _achvBanner = banner;

            Ps1.Play("achievement.wav");

            // Entrada: o painel desce com "quique", as linhas se abrem do meio para as pontas.
            var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-70, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = back });
            var open = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650))
            {
                BeginTime = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            topGrow.BeginAnimation(ScaleTransform.ScaleXProperty, open);
            botGrow.BeginAnimation(ScaleTransform.ScaleXProperty, open);

            await Task.Delay(5800);
            if (id != _achvId) return; // veio outra conquista e já assumiu a tela

            // Saída: sobe e some.
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
            fade.Completed += (_, _) =>
            {
                root.Children.Remove(banner);
                if (ReferenceEquals(_achvBanner, banner)) _achvBanner = null;
            };
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -70, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            banner.BeginAnimation(OpacityProperty, fade);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Falha ao mostrar a conquista", ex);
        }
    }
}
