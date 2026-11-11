using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoonlightLauncher;

/// <summary>
/// Animador simples que roda a cada quadro desenhado (serve para animar tamanhos de linhas/colunas,
/// o que o Storyboard normal não faz bem). Só existe um por "assunto": começar de novo cancela o anterior.
/// </summary>
public sealed class FrameAnim
{
    private EventHandler? _handler;

    public bool Running => _handler != null;

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    public static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>step recebe o tempo bruto (0..1). Quando termina, chama done.</summary>
    public void Run(double ms, Action<double> step, Action? done = null)
    {
        Stop();
        var sw = Stopwatch.StartNew();
        _handler = (_, _) =>
        {
            var t = Math.Min(1, sw.Elapsed.TotalMilliseconds / Math.Max(1, ms));
            step(t);
            if (t >= 1)
            {
                Stop();
                done?.Invoke();
            }
        };
        CompositionTarget.Rendering += _handler;
    }

    public void Stop()
    {
        if (_handler == null) return;
        CompositionTarget.Rendering -= _handler;
        _handler = null;
    }
}

/// <summary>
/// Caixa que mostra/esconde o conteúdo "recolhendo" o espaço de verdade (a lista de baixo sobe junto),
/// mas sem amassar o conteúdo: ele desliza para cima e some, como a barra de um site ao rolar a página.
/// Progress: 1 = totalmente visível, 0 = escondida.
/// </summary>
public sealed class RevealHost : Decorator
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RevealHost),
        new FrameworkPropertyMetadata(1.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            OnProgressChanged, CoerceProgress));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private static object CoerceProgress(DependencyObject d, object value) => Math.Clamp((double)value, 0.0, 1.0);

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (RevealHost)d;
        var p = (double)e.NewValue;

        // Só corta o que passa da borda enquanto está animando (assim sombras/brilhos não são cortados quando parado).
        host.ClipToBounds = p < 0.999;

        if (host.Child != null)
            host.Child.Opacity = p >= 0.999 ? 1 : Math.Pow(p, 1.7);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null) return default;
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        var ds = Child.DesiredSize;
        return new Size(ds.Width, ds.Height * Progress);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child == null) return arrangeSize;
        var h = Child.DesiredSize.Height;
        Child.Arrange(new Rect(0, -(1 - Progress) * h, arrangeSize.Width, h));
        return arrangeSize;
    }
}
