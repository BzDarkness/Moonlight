using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>
/// Temas do launcher (Padrão, Claro, Escuro e Midnight) + as paletas de cores do Golden Moon.
/// Tudo vira "recursos dinâmicos" em Application.Resources: o XAML usa {DynamicResource ...}, então
/// trocar o tema muda a janela inteira na hora (fundo, cartões, botões, abas, listas…).
/// </summary>
public static class ThemeManager
{
    public static string Current { get; private set; } = "Default";
    public static bool PalettesOn { get; private set; }

    /// <summary>0 = sem céu estrelado (tema Claro); 1 = céu completo.</summary>
    public static double SkyOpacity { get; private set; } = 1.0;

    public static event Action? Changed;

    public static readonly Color DefaultGold1 = Color.FromRgb(0xFF, 0xD5, 0x4F);
    public static readonly Color DefaultGold2 = Color.FromRgb(0xFF, 0x8F, 0x00);

    private static readonly Color DefA1 = Color.FromRgb(0x8F, 0x7B, 0xFF);
    private static readonly Color DefA2 = Color.FromRgb(0x4F, 0xC3, 0xF7);
    private static readonly Color DefA3 = Color.FromRgb(0xC7, 0x7D, 0xFF);

    private static readonly string SettingsFile = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "settings.json");

    // Transparências usadas no XAML para as cores de destaque (AccB1_55 = destaque 1 com alfa 0x55 etc.).
    private static readonly string[] Alphas = { "00", "16", "33", "38", "44", "48", "4C", "55", "66", "80", "88", "99", "FF" };

    private sealed record Pal(
        string Text, string TextStrong, string Text2, string Muted,
        string Surface, string SurfaceHi, string Line, string Thumb, string Sidebar, string Details,
        string Overlay, string Popup, string CardA, string CardB,
        string Bg0, string Bg1, string Bg2, string GreenText, double Sky, double Tint);

    private static readonly Dictionary<string, Pal> Palettes = new()
    {
        // O visual de sempre do launcher.
        ["Default"] = new Pal("#EDEBFF", "#FFFFFF", "#CFCBF0", "#9A96C8",
            "#12FFFFFF", "#22FFFFFF", "#26FFFFFF", "#40FFFFFF", "#14000000", "#33000000",
            "#D905050F", "#FF1A1540", "#1BFFFFFF", "#0AFFFFFF",
            "#070716", "#140D40", "#0A1C40", "#7FE3A8", 1.0, 0.25),

        // Branco suave (não cansa a vista).
        ["Light"] = new Pal("#23263A", "#14162A", "#4A4F68", "#6B7090",
            "#0F000000", "#1C000000", "#26000000", "#40000000", "#0C000000", "#14000000",
            "#99101428", "#FFFFFFFF", "#D9FFFFFF", "#99FFFFFF",
            "#F4F5F9", "#EEF0F6", "#E9ECF4", "#1E8E4E", 0.0, 0.16),

        // Cinza escuro.
        ["Dark"] = new Pal("#ECECF1", "#FFFFFF", "#C4C5CE", "#9293A4",
            "#14FFFFFF", "#26FFFFFF", "#26FFFFFF", "#40FFFFFF", "#26000000", "#33000000",
            "#D9101114", "#FF2B2C33", "#1AFFFFFF", "#08FFFFFF",
            "#1C1D21", "#25262B", "#1A1B1F", "#7FE3A8", 0.4, 0.22),

        // Bem preto.
        ["Midnight"] = new Pal("#E9E9F2", "#FFFFFF", "#C0C0CC", "#8A8A9A",
            "#0FFFFFFF", "#1FFFFFFF", "#22FFFFFF", "#40FFFFFF", "#33000000", "#40000000",
            "#E6000000", "#FF0A0A0F", "#10FFFFFF", "#06FFFFFF",
            "#000000", "#050508", "#000000", "#7FE3A8", 0.85, 0.15),
    };

    // ---------- Início ----------

    /// <summary>Chamado no começo do programa, antes de qualquer janela existir (evita \"piscar\" no tema errado).</summary>
    public static void Initialize()
    {
        var theme = "Default";
        var code = "";
        var on = false;
        var c1 = DefaultGold1;
        var c2 = DefaultGold2;

        try
        {
            if (File.Exists(SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
                var r = doc.RootElement;
                if (r.TryGetProperty("Theme", out var t) && t.ValueKind == JsonValueKind.String)
                    theme = t.GetString() ?? "Default";
                if (r.TryGetProperty("GoldenCode", out var g) && g.ValueKind == JsonValueKind.String)
                    code = g.GetString() ?? "";
                if (r.TryGetProperty("GoldPalettesOn", out var p))
                    on = p.ValueKind == JsonValueKind.True;
                if (r.TryGetProperty("GoldColor1", out var a) && a.ValueKind == JsonValueKind.String)
                    c1 = ParseColor(a.GetString(), c1);
                if (r.TryGetProperty("GoldColor2", out var b) && b.ValueKind == JsonValueKind.String)
                    c2 = ParseColor(b.GetString(), c2);
            }
        }
        catch { /* arquivo ruim: usa o tema padrão */ }

        Apply(theme, on && GoldenMoon.IsValid(code), c1, c2);
    }

    // ---------- Aplicar ----------

    public static void Apply(string theme, bool palettes, Color c1, Color c2)
    {
        Current = theme is "Light" or "Dark" or "Midnight" ? theme : "Default";
        PalettesOn = palettes;

        var res = Application.Current?.Resources;
        if (res == null) return;

        var p = Palettes[Current];
        SkyOpacity = p.Sky;

        // Cores de destaque: as padrão (lilás/azul) ou as duas paletas do Golden Moon.
        Color a1 = DefA1, a2 = DefA2, a3 = DefA3;
        if (palettes)
        {
            a1 = c1;
            a2 = c2;
            a3 = Mix(c1, c2, 0.5);
        }

        // Neutros do tema
        res["TextBrush"] = Br(p.Text);
        res["TextStrongBrush"] = Br(p.TextStrong);
        res["Text2Brush"] = Br(p.Text2);
        res["Muted"] = Br(p.Muted);
        res["SurfaceBrush"] = Br(p.Surface);
        res["SurfaceHiBrush"] = Br(p.SurfaceHi);
        res["LineBrush"] = Br(p.Line);
        res["ThumbBrush"] = Br(p.Thumb);
        res["SidebarBrush"] = Br(p.Sidebar);
        res["DetailsBrush"] = Br(p.Details);
        res["OverlayBrush"] = Br(p.Overlay);
        res["PopupBrush"] = Br(p.Popup);
        res["GreenTextBrush"] = Br(p.GreenText);
        res["CardBrush"] = Grad(Col(p.CardA), Col(p.CardB));

        // Fundo da janela: o degradê do tema, tingido pelas paletas quando elas estão ligadas.
        var t = palettes ? p.Tint : 0.0;
        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        bg.GradientStops.Add(new GradientStop(Mix(Col(p.Bg0), a1, t), 0));
        bg.GradientStops.Add(new GradientStop(Mix(Col(p.Bg1), a3, Math.Min(1, t * 1.15)), 0.55));
        bg.GradientStops.Add(new GradientStop(Mix(Col(p.Bg2), a2, t), 1));
        bg.Freeze();
        res["WindowBgBrush"] = bg;

        // Destaques (com todas as transparências que o XAML usa)
        res["AccentGradient"] = Grad(a1, a2);
        foreach (var al in Alphas)
        {
            var alpha = Convert.ToByte(al, 16);
            SetAccent(res, 1, a1, al, alpha);
            SetAccent(res, 2, a2, al, alpha);
            SetAccent(res, 3, a3, al, alpha);
        }

        Changed?.Invoke();
    }

    private static void SetAccent(ResourceDictionary res, int n, Color c, string al, byte alpha)
    {
        var col = Color.FromArgb(alpha, c.R, c.G, c.B);
        res[$"Acc{n}_{al}"] = col;
        var b = new SolidColorBrush(col);
        b.Freeze();
        res[$"AccB{n}_{al}"] = b;
    }

    // ---------- Utilidades de cor ----------

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            if (h.Length != 7 && h.Length != 9) return fallback;
            var obj = ColorConverter.ConvertFromString(h);
            return obj is Color c ? Color.FromRgb(c.R, c.G, c.B) : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string HexOf(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Mix(Color x, Color y, double t) => Color.FromRgb(
        (byte)Math.Round(x.R + (y.R - x.R) * t),
        (byte)Math.Round(x.G + (y.G - x.G) * t),
        (byte)Math.Round(x.B + (y.B - x.B) * t));

    private static Color Col(string hex)
    {
        var obj = ColorConverter.ConvertFromString(hex);
        return obj is Color c ? c : Colors.Magenta;
    }

    private static SolidColorBrush Br(string hex)
    {
        var b = new SolidColorBrush(Col(hex));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Grad(Color start, Color end)
    {
        var g = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }
}

/// <summary>
/// Verificação do código de ativação do Golden Moon (funciona sem internet).
/// O código tem 16 letras/números: 10 sorteados + 6 de verificação (HMAC com o segredo abaixo).
/// Para gerar códigos use o arquivo tools/gerar-codigo-golden.html (com o MESMO segredo).
/// </summary>
public static class GoldenMoon
{
    // >>> TROQUE este segredo por um só seu e use o MESMO no gerador de códigos. <<<
    public const string Secret = "TROQUE-ESTE-SEGREDO-DO-GOLDEN-MOON";

    // >>> Link da sua página de assinatura (ex.: o site do launcher no Render). Vazio = ainda não disponível. <<<
    public const string SubscribeUrl = "";

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Normalize(string? code)
    {
        var sb = new StringBuilder();
        foreach (var ch in (code ?? "").ToUpperInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    public static bool IsValid(string? code)
    {
        var c = Normalize(code);
        if (c.Length != 16) return false;
        foreach (var ch in c)
            if (Alphabet.IndexOf(ch) < 0) return false;

        return string.Equals(Mac(c.Substring(0, 10)), c.Substring(10, 6), StringComparison.Ordinal);
    }

    private static string Mac(string payload)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var d = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var n = ((uint)d[0] << 24) | ((uint)d[1] << 16) | ((uint)d[2] << 8) | d[3];

        var sb = new StringBuilder();
        for (var i = 0; i < 6; i++)
            sb.Append(Alphabet[(int)((n >> (27 - 5 * i)) & 31u)]);
        return sb.ToString();
    }
}

// ---------- Interface: temas, paletas e a aba Golden Moon ----------

public partial class MainWindow
{
    private sealed class PaletteRow
    {
        public Border Swatch = null!;
        public TextBox Hex = null!;
        public Slider R = null!;
        public Slider G = null!;
        public Slider B = null!;
    }

    private static readonly string[] SwatchColors =
    {
        "#FFD54F", "#FFB300", "#FF8F00", "#FF6F00", "#F44336", "#E91E63", "#C77DFF", "#8F7BFF",
        "#5C6BC0", "#4FC3F7", "#26C6DA", "#26A69A", "#66BB6A", "#9CCC65", "#F5F5F5", "#9E9E9E"
    };

    private bool _themeUiReady;
    private bool _palBusy;
    private DispatcherTimer? _palTimer;
    private Color _palC1 = ThemeManager.DefaultGold1;
    private Color _palC2 = ThemeManager.DefaultGold2;
    private readonly List<PaletteRow> _palRows = new();

    private bool GoldenActive => GoldenMoon.IsValid(_state.GoldenCode);

    private void InitThemeUi()
    {
        _palC1 = ThemeManager.ParseColor(_state.GoldColor1, ThemeManager.DefaultGold1);
        _palC2 = ThemeManager.ParseColor(_state.GoldColor2, ThemeManager.DefaultGold2);

        foreach (var rb in ThemePanel.Children.OfType<RadioButton>())
            rb.IsChecked = (rb.Tag?.ToString() ?? "") == ThemeManager.Current;

        BuildPaletteEditor();
        BuildIconPickers();
        ThemeManager.Changed += () => Dispatcher.Invoke(() => OnThemeChanged());

        _themeUiReady = true;
        OnThemeChanged();
        UpdateGoldenUi();
    }

    // Reage a uma troca de tema: céu estrelado liga/desliga e o texto de apoio muda.
    private void OnThemeChanged()
    {
        if (SkyHost == null || ThemeInfo == null) return;

        var skyOn = ThemeManager.SkyOpacity > 0.01;
        SkyHost.Visibility = skyOn ? Visibility.Visible : Visibility.Collapsed;
        SkyHost.Opacity = ThemeManager.SkyOpacity;
        if (!_renderPaused)
        {
            if (skyOn) _bg?.Resume(); else _bg?.Pause();
        }

        ThemeInfo.Text = ThemeManager.Current switch
        {
            "Light" => "Tema Claro: branco suave que não cansa a vista.",
            "Dark" => "Tema Escuro: cinza escuro, com o céu bem discreto.",
            "Midnight" => "Tema Midnight: preto total, com estrelas bem de leve.",
            _ => "Tema Padrão: o céu estrelado do Moonlight."
        };
        if (ThemeManager.PalettesOn) ThemeInfo.Text += "  ·  Suas paletas Golden Moon estão ligadas.";
    }

    private void ThemeChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (!_themeUiReady || sender is not RadioButton rb) return;
        _state.Theme = rb.Tag?.ToString() ?? "Default";
        SaveState();
        ApplyThemeNow();
    }

    private void ApplyThemeNow() =>
        ThemeManager.Apply(_state.Theme, GoldenActive && _state.GoldPalettesOn, _palC1, _palC2);

    // ---------- Editor das 2 paletas (só para assinantes) ----------

    private void BuildPaletteEditor()
    {
        PaletteHost.Children.Clear();
        _palRows.Clear();

        for (var i = 0; i < 2; i++)
        {
            var idx = i;
            var row = new PaletteRow();

            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(1)
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            // Esquerda: amostra + nome + código da cor + cores prontas
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            row.Swatch = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 12, 0),
                BorderThickness = new Thickness(1)
            };
            row.Swatch.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

            head.Children.Add(row.Swatch);
            head.Children.Add(new TextBlock
            {
                Text = $"Paleta {i + 1}",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            row.Hex = new TextBox
            {
                Width = 104,
                MaxLength = 9,
                FontSize = 13,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Código da cor (ex.: #FFD54F)"
            };
            row.Hex.Style = (Style)FindResource("MoonTextBox");
            row.Hex.Padding = new Thickness(8, 6, 8, 6);
            row.Hex.KeyDown += (_, ev) =>
            {
                if (ev.Key == Key.Enter) CommitHex(idx);
            };
            row.Hex.LostFocus += (_, _) => CommitHex(idx);
            head.Children.Add(row.Hex);
            left.Children.Add(head);

            var swatches = new WrapPanel();
            foreach (var hex in SwatchColors)
            {
                var color = ThemeManager.ParseColor(hex, Colors.White);
                var chip = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = hex
                };
                chip.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                chip.MouseLeftButtonUp += (_, _) => UserPickColor(idx, color);
                swatches.Children.Add(chip);
            }
            left.Children.Add(swatches);
            grid.Children.Add(left);

            // Direita: sliders R, G, B
            var sliders = new Grid { VerticalAlignment = VerticalAlignment.Center };
            sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < 3; r++) sliders.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Slider MakeSlider(string label, int rowIndex)
            {
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                lbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                Grid.SetRow(lbl, rowIndex);
                sliders.Children.Add(lbl);

                var sl = new Slider { Minimum = 0, Maximum = 255, Margin = new Thickness(0, 2, 0, 2) };
                sl.Style = (Style)FindResource("MoonSlider");
                Grid.SetRow(sl, rowIndex);
                Grid.SetColumn(sl, 1);
                sl.ValueChanged += (_, _) =>
                {
                    if (_palBusy) return;
                    UserPickColor(idx, Color.FromRgb(
                        (byte)Math.Round(row.R.Value), (byte)Math.Round(row.G.Value), (byte)Math.Round(row.B.Value)));
                };
                sliders.Children.Add(sl);
                return sl;
            }

            row.R = MakeSlider("R", 0);
            row.G = MakeSlider("G", 1);
            row.B = MakeSlider("B", 2);

            Grid.SetColumn(sliders, 1);
            grid.Children.Add(sliders);

            card.Child = grid;
            PaletteHost.Children.Add(card);
            _palRows.Add(row);
        }

        SetPaletteColor(0, _palC1);
        SetPaletteColor(1, _palC2);
    }

    private void CommitHex(int idx)
    {
        if (_palBusy || idx < 0 || idx >= _palRows.Count) return;
        var current = idx == 0 ? _palC1 : _palC2;
        var parsed = ThemeManager.ParseColor(_palRows[idx].Hex.Text, current);
        if (parsed == current)
        {
            _palRows[idx].Hex.Text = ThemeManager.HexOf(current); // texto inválido: volta ao valor certo
            return;
        }
        UserPickColor(idx, parsed);
    }

    // O jogador mexeu numa cor: liga as paletas, mostra na hora e salva logo depois.
    private void UserPickColor(int idx, Color c)
    {
        if (!GoldenActive) return;
        SetPaletteColor(idx, c);

        _state.GoldPalettesOn = true;
        PaletteToggle.IsChecked = true;
        SchedulePaletteApply();
    }

    private void SetPaletteColor(int idx, Color c)
    {
        if (idx == 0) _palC1 = c; else _palC2 = c;
        var row = _palRows[idx];

        _palBusy = true;
        try
        {
            row.Swatch.Background = new SolidColorBrush(c);
            row.Hex.Text = ThemeManager.HexOf(c);
            row.R.Value = c.R;
            row.G.Value = c.G;
            row.B.Value = c.B;
        }
        finally
        {
            _palBusy = false;
        }

        PalettePreview.Background = new LinearGradientBrush(_palC1, _palC2, 0);
    }

    private void SchedulePaletteApply()
    {
        _palTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _palTimer.Tick -= PaletteTimerTick;
        _palTimer.Tick += PaletteTimerTick;
        _palTimer.Stop();
        _palTimer.Start();
    }

    private void PaletteTimerTick(object? sender, EventArgs e)
    {
        _palTimer?.Stop();
        _state.GoldColor1 = ThemeManager.HexOf(_palC1);
        _state.GoldColor2 = ThemeManager.HexOf(_palC2);
        SaveState();
        ApplyThemeNow();
    }

    private void PaletteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive)
        {
            PaletteToggle.IsChecked = false;
            return;
        }
        _state.GoldPalettesOn = PaletteToggle.IsChecked == true;
        SaveState();
        ApplyThemeNow();
    }

    private void PaletteSave_Click(object sender, RoutedEventArgs e)
    {
        if (!GoldenActive) return;
        _state.GoldColor1 = ThemeManager.HexOf(_palC1);
        _state.GoldColor2 = ThemeManager.HexOf(_palC2);
        _state.GoldPalettesOn = true;
        PaletteToggle.IsChecked = true;
        SaveState();
        ApplyThemeNow();
        ThemeInfo.Text = "✔ Paletas salvas. O launcher vai abrir sempre com elas.";
    }

    private void PaletteReset_Click(object sender, RoutedEventArgs e)
    {
        SetPaletteColor(0, ThemeManager.DefaultGold1);
        SetPaletteColor(1, ThemeManager.DefaultGold2);
        _state.GoldPalettesOn = false;
        _state.GoldColor1 = ThemeManager.HexOf(_palC1);
        _state.GoldColor2 = ThemeManager.HexOf(_palC2);
        PaletteToggle.IsChecked = false;
        SaveState();
        ApplyThemeNow();
    }

    private void GoldenLock_Click(object sender, RoutedEventArgs e) => GoTo("Golden");

    // ---------- Aba Golden Moon ----------

    private void UpdateGoldenUi()
    {
        if (GoldenStatus == null) return;
        var on = GoldenActive;

        if (on) GoldenStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4A));
        else GoldenStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        GoldenStatus.Text = on ? "✔ Golden Moon ativo" : "Você ainda não é assinante";
        GoldenActionTitle.Text = on ? "Tudo liberado 🌙" : "Ativar o Golden Moon";
        GoldenActionDesc.Text = on
            ? "Obrigado por apoiar o Moonlight Launcher! Em Ajustes você cria suas paletas e troca os ícones; o criador de skin e o criador de modpacks já estão liberados."
            : "Já assinou? Cole abaixo o código de ativação que você recebeu. Ainda não assinou? Clique em “Assinar o Golden Moon”.";

        GoldenCodeRow.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        GoldenSubscribeBtn.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        GoldenCustomizeBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        GoldenRemoveBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        GoldenCustomizeBtn.Content = "🎨  Personalizar (paletas e ícones)";

        PaletteLock.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        PaletteBox.IsEnabled = on;
        PaletteToggle.IsChecked = on && _state.GoldPalettesOn;

        // Ícones, criador de skin e criador de modpacks: tudo só com o Golden Moon.
        IconLock.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        IconBox.IsEnabled = on;
        PackLock.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        PackTab.Content = on ? "🎒  Meus modpacks" : "🔒  Meus modpacks";
        SkinEditorTab.Content = on ? "🎨  Criador de skin" : "🔒  Criador de skin";
        ApplySkinTab(CurrentSkinTab());
        RefreshIconTiles();
        ApplyIconChoices();
    }

    private void GoldenActivate_Click(object sender, RoutedEventArgs e)
    {
        var code = GoldenCodeBox.Text;
        if (!GoldenMoon.IsValid(code))
        {
            GoldenMsg.Text = "Esse código não é válido. Confira se digitou tudo certinho (são 16 letras e números).";
            return;
        }

        _state.GoldenCode = GoldenMoon.Normalize(code);
        SaveState();
        GoldenCodeBox.Text = "";
        GoldenMsg.Text = "🌙 Golden Moon ativado! Paletas e ícones em Ajustes; criador de skin e modpacks já estão liberados.";
        UpdateGoldenUi();
        ApplyThemeNow();
        ShowGoldenFx(true); // explosão dourada de comemoração
    }

    private async void GoldenSubscribe_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GoldenMoon.SubscribeUrl))
        {
            await ShowPopupAsync("Golden Moon",
                "A página de assinatura ainda não está disponível. Tente de novo em breve.", null, ("Ok", "ok"));
            return;
        }
        OpenUrl(GoldenMoon.SubscribeUrl);
    }

    private void GoldenCustomize_Click(object sender, RoutedEventArgs e) => GoTo("Settings");

    private async void GoldenRemove_Click(object sender, RoutedEventArgs e)
    {
        var r = await ShowPopupAsync("Remover o Golden Moon deste PC?",
            "As paletas voltam ao padrão. Você poderá ativar de novo com o seu código quando quiser.", null,
            ("Remover", "yes"), ("Cancelar", "no"));
        if (r != "yes") return;

        _state.GoldenCode = "";
        _state.GoldPalettesOn = false;
        SaveState();
        GoldenMsg.Text = "";
        UpdateGoldenUi();
        ApplyThemeNow();
    }
}
