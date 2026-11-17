using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MoonlightLauncher;

public enum CallPhase { None, Outgoing, Incoming, Active }

/// <summary>Tudo que o painel precisa saber para se desenhar (o painel não tem lógica de chamada).</summary>
public sealed record CallView(
    CallPhase Phase, string Status, bool MeMuted, bool MeDeaf, bool PeerMuted, bool PeerDeaf,
    bool MeSpeaking, bool PeerSpeaking, bool Sharing, bool PeerSharing, bool Watching, bool HasFrame,
    bool ShareAudioFailed, double Pulse, string ShareInfo = "");

internal static class CallUi
{
    public static SolidColorBrush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    private static readonly Dictionary<string, Style> Cache = new();

    /// <summary>Botão redondo de chamada. toggle=true: marcado = fundo claro (e, com slash, uma risca vermelha em cima do ícone).</summary>
    public static Style Round(bool toggle, string bg, string hover, int size, bool slash = false, string checkedBg = "#F2F2F5")
    {
        var key = $"{toggle}|{bg}|{hover}|{size}|{slash}|{checkedBg}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var type = toggle ? "ToggleButton" : "Button";
        var slashXaml = slash
            ? "<Path x:Name=\"Slash\" Data=\"M 0,22 L 22,0\" Stroke=\"#FF4D5E\" StrokeThickness=\"3\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Visibility=\"Collapsed\" IsHitTestVisible=\"False\"/>"
            : "";
        var checkedTrigger = toggle
            ? "<Trigger Property=\"IsChecked\" Value=\"True\"><Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + checkedBg + "\"/>"
              + (slash ? "<Setter TargetName=\"Slash\" Property=\"Visibility\" Value=\"Visible\"/>" : "") + "</Trigger>"
            : "";

        var xaml =
            "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"" + type + "\">" +
            "<Setter Property=\"Cursor\" Value=\"Hand\"/><Setter Property=\"Foreground\" Value=\"White\"/>" +
            "<Setter Property=\"FontSize\" Value=\"" + (size * 0.4).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"/>" +
            "<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"" + type + "\">" +
            "<Grid><Border x:Name=\"Bd\" CornerRadius=\"" + (size / 2) + "\" Background=\"" + bg + "\"/>" +
            "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" + slashXaml + "</Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + hover + "\"/></Trigger>" +
            checkedTrigger +
            "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.75\"/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";

        var style = (Style)XamlReader.Parse(xaml);
        Cache[key] = style;
        return style;
    }

    public static Button RoundButton(string glyph, string bg, string hover, int size, string tip)
    {
        var b = new Button { Content = glyph, Width = size, Height = size, ToolTip = tip, Margin = new Thickness(7, 0, 7, 0),
            Style = Round(false, bg, hover, size) };
        return b;
    }

    public static ToggleButton RoundToggle(string glyph, int size, string tip, bool slash, string bg = "#33FFFFFF", string hover = "#4DFFFFFF", string checkedBg = "#F2F2F5")
    {
        var b = new ToggleButton { Content = glyph, Width = size, Height = size, ToolTip = tip, Margin = new Thickness(7, 0, 7, 0),
            Style = Round(true, bg, hover, size, slash, checkedBg) };
        return b;
    }
}

// ------------------------------------------------------------------------------------
//  Uma pessoa na chamada: rosto redondo da skin, anel verde quando fala, selo de mudo.
// ------------------------------------------------------------------------------------
internal sealed class CallAvatar : StackPanel
{
    private readonly Grid _box = new();
    private readonly Ellipse _ring = new() { StrokeThickness = 3, Opacity = 0 };
    private readonly Border _face = new();
    private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
    private readonly Grid _badge = new() { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly TextBlock _name = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150, Foreground = Brushes.White };
    private static readonly SolidColorBrush Green = CallUi.B("#3DDC84");
    private bool _glow;
    private readonly Ellipse _badgeBg = new() { Fill = CallUi.B("#ED4245"), Stroke = CallUi.B("#0C0E18"), StrokeThickness = 3 };

    public CallAvatar()
    {
        Orientation = Orientation.Vertical;
        Margin = new Thickness(18, 0, 18, 0);
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.NearestNeighbor);
        _face.Background = _brush;
        _face.Margin = new Thickness(6);
        _ring.Stroke = Green;

        _box.Children.Add(_face);
        _box.Children.Add(_ring);

        _badge.Children.Add(_badgeBg);
        _badge.Children.Add(new TextBlock { Text = "🎙", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center });
        _badge.Children.Add(new Line { X1 = 0, Y1 = 12, X2 = 12, Y2 = 0, Stroke = Brushes.White, StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center });
        _box.Children.Add(_badge);

        Children.Add(_box);
        Children.Add(_name);
        SetSize(96);
    }

    public void SetSize(double s)
    {
        _box.Width = s; _box.Height = s;
        _face.CornerRadius = new CornerRadius((s - 12) / 2);
        _badge.Width = _badge.Height = Math.Max(22, s * 0.3);
    }

    public void SetPerson(string name, ImageSource face)
    {
        _name.Text = name;
        _brush.ImageSource = face;
    }

    public void SetState(bool speaking, bool muted, double pulse)
    {
        _badge.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        if (speaking)
        {
            _ring.Stroke = Green;
            _ring.Opacity = 1;
            if (!_glow)
            {
                _ring.Effect = new DropShadowEffect { Color = Color.FromRgb(0x3D, 0xDC, 0x84), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 };
                _glow = true;
            }
            return;
        }
        if (_glow) { _ring.Effect = null; _glow = false; }
        _ring.Opacity = pulse > 0 ? 0.25 + 0.75 * pulse : 0;
        if (pulse > 0) _ring.SetResourceReference(Shape.StrokeProperty, "AccB1_FF");
    }
}

// ------------------------------------------------------------------------------------
//  O painel da chamada, no alto da conversa (estilo Discord): rostos redondos, botões, tela do amigo.
// ------------------------------------------------------------------------------------
public sealed class CallStage : Border
{
    public event Action? MuteClicked, DeafClicked, ShareClicked, HangupClicked, AcceptClicked, DeclineClicked, FullscreenClicked, WatchClicked;
    public event Action<double>? VoiceVolumeChanged, StreamVolumeChanged;
    public event Action<bool>? NoiseSuppressionChanged;

    private readonly CallAvatar _me = new(), _peer = new();
    private readonly TextBlock _status = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = CallUi.B("#C9CCE0"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _banner = new() { FontSize = 12, Foreground = CallUi.B("#C9CCE0"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly Button _watch = new() { Padding = new Thickness(12, 5, 12, 5), FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _full = new() { Content = "⛶  Tela inteira", Padding = new Thickness(12, 5, 12, 5), FontSize = 12, ToolTip = "Abrir a transmissão em tela inteira (Esc volta para a metade)", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Border _shareHost = new() { Background = Brushes.Black, CornerRadius = new CornerRadius(12), Margin = new Thickness(18, 10, 18, 0), Visibility = Visibility.Collapsed, ClipToBounds = true };
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 16) };
    private readonly ToggleButton _mute, _deaf, _share;
    private readonly Button _opt, _hang, _accept, _decline;
    private readonly Popup _pop = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Top };
    private readonly CheckBox _noise = new() { Content = "🔇  Supressão de ruído do microfone", IsChecked = true, Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(0, 12, 0, 0) };
    private bool _noiseSetting;
    private readonly Slider _voiceVol = new() { Minimum = 0, Maximum = 2, Value = 1, Width = 200, SmallChange = 0.05, LargeChange = 0.2 };
    private readonly Slider _streamVol = new() { Minimum = 0, Maximum = 2, Value = 1, Width = 200, SmallChange = 0.05, LargeChange = 0.2 };

    public Image ShareImage { get; } = new() { Stretch = Stretch.Uniform, MaxHeight = 330 };

    /// <summary>Marca/desmarca a caixa da supressão de ruído sem disparar o evento.</summary>
    public void SetNoiseSuppression(bool on)
    {
        _noiseSetting = true;
        _noise.IsChecked = on;
        _noiseSetting = false;
    }

    public CallStage()
    {
        Visibility = Visibility.Collapsed;
        Background = new LinearGradientBrush(Color.FromRgb(0x08, 0x09, 0x12), Color.FromRgb(0x14, 0x10, 0x26), 90);
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BorderBrushProperty, "LineBrush");

        _mute = CallUi.RoundToggle("🎙", 52, "Silenciar o microfone", slash: true);
        _deaf = CallUi.RoundToggle("🎧", 52, "Ficar surdo (não ouve nem fala)", slash: true);
        _share = CallUi.RoundToggle("🖥", 52, "Compartilhar a tela", slash: false, checkedBg: "#5B6CFF");
        _opt = CallUi.RoundButton("⚙", "#33FFFFFF", "#4DFFFFFF", 52, "Volume e opções");
        _hang = CallUi.RoundButton("📞", "#ED4245", "#F25B5E", 52, "Desligar");
        _accept = CallUi.RoundButton("📞", "#2FA866", "#3DDC84", 58, "Atender");
        _decline = CallUi.RoundButton("✖", "#ED4245", "#F25B5E", 58, "Recusar");

        _watch.SetResourceReference(StyleProperty, "GhostButton");
        _full.SetResourceReference(StyleProperty, "GhostButton");

        _mute.Click += (_, _) => MuteClicked?.Invoke();
        _deaf.Click += (_, _) => DeafClicked?.Invoke();
        _share.Click += (_, _) => ShareClicked?.Invoke();
        _hang.Click += (_, _) => HangupClicked?.Invoke();
        _accept.Click += (_, _) => AcceptClicked?.Invoke();
        _decline.Click += (_, _) => DeclineClicked?.Invoke();
        _watch.Click += (_, _) => WatchClicked?.Invoke();
        _full.Click += (_, _) => FullscreenClicked?.Invoke();
        _opt.Click += (_, _) => { _pop.PlacementTarget = _opt; _pop.IsOpen = !_pop.IsOpen; };
        _noise.Checked += (_, _) => { if (!_noiseSetting) NoiseSuppressionChanged?.Invoke(true); };
        _noise.Unchecked += (_, _) => { if (!_noiseSetting) NoiseSuppressionChanged?.Invoke(false); };
        _voiceVol.ValueChanged += (_, e) => VoiceVolumeChanged?.Invoke(e.NewValue);
        _streamVol.ValueChanged += (_, e) => StreamVolumeChanged?.Invoke(e.NewValue);

        // barra de cima: estado + botões da transmissão
        var top = new DockPanel { Margin = new Thickness(18, 12, 12, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_watch);
        right.Children.Add(_full);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(_status);

        _shareHost.Child = ShareImage;
        RenderOptions.SetBitmapScalingMode(ShareImage, BitmapScalingMode.HighQuality);

        var people = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        people.Children.Add(_me);
        people.Children.Add(_peer);

        _bar.Children.Add(_mute); _bar.Children.Add(_deaf); _bar.Children.Add(_share); _bar.Children.Add(_opt); _bar.Children.Add(_hang);
        _bar.Children.Add(_accept); _bar.Children.Add(_decline);

        var root = new StackPanel();
        root.Children.Add(top);
        root.Children.Add(_shareHost);
        root.Children.Add(people);
        root.Children.Add(_banner);
        root.Children.Add(_bar);
        root.Children.Add(_pop);
        Child = root;

        // opções (volume)
        var pb = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(8),
            BorderThickness = new Thickness(1), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.5 } };
        pb.SetResourceReference(BackgroundProperty, "PopupBrush");
        pb.SetResourceReference(BorderBrushProperty, "LineBrush");
        var ps = new StackPanel();
        ps.Children.Add(Label("Volume do seu amigo"));
        ps.Children.Add(_voiceVol);
        ps.Children.Add(Label("Volume da transmissão", 12));
        ps.Children.Add(_streamVol);
        ps.Children.Add(_noise);
        ps.Children.Add(new TextBlock { Text = "Dica: use fones para o seu amigo não ouvir a própria voz de volta.", FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 220,
            Margin = new Thickness(0, 8, 0, 0), Foreground = CallUi.B("#9DA2BF") });
        pb.Child = ps;
        _pop.Child = pb;
    }

    private static TextBlock Label(string t, double top = 0) =>
        new() { Text = t, FontSize = 11.5, Foreground = CallUi.B("#C9CCE0"), Margin = new Thickness(0, top, 0, 4) };

    public void SetPeople(string meName, ImageSource meFace, string peerName, ImageSource peerFace)
    {
        _me.SetPerson(meName + " (você)", meFace);
        _peer.SetPerson(peerName, peerFace);
    }

    public void Render(CallView v)
    {
        Visibility = v.Phase == CallPhase.None ? Visibility.Collapsed : Visibility.Visible;
        if (v.Phase == CallPhase.None) return;

        _status.Text = v.Status;
        var stream = v.Watching && v.HasFrame;
        _shareHost.Visibility = stream ? Visibility.Visible : Visibility.Collapsed;
        if (stream)
        {
            // "Metade": a transmissão ocupa cerca de metade do launcher e a conversa continua à vista.
            // Para ver em "Tela inteira" é só clicar no botão ao lado (Esc volta).
            var wh = Window.GetWindow(this)?.ActualHeight ?? 700;
            ShareImage.MaxHeight = Math.Clamp(wh * 0.42, 220, 640);
        }
        _full.Visibility = stream ? Visibility.Visible : Visibility.Collapsed;
        _watch.Visibility = v.PeerSharing && v.Phase == CallPhase.Active ? Visibility.Visible : Visibility.Collapsed;
        _watch.Content = v.Watching ? "✖  Fechar transmissão" : "▶  Assistir à tela";

        var size = stream ? 54 : 96;
        _me.SetSize(size); _peer.SetSize(size);

        _banner.Text = v.Sharing
            ? "🔴 Você está transmitindo" + (v.ShareInfo.Length > 0 ? " · " + v.ShareInfo : " a tela") + (v.ShareAudioFailed ? " (sem o som do PC: o Windows não deixou capturar)" : "")
            : v.PeerSharing && !v.Watching ? "🖥 Seu amigo está transmitindo a tela" : "";
        _banner.Visibility = _banner.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var ringing = v.Phase != CallPhase.Active;
        _me.SetState(v.MeSpeaking, v.MeMuted || v.MeDeaf, 0);
        _peer.SetState(v.PeerSpeaking, v.PeerMuted || v.PeerDeaf, ringing ? v.Pulse : 0);

        var active = v.Phase == CallPhase.Active;
        _mute.Visibility = _deaf.Visibility = _share.Visibility = _opt.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _hang.Visibility = v.Phase == CallPhase.Incoming ? Visibility.Collapsed : Visibility.Visible;
        _hang.ToolTip = active ? "Desligar" : "Cancelar a chamada";
        _accept.Visibility = _decline.Visibility = v.Phase == CallPhase.Incoming ? Visibility.Visible : Visibility.Collapsed;

        _mute.IsChecked = v.MeMuted || v.MeDeaf;
        _deaf.IsChecked = v.MeDeaf;
        _share.IsChecked = v.Sharing;
        _share.ToolTip = v.Sharing ? "Parar de compartilhar a tela" : "Compartilhar a tela";
    }
}

// ------------------------------------------------------------------------------------
//  Janelinha "fulano está te ligando" (fica por cima de tudo, até do Minecraft).
// ------------------------------------------------------------------------------------
public sealed class CallRingWindow : Window
{
    public event Action? Accepted, Declined, OpenRequested;

    private readonly Border _faceBox = new() { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), ClipToBounds = true };
    private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _name = new() { FontSize = 16, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sub = new() { FontSize = 12.5, Text = "Chamada de voz do Moonlight…" };
    private readonly Border _ring = new() { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), BorderThickness = new Thickness(3) };

    public CallRingWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        var card = new Border { CornerRadius = new CornerRadius(20), Margin = new Thickness(16), Padding = new Thickness(18, 16, 18, 16),
            BorderThickness = new Thickness(1), Width = 350, Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = 0.6, Color = Colors.Black } };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "AccB1_80");

        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.NearestNeighbor);
        _faceBox.Background = _brush;
        _ring.SetResourceReference(Border.BorderBrushProperty, "AccB1_FF");
        var faceGrid = new Grid { Width = 72, Height = 72, Margin = new Thickness(0, 0, 14, 0) };
        faceGrid.Children.Add(_ring);
        faceGrid.Children.Add(_faceBox);

        _name.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        _sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_name);
        texts.Children.Add(_sub);

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(faceGrid);
        head.Children.Add(texts);

        var accept = new Button { Content = "📞  Atender", Padding = new Thickness(0, 10, 0, 10), FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Cursor = Cursors.Hand };
        accept.Template = ButtonTemplate("#2FA866", "#3DDC84");
        var decline = new Button { Content = "✖  Recusar", Padding = new Thickness(0, 10, 0, 10), FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Cursor = Cursors.Hand };
        decline.Template = ButtonTemplate("#ED4245", "#F25B5E");
        accept.Click += (_, _) => Accepted?.Invoke();
        decline.Click += (_, _) => Declined?.Invoke();

        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        accept.Margin = new Thickness(0, 0, 5, 0);
        decline.Margin = new Thickness(5, 0, 0, 0);
        buttons.Children.Add(accept);
        buttons.Children.Add(decline);

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(buttons);
        card.Child = root;
        card.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource is not Button && !(e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null)) OpenRequested?.Invoke(); };
        Content = card;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static ControlTemplate ButtonTemplate(string bg, string hover)
    {
        var xaml = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">" +
                   "<Border x:Name=\"Bd\" CornerRadius=\"12\" Background=\"" + bg + "\"><ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border>" +
                   "<ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + hover + "\"/></Trigger>" +
                   "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.8\"/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    public void ShowCall(string name, ImageSource face)
    {
        _name.Text = name;
        _brush.ImageSource = face;
        if (!IsVisible) Show();
        Opacity = 1;
        var wa = SystemParameters.WorkArea;
        UpdateLayout();
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + 10;

        if (!Perf.Light)
        {
            _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(700))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    public void Dismiss()
    {
        try { _ring.BeginAnimation(OpacityProperty, null); } catch { /* */ }
        if (IsVisible) Hide();
    }
}

// ------------------------------------------------------------------------------------
//  A foto (rosto da skin) de quem está falando, redondinha, com anel verde.
//  Só aparece enquanto a pessoa fala; entra com um "pop" rápido.
// ------------------------------------------------------------------------------------
internal sealed class SpeakerFace : Grid
{
    private readonly Border _ring = new() { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(3), BorderBrush = CallUi.B("#3DDC84") };
    private readonly Border _box = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), ClipToBounds = true };
    private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _shown;

    public SpeakerFace()
    {
        Width = 46; Height = 46; Margin = new Thickness(0, 0, 8, 0);
        Visibility = Visibility.Collapsed;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.NearestNeighbor);
        _box.Background = _brush;
        Children.Add(_ring);
        Children.Add(_box);
    }

    public void Set(ImageSource face, bool speaking)
    {
        _brush.ImageSource = face;
        if (speaking == _shown) return;
        _shown = speaking;
        if (!speaking) { Visibility = Visibility.Collapsed; return; }

        Visibility = Visibility.Visible;
        if (Perf.Light) return;
        var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}

// ------------------------------------------------------------------------------------
//  Barrinha da chamada que fica por cima de tudo quando o painel não está na tela
//  (por exemplo com o Minecraft aberto): mostra a FOTO DE QUEM ESTÁ FALANDO, tempo, mudo e desligar.
//  Quando ninguém fala aparece só o ícone da chamada.
// ------------------------------------------------------------------------------------
public sealed class CallMiniWindow : Window
{
    public event Action? MuteClicked, HangupClicked, OpenRequested;

    private readonly SpeakerFace _peerFace = new(), _meFace = new();
    private readonly Border _idle = new()
    {
        Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 0, 8, 0), Background = CallUi.B("#33808080"),
        Child = new TextBlock { Text = "📞", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };
    private readonly TextBlock _name = new() { FontSize = 13, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
    private readonly TextBlock _time = new() { FontSize = 11.5 };
    private readonly ToggleButton _mute;
    private bool _placed, _moved;

    public CallMiniWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        var card = new Border { CornerRadius = new CornerRadius(30), Margin = new Thickness(12), Padding = new Thickness(8, 6, 10, 6),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.55, Color = Colors.Black } };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "AccB1_80");

        _name.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        _time.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
        texts.Children.Add(_name);
        texts.Children.Add(_time);

        _mute = CallUi.RoundToggle("🎙", 34, "Silenciar", slash: true, bg: "#33808080", hover: "#55808080");
        _mute.Margin = new Thickness(2, 0, 2, 0);
        var hang = CallUi.RoundButton("📞", "#ED4245", "#F25B5E", 34, "Desligar");
        hang.Margin = new Thickness(2, 0, 0, 0);
        _mute.Click += (_, _) => MuteClicked?.Invoke();
        hang.Click += (_, _) => HangupClicked?.Invoke();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_idle);
        row.Children.Add(_peerFace);
        row.Children.Add(_meFace);
        row.Children.Add(texts);
        row.Children.Add(_mute);
        row.Children.Add(hang);
        card.Child = row;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && (FindButton(d))) return;
            if (e.ClickCount == 2) { OpenRequested?.Invoke(); return; }
            _moved = true;
            try { DragMove(); } catch { /* soltou rápido demais */ }
        };
        Content = card;

        // Quando a barrinha muda de largura (entra/sai uma foto), ela continua encostada no canto da tela.
        SizeChanged += (_, _) =>
        {
            if (_placed && !_moved) Left = SystemParameters.WorkArea.Right - ActualWidth - 8;
        };
    }

    private static bool FindButton(DependencyObject d)
    {
        while (d != null)
        {
            if (d is ButtonBase) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    public void Update(string peerName, ImageSource peerFace, string meName, ImageSource meFace, string time, bool muted, bool meSpeaking, bool peerSpeaking)
    {
        _peerFace.Set(peerFace, peerSpeaking);
        _meFace.Set(meFace, meSpeaking);
        _idle.Visibility = peerSpeaking || meSpeaking ? Visibility.Collapsed : Visibility.Visible;

        _name.Text = peerSpeaking && meSpeaking ? peerName + " e você"
            : peerSpeaking ? peerName
            : meSpeaking ? "Você"
            : peerName;
        _time.Text = (peerSpeaking || meSpeaking ? "🗣 falando  ·  " : "") + time + "  ·  duplo clique para abrir";
        _mute.IsChecked = muted;

        if (!IsVisible) Show();
        if (!_placed)
        {
            _placed = true;
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 8;
            Top = wa.Top + 8;
        }
    }

    public void Dismiss()
    {
        _placed = false;
        _moved = false;
        if (IsVisible) Hide();
    }
}

// ------------------------------------------------------------------------------------
//  Tela do amigo em tela cheia (Esc ou duplo clique para sair).
// ------------------------------------------------------------------------------------
public sealed class ShareFullscreenWindow : Window
{
    private readonly Image _img = new() { Stretch = Stretch.Uniform };

    public ShareFullscreenWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        Background = Brushes.Black; Topmost = true; ShowInTaskbar = false;
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        var grid = new Grid();
        grid.Children.Add(_img);
        grid.Children.Add(new TextBlock { Text = "Esc ou duplo clique para sair", Foreground = CallUi.B("#80FFFFFF"), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10), IsHitTestVisible = false });
        var back = new Border
        {
            CornerRadius = new CornerRadius(10), Background = CallUi.B("#99000000"), Padding = new Thickness(13, 8, 13, 8),
            Margin = new Thickness(0, 14, 14, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "◧  Voltar para metade", Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold }
        };
        back.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        grid.Children.Add(back);
        Content = grid;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseDoubleClick += (_, _) => Close();
    }

    public void SetFrame(ImageSource src) => _img.Source = src;
}
