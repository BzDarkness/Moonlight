using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SPath = System.Windows.Shapes.Path;

namespace MoonlightLauncher;

// Abertura do launcher: a janela aparece toda branca, balançando e tremendo na tela; quando ela abre,
// os menus entram em cascata e cada um solta brilhinhos (estrelinhas) na hora em que aparece.
public partial class MainWindow
{
    // ---------- Tremor e balanço da janela ----------

    private EventHandler? _introShakeHandler;
    private bool _introShaking;
    private bool _introFinal;                                   // true = última sacudida, antes de abrir
    private const double IntroStartAmp = 7;                      // força do tremor logo que a janela aparece
    private const double IntroFinalAmp = 34;                     // força do tremor um instante antes de abrir
    private double _introAmp = IntroStartAmp, _introTargetAmp = IntroStartAmp;
    private double _introBaseLeft, _introBaseTop;
    private readonly Random _introRnd = new();

    private void StartIntroShake()
    {
        // No modo leve a janela não balança (mexer a janela a cada quadro pesa em PC fraco).
        if (Perf.Light) return;
        if (_introShaking || WindowState != WindowState.Normal || double.IsNaN(Left) || double.IsNaN(Top)) return;

        _introBaseLeft = Left;
        _introBaseTop = Top;
        _introShaking = true;
        var sw = Stopwatch.StartNew();

        _introShakeHandler = (_, _) =>
        {
            var t = sw.Elapsed.TotalSeconds;

            // A força vai subindo sozinha enquanto a abertura dura; no fim ela dispara.
            if (!_introFinal) _introTargetAmp = IntroStartAmp + Math.Min(11, t * 6);

            _introAmp += (_introTargetAmp - _introAmp) * 0.18;
            var sway = Math.Sin(t * 11.0) * _introAmp * 1.6;                       // balança de um lado para o outro
            var bob = Math.Sin(t * 8.3) * _introAmp * 0.7;                         // sobe e desce
            var jitter = _introAmp * 0.9;                                          // tremedeira rápida
            Left = _introBaseLeft + sway + (_introRnd.NextDouble() * 2 - 1) * jitter;
            Top = _introBaseTop + bob + (_introRnd.NextDouble() * 2 - 1) * jitter;
        };
        CompositionTarget.Rendering += _introShakeHandler;
        Closed += (_, _) => StopIntroShake(false);
    }

    private void StopIntroShake(bool restore = true)
    {
        if (!_introShaking) return;
        _introShaking = false;
        if (_introShakeHandler != null) CompositionTarget.Rendering -= _introShakeHandler;
        _introShakeHandler = null;
        if (restore) { Left = _introBaseLeft; Top = _introBaseTop; }
    }

    // ---------- Brilhinhos ----------

    private sealed class Spark
    {
        public required SPath Shape;
        public required ScaleTransform Scale;
        public required RotateTransform Rot;
        public double X, Y, Vx, Vy, Age, Life, Size, Spin, Phase;
    }

    private const int MaxSparks = 160;
    private readonly List<Spark> _sparks = new();
    private readonly Stopwatch _sparkClock = new();
    private long _sparkLast;
    private bool _sparkRunning;

    private static readonly Geometry StarGeo = MakeStar();
    private static readonly Brush[] SparkBrushes =
    {
        Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)), Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        Frozen(Color.FromRgb(0xFF, 0xE9, 0xA6)), Frozen(Color.FromRgb(0xFF, 0xC9, 0x4A)),
        Frozen(Color.FromRgb(0xCF, 0xC4, 0xFF))
    };

    private static Geometry MakeStar()
    {
        var g = Geometry.Parse("M0,-1 L0.2,-0.2 L1,0 L0.2,0.2 L0,1 L-0.2,0.2 L-1,0 L-0.2,-0.2 Z");
        g.Freeze();
        return g;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private async void SparkleBurstAfter(FrameworkElement el, int delayMs, int count)
    {
        await Task.Delay(delayMs);
        SparkleBurst(el, count);
    }

    // Solta estrelinhas em cima de um menu (no lugar em que ele está aparecendo).
    private void SparkleBurst(FrameworkElement el, int count)
    {
        try
        {
            if (!el.IsVisible || el.ActualWidth < 2 || el.ActualHeight < 2 || IntroSparkles.ActualWidth < 2) return;
            if (Perf.Light) return;   // modo leve: sem brilhinhos
            count = Math.Min(count, MaxSparks - _sparks.Count);
            if (count <= 0) return;

            var origin = el.TransformToVisual(IntroSparkles).Transform(new Point(0, 0));
            for (var i = 0; i < count; i++)
            {
                var x = origin.X + _introRnd.NextDouble() * el.ActualWidth;
                var y = origin.Y + _introRnd.NextDouble() * el.ActualHeight;
                var ang = _introRnd.NextDouble() * Math.PI * 2;
                var speed = 25 + _introRnd.NextDouble() * 65;

                var scale = new ScaleTransform(0.01, 0.01);
                var rot = new RotateTransform(_introRnd.NextDouble() * 90);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(rot);

                var shape = new SPath
                {
                    Data = StarGeo,
                    Fill = SparkBrushes[_introRnd.Next(SparkBrushes.Length)],
                    IsHitTestVisible = false,
                    RenderTransform = group
                };
                Canvas.SetLeft(shape, x);
                Canvas.SetTop(shape, y);
                IntroSparkles.Children.Add(shape);

                _sparks.Add(new Spark
                {
                    Shape = shape, Scale = scale, Rot = rot,
                    X = x, Y = y,
                    Vx = Math.Cos(ang) * speed, Vy = Math.Sin(ang) * speed - 14,
                    Life = 0.7 + _introRnd.NextDouble() * 0.6,
                    Size = 7 + _introRnd.NextDouble() * 9,
                    Spin = (_introRnd.NextDouble() * 2 - 1) * 160,
                    Phase = _introRnd.NextDouble() * 6.28
                });
            }

            if (!_sparkRunning)
            {
                _sparkRunning = true;
                _sparkClock.Restart();
                _sparkLast = 0;
                CompositionTarget.Rendering += SparkTick;
            }
        }
        catch (Exception ex) { LauncherLog.Write("Os brilhinhos da abertura falharam", ex); }
    }

    private void SparkTick(object? sender, EventArgs e)
    {
        var now = _sparkClock.ElapsedMilliseconds;
        var dt = Math.Min(0.05, (now - _sparkLast) / 1000.0);
        _sparkLast = now;

        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var k = _sparks[i];
            k.Age += dt;
            var p = k.Age / k.Life;
            if (p >= 1)
            {
                IntroSparkles.Children.Remove(k.Shape);
                _sparks.RemoveAt(i);
                continue;
            }

            k.X += k.Vx * dt;
            k.Y += k.Vy * dt;
            k.Vx *= 0.985;
            k.Vy *= 0.985;

            var twinkle = 0.85 + 0.15 * Math.Sin(k.Age * 28 + k.Phase);
            var sc = Math.Max(0.01, k.Size / 2 * Math.Sin(Math.PI * Math.Sqrt(p)) * twinkle); // nasce rápido e some devagar
            k.Scale.ScaleX = sc;
            k.Scale.ScaleY = sc;
            k.Rot.Angle += k.Spin * dt;
            Canvas.SetLeft(k.Shape, k.X);
            Canvas.SetTop(k.Shape, k.Y);
        }

        if (_sparks.Count == 0)
        {
            CompositionTarget.Rendering -= SparkTick;
            _sparkRunning = false;
            _sparkClock.Stop();
        }
    }
}
