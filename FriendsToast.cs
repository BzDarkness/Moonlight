using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MoonlightLauncher;

// ====================================================================================
//  Som de notificação (sino suave gerado na hora, sem arquivo) e a mini janelinha de mensagem.
// ====================================================================================
public static class FriendsSound
{
    private static byte[]? _wav;

    public static void Play()
    {
        try
        {
            _wav ??= BuildChime();
            var player = new SoundPlayer(new MemoryStream(_wav));
            player.Play(); // toca em segundo plano, sem travar a tela
        }
        catch (Exception ex) { LauncherLog.Write("Não consegui tocar o som de notificação", ex); }
    }

    // Dois sininhos subindo (sol → dó), com harmônicos e queda suave: agradável e curtinho.
    private static byte[] BuildChime()
    {
        const int rate = 44100;
        const double dur = 1.0;
        var n = (int)(rate * dur);
        var samples = new double[n];

        void Bell(double start, double freq, double gain)
        {
            var s0 = (int)(start * rate);
            for (var i = s0; i < n; i++)
            {
                var t = (i - s0) / (double)rate;
                var env = Math.Min(1.0, t / 0.006) * Math.Exp(-t / 0.20);
                var w = Math.Sin(2 * Math.PI * freq * t)
                        + 0.38 * Math.Sin(2 * Math.PI * freq * 2 * t) * Math.Exp(-t / 0.12)
                        + 0.14 * Math.Sin(2 * Math.PI * freq * 3 * t) * Math.Exp(-t / 0.08)
                        + 0.10 * Math.Sin(2 * Math.PI * freq * 2.76 * t) * Math.Exp(-t / 0.06);
                samples[i] += w * env * gain;
            }
        }

        Bell(0.00, 784.0, 0.30);    // sol
        Bell(0.11, 1046.5, 0.34);   // dó

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var dataLen = n * 2;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + dataLen); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(dataLen);
        foreach (var s in samples)
            bw.Write((short)(Math.Clamp(s, -1, 1) * 30000));
        return ms.ToArray();
    }
}

/// <summary>
/// A janelinha que aparece no canto da tela quando um amigo fala com você. Fica por cima de tudo
/// (até do Minecraft), não rouba o foco do jogo e some sozinha depois de alguns segundos.
/// </summary>
public sealed class ChatToastWindow : Window
{
    private readonly Image _face = new() { Width = 46, Height = 46 };
    private readonly Border _faceBox = new();
    private readonly TextBlock _name = new();
    private readonly TextBlock _text = new();
    private readonly TextBlock _more = new();
    private readonly Border _replyBox = new();
    private readonly TextBox _reply = new();
    private readonly DispatcherTimer _timer = new();
    private readonly TranslateTransform _slide = new(60, 0);
    private string _friendId = "";
    private int _count;
    private bool _closing;

    public event Action<string, string>? ReplySent;   // (id do amigo, texto)
    public event Action<string>? OpenRequested;        // (id do amigo)

    public ChatToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;      // não tira o foco do Minecraft
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 372;
        Focusable = false;

        // cartão
        var card = new Border { CornerRadius = new CornerRadius(18), Margin = new Thickness(14), Padding = new Thickness(14, 12, 12, 12),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, RenderTransform = _slide,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black } };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "AccB1_80");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _faceBox.Width = 52; _faceBox.Height = 52; _faceBox.CornerRadius = new CornerRadius(14);
        _faceBox.Margin = new Thickness(0, 0, 12, 0); _faceBox.VerticalAlignment = VerticalAlignment.Top;
        _faceBox.BorderThickness = new Thickness(2);
        _faceBox.SetResourceReference(Border.BorderBrushProperty, "AccB1_FF");
        _faceBox.Child = _face;
        RenderOptions.SetBitmapScalingMode(_face, BitmapScalingMode.NearestNeighbor);
        _face.Margin = new Thickness(1);
        _faceBox.ClipToBounds = true;
        grid.Children.Add(_faceBox);

        var right = new StackPanel();
        Grid.SetColumn(right, 1);

        var top = new DockPanel();
        var close = new TextBlock { Text = "✕", FontSize = 12, Cursor = Cursors.Hand, Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Dismiss(); };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);

        _name.FontWeight = FontWeights.Bold; _name.FontSize = 14; _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        top.Children.Add(_name);
        right.Children.Add(top);

        _text.TextWrapping = TextWrapping.Wrap; _text.MaxHeight = 62; _text.TextTrimming = TextTrimming.CharacterEllipsis;
        _text.FontSize = 13; _text.Margin = new Thickness(0, 3, 0, 0);
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        right.Children.Add(_text);

        _more.FontSize = 11; _more.Margin = new Thickness(0, 4, 0, 0); _more.Visibility = Visibility.Collapsed;
        _more.SetResourceReference(TextBlock.ForegroundProperty, "AccB1_FF");
        right.Children.Add(_more);

        // resposta rápida
        _replyBox.CornerRadius = new CornerRadius(10); _replyBox.BorderThickness = new Thickness(1);
        _replyBox.Margin = new Thickness(0, 9, 0, 0); _replyBox.Padding = new Thickness(10, 5, 10, 5);
        _replyBox.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        _replyBox.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        _reply.BorderThickness = new Thickness(0); _reply.Background = Brushes.Transparent; _reply.FontSize = 12.5;
        _reply.MaxLength = 400;
        _reply.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _reply.SetResourceReference(TextBox.CaretBrushProperty, "AccB1_FF");
        var hint = new TextBlock { Text = "Responder…  (Enter envia)", FontSize = 12.5, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var rg = new Grid();
        rg.Children.Add(hint); rg.Children.Add(_reply);
        _replyBox.Child = rg;
        _reply.TextChanged += (_, _) => hint.Visibility = _reply.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _reply.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var t = _reply.Text.Trim();
            if (t.Length == 0) return;
            ReplySent?.Invoke(_friendId, t);
            _reply.Clear();
            _more.Text = "✔ Enviado"; _more.Visibility = Visibility.Visible;
            Restart(3);
            e.Handled = true;
        };
        _reply.GotKeyboardFocus += (_, _) => _timer.Stop();
        right.Children.Add(_replyBox);

        grid.Children.Add(right);
        card.Child = grid;
        card.MouseLeftButtonUp += (_, _) => { OpenRequested?.Invoke(_friendId); Dismiss(); };
        Content = card;

        _timer.Tick += (_, _) => { _timer.Stop(); if (!IsMouseOver && !_reply.IsKeyboardFocused) Dismiss(); else Restart(3); };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { if (!_reply.IsKeyboardFocused) Restart(3); };
        SizeChanged += (_, _) => PlaceBottomRight();
    }

    /// <summary>Mostra (ou atualiza) a janelinha com uma mensagem nova.</summary>
    public void ShowMessage(string friendId, string name, System.Windows.Media.Imaging.BitmapSource face, string text,
        bool allowReply = true)
    {
        _closing = false;
        if (_friendId == friendId && IsVisible) _count++; else _count = 1;
        _friendId = friendId;
        _name.Text = name;
        _face.Source = face;
        _text.Text = text;
        _more.Text = _count > 1 ? $"+ {_count - 1} mensagem(ns) nova(s)" : "";
        _more.Visibility = _count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _replyBox.Visibility = allowReply ? Visibility.Visible : Visibility.Collapsed;

        var wasVisible = IsVisible;
        Opacity = 1;
        if (!wasVisible) { Show(); }
        PlaceBottomRight();

        if (!Perf.Light)
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(wasVisible ? 14 : 60, 0, TimeSpan.FromMilliseconds(320))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
            if (!wasVisible)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        else { _slide.X = 0; }

        Restart(7);
    }

    private void Restart(int seconds)
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    private void PlaceBottomRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 6;
        Top = wa.Bottom - ActualHeight - 6;
    }

    public void Dismiss()
    {
        if (_closing || !IsVisible) return;
        _closing = true;
        _timer.Stop();
        _count = 0;

        if (Perf.Light) { Hide(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (_closing) Hide(); };
        BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 50, TimeSpan.FromMilliseconds(220)));
    }
}
