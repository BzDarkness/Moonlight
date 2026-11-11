using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MoonlightLauncher;

/// <summary>
/// O céu do launcher. Para ser leve:
///  • as estrelas são desenhadas em poucas camadas (uma forma só com dezenas de pontinhos),
///    e quem pisca é a camada inteira, não cada estrela;
///  • todas as animações usam "relógios" que dá para pausar (minimizado / jogando = zero trabalho);
///  • no modo leve só sobra uma camada fixa de estrelas.
/// </summary>
internal sealed class SpaceBackground
{
    private readonly Canvas _far;
    private readonly Canvas _near;
    private readonly Canvas _sparkles;
    private readonly TranslateTransform[] _blobs;
    private readonly TranslateTransform _moon;
    private readonly TranslateTransform _logo;
    private readonly (Border El, TranslateTransform T)[] _shoots;

    private readonly TranslateTransform _farShift = new();
    private readonly TranslateTransform _nearShift = new();

    private readonly List<(IAnimatable Target, DependencyProperty Prop, AnimationClock Clock)> _applied = new();

    public SpaceBackground(Canvas far, Canvas near, Canvas sparkles, TranslateTransform[] blobs,
        TranslateTransform moon, TranslateTransform logo, (Border, TranslateTransform)[] shoots)
    {
        _far = far;
        _near = near;
        _sparkles = sparkles;
        _blobs = blobs;
        _moon = moon;
        _logo = logo;
        _shoots = shoots;

        _far.RenderTransform = _farShift;
        _near.RenderTransform = _nearShift;
    }

    // ---------- Montagem ----------

    public void Build(bool light)
    {
        Clear();

        var w = _far.ActualWidth > 50 ? _far.ActualWidth : 1180;
        var h = _far.ActualHeight > 50 ? _far.ActualHeight : 690;
        var rnd = new Random(7);

        var white = Frozen(Color.FromRgb(255, 255, 255));
        var lavender = Frozen(Color.FromRgb(214, 205, 255));
        var ice = Frozen(Color.FromRgb(200, 235, 255));

        // Camada base: estrelinhas fixas.
        _far.Children.Add(Layer(rnd, light ? 46 : 70, w, h, 0.5, 0.95, white, 0.42));

        foreach (var s in _shoots) s.El.Visibility = light ? Visibility.Collapsed : Visibility.Visible;

        if (light) return; // modo leve: só o céu parado

        // Camadas que piscam (3 animações no total, não uma por estrela).
        var a = Layer(rnd, 22, w, h, 0.9, 1.4, lavender, 0.9);
        var b = Layer(rnd, 18, w, h, 0.9, 1.5, ice, 0.9);
        var c = Layer(rnd, 14, w, h, 1.5, 2.2, white, 0.9);
        _far.Children.Add(a);
        _far.Children.Add(b);
        _near.Children.Add(c);
        Pulse(a, 0.25, 0.95, 2.6, 0);
        Pulse(b, 0.2, 0.9, 3.4, 1.1);
        Pulse(c, 0.35, 1.0, 4.2, 0.6);

        // Estrelas de 4 pontas que brilham de tempos em tempos.
        var star = Geometry.Parse("M7,0 L8.4,5.6 L14,7 L8.4,8.4 L7,14 L5.6,8.4 L0,7 L5.6,5.6 Z");
        star.Freeze();
        for (var i = 0; i < 6; i++)
        {
            var sp = new System.Windows.Shapes.Path
            {
                Data = star,
                Fill = white,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.4, 0.4)
            };
            Canvas.SetLeft(sp, 40 + rnd.NextDouble() * (w - 80));
            Canvas.SetTop(sp, 30 + rnd.NextDouble() * (h - 60));
            _sparkles.Children.Add(sp);

            var delay = i * 0.9 + rnd.NextDouble();
            var life = 2.4 + rnd.NextDouble() * 1.6;
            Apply(sp, UIElement.OpacityProperty, Loop(0, 1, life, delay));
            Apply((ScaleTransform)sp.RenderTransform, ScaleTransform.ScaleXProperty, Loop(0.4, 1.2, life, delay));
            Apply((ScaleTransform)sp.RenderTransform, ScaleTransform.ScaleYProperty, Loop(0.4, 1.2, life, delay));
        }

        // Auroras à deriva, lua grande e logo flutuando.
        Drift(_blobs[0], 260, -120, 17, 13);
        Drift(_blobs[1], -300, 160, 19, 15);
        Drift(_blobs[2], -180, -100, 21, 16);
        Apply(_moon, TranslateTransform.YProperty, Loop(-10, 14, 7, 0, true));
        Apply(_logo, TranslateTransform.YProperty, Loop(-2.5, 2.5, 3.2, 0, true));

        // Estrelas cadentes ocasionais.
        Shoot(_shoots[0].El, _shoots[0].T, 11, 8, 330, 175);
        if (_shoots.Length > 1) Shoot(_shoots[1].El, _shoots[1].T, 17, 13, 260, 138);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // Uma forma só com várias estrelas (EllipseGeometry), congelada: barata de desenhar.
    private static System.Windows.Shapes.Path Layer(Random rnd, int count, double w, double h, double minR, double maxR, Brush fill, double opacity)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var i = 0; i < count; i++)
        {
            var r = minR + rnd.NextDouble() * (maxR - minR);
            g.Children.Add(new EllipseGeometry(new Point(rnd.NextDouble() * (w + 40) - 20, rnd.NextDouble() * (h + 40) - 20), r, r));
        }
        g.Freeze();
        return new System.Windows.Shapes.Path { Data = g, Fill = fill, Opacity = opacity, IsHitTestVisible = false };
    }

    // ---------- Animações (com relógio, para poder pausar) ----------

    private void Apply(IAnimatable target, DependencyProperty prop, AnimationTimeline anim)
    {
        var clock = anim.CreateClock();
        target.ApplyAnimationClock(prop, clock);
        _applied.Add((target, prop, clock));
    }

    private static DoubleAnimation Loop(double from, double to, double seconds, double delay, bool smooth = true)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delay)
        };
        if (smooth) a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        Timeline.SetDesiredFrameRate(a, 30);
        return a;
    }

    private void Pulse(UIElement el, double from, double to, double seconds, double delay) =>
        Apply(el, UIElement.OpacityProperty, Loop(from, to, seconds, delay));

    private void Drift(TranslateTransform t, double dx, double dy, double sx, double sy)
    {
        Apply(t, TranslateTransform.XProperty, Loop(0, dx, sx, 0));
        Apply(t, TranslateTransform.YProperty, Loop(0, dy, sy, 0));
    }

    private void Shoot(Border el, TranslateTransform t, double period, double startAt, double dx, double dy)
    {
        var total = TimeSpan.FromSeconds(period);
        var fly = startAt + 0.9;

        DoubleAnimationUsingKeyFrames Frames(params (double Time, double Value, bool Discrete)[] keys)
        {
            var k = new DoubleAnimationUsingKeyFrames { Duration = total, RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (time, value, discrete) in keys)
            {
                var kt = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(time));
                k.KeyFrames.Add(discrete ? new DiscreteDoubleKeyFrame(value, kt) : new LinearDoubleKeyFrame(value, kt));
            }
            Timeline.SetDesiredFrameRate(k, 30);
            return k;
        }

        Apply(el, UIElement.OpacityProperty, Frames((0, 0, true), (startAt, 0.95, true), (fly, 0, false)));
        Apply(t, TranslateTransform.XProperty, Frames((0, 0, true), (startAt, 0, true), (fly, dx, false)));
        Apply(t, TranslateTransform.YProperty, Frames((0, 0, true), (startAt, 0, true), (fly, dy, false)));
    }

    // ---------- Controle ----------

    /// <summary>Efeito de profundidade: as camadas andam um pouco ao contrário do mouse (x e y entre -1 e 1).</summary>
    public void SetParallax(double nx, double ny)
    {
        _farShift.X = -nx * 6;
        _farShift.Y = -ny * 4;
        _nearShift.X = -nx * 15;
        _nearShift.Y = -ny * 10;
    }

    public void Pause()
    {
        foreach (var (_, _, clock) in _applied) clock.Controller?.Pause();
    }

    public void Resume()
    {
        foreach (var (_, _, clock) in _applied) clock.Controller?.Resume();
    }

    private void Clear()
    {
        foreach (var (target, prop, _) in _applied)
            target.ApplyAnimationClock(prop, null);
        _applied.Clear();

        _far.Children.Clear();
        _near.Children.Clear();
        _sparkles.Children.Clear();
        _farShift.X = _farShift.Y = _nearShift.X = _nearShift.Y = 0;
    }
}
