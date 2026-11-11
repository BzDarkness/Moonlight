using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoonlightLauncher;

/// <summary>O que cada versão do Minecraft traz: nome da atualização, texto, destaques e o banner (pixel art em Assets/versions).</summary>
public sealed record VersionTheme(string Art, string Name, string Emoji, string Description, string[] Chips, string Accent);

public static class VersionLore
{
    private static VersionTheme T(string art, string name, string emoji, string desc, string accent, params string[] chips) =>
        new(art, name, emoji, desc, chips, accent);

    private static readonly VersionTheme Release = T("end", "Lançamento 1.0", "🐉",
        "O Minecraft completo: Nether, o Fim e a luta contra o Ender Dragon.", "#C792FF", "🐉 Ender Dragon", "🌌 O Fim", "🧪 Poções");
    private static readonly VersionTheme Jungle = T("jungle", "Selvas e ocelotes", "🌴",
        "Biomas de selva, ocelotes que viram gatinhos e o golem de ferro.", "#6FE08A", "🌴 Selvas", "🐱 Ocelotes", "🗿 Golem de ferro");
    private static readonly VersionTheme Trade = T("village", "Trocas e esmeraldas", "💚",
        "Troque com aldeões usando esmeraldas e jogue em servidor integrado.", "#7FE3A8", "💚 Esmeraldas", "🏘 Trocas", "🧭 Servidor integrado");
    private static readonly VersionTheme Scary = T("night", "Pretty Scary Update", "👻",
        "Wither, bruxas, bigorna e os primeiros blocos de comando.", "#B9A6FF", "👻 Wither", "🧙 Bruxas", "⚒ Bigorna");
    private static readonly VersionTheme RedstoneT = T("redstone", "Redstone Update", "🔴",
        "Comparador, funil e blocos de redstone para máquinas mais espertas.", "#FF6B5E", "🔴 Redstone", "💡 Comparador", "🔦 Funil");
    private static readonly VersionTheme Horse = T("plains", "Horse Update", "🐴",
        "Cavalos, burros, laços e fardos de feno para explorar as planícies.", "#9BE37A", "🐴 Cavalos", "🔗 Laço", "🌾 Feno");
    private static readonly VersionTheme World = T("mesa", "O mundo mudou", "🌍",
        "Muitos biomas novos (como a mesa), mais peixes e flores.", "#FFB070", "🌍 Biomas", "🎣 Peixes", "🌻 Flores");
    private static readonly VersionTheme Bounty = T("ocean", "Bountiful Update", "🏛",
        "Monumentos oceânicos, coelhos, esponjas e suportes de armadura.", "#62D6C4", "🏛 Monumentos", "🐇 Coelhos", "🧽 Esponja");
    private static readonly VersionTheme Combat = T("end", "Combat Update", "⚔",
        "Escudos, segunda mão, elytra e as cidades do Fim.", "#C792FF", "⚔ Escudos", "🦅 Elytra", "🏙 Cidades do Fim");
    private static readonly VersionTheme Frost = T("frost", "Frostburn Update", "❄",
        "Ursos polares, blocos de magma e esqueletos Stray.", "#8FD8F5", "🐻 Urso polar", "🌋 Magma", "💀 Stray");
    private static readonly VersionTheme Explore = T("mansion", "Exploration Update", "🏰",
        "Mansões na floresta escura, lhamas, shulkers e o totem da imortalidade.", "#8FD08A", "🏰 Mansões", "🦙 Lhamas", "🛡 Totem");
    private static readonly VersionTheme Rainbow = T("color", "World of Color", "🌈",
        "Concreto, terracota esmaltada, papagaios e o livro de receitas.", "#FFD54F", "🧱 Concreto", "🦜 Papagaios", "📖 Receitas");
    private static readonly VersionTheme Aquatic = T("ocean", "Update Aquatic", "🐬",
        "Oceanos vivos: golfinhos, tartarugas, afogados, naufrágios e corais.", "#4FC3F7", "🐬 Golfinhos", "🐢 Tartarugas", "🚢 Naufrágios");
    private static readonly VersionTheme Village = T("village", "Village & Pillage", "🏘",
        "Vilas renovadas, saqueadores, a besta e a raposa.", "#E3B36A", "🏘 Vilas novas", "🏹 Saqueadores", "🦊 Raposas");
    private static readonly VersionTheme Bees = T("bees", "Buzzy Bees", "🐝",
        "Abelhas, colmeias e mel. Cuidado ao mexer no ninho!", "#FFD24A", "🐝 Abelhas", "🍯 Mel", "🪹 Colmeias");
    private static readonly VersionTheme NetherT = T("nether", "Nether Update", "🔥",
        "Piglins, hoglins, netherite e biomas novos no Nether.", "#FF7A3D", "🔥 Piglins", "🟪 Netherite", "🌋 Biomas do Nether");
    private static readonly VersionTheme CavesA = T("caves", "Caves & Cliffs: Parte I", "🦎",
        "Axolotes, cabras, ametista, cobre e lunetas.", "#B388FF", "🦎 Axolotes", "🟣 Ametista", "🟠 Cobre");
    private static readonly VersionTheme CavesB = T("mountain", "Caves & Cliffs: Parte II", "⛰",
        "Montanhas gigantes, cavernas enormes e um mundo bem mais alto.", "#9FD0FF", "⛰ Montanhas", "🕳 Cavernas", "📐 Mundo mais alto");
    private static readonly VersionTheme Wild = T("wild", "The Wild Update", "🌿",
        "Manguezais, Deep Dark, o Warden, sapos e o allay.", "#5BD4B6", "🌿 Mangue", "👁 Deep Dark", "🐸 Sapos");
    private static readonly VersionTheme Cherry = T("cherry", "Trails & Tales", "🌸",
        "Bosques de cerejeira, camelos, arqueologia, sniffer e acabamentos de armadura.", "#FF9BC8", "🌸 Cerejeira", "🐪 Camelo", "🔍 Arqueologia");
    private static readonly VersionTheme Paws = T("savanna", "Armored Paws", "🐺",
        "Armadura para lobos, o tatu e novidades para as câmaras de prova.", "#FFB070", "🐺 Armadura de lobo", "🦔 Tatu");
    private static readonly VersionTheme Trials = T("trials", "Tricky Trials", "🧪",
        "Câmaras de prova, a maça, o Breeze e o crafter automático.", "#5BD4B6", "🧪 Câmaras de prova", "🔨 Maça", "💨 Breeze");
    private static readonly VersionTheme Bundles = T("meadow", "Bundles of Bravery", "🎒",
        "Bundles para carregar vários itens e outros ajustes do dia a dia.", "#FFB88A", "🎒 Bundles");
    private static readonly VersionTheme Pale = T("pale", "The Garden Awakens", "🌫",
        "O Pale Garden, o Creaking e a resina.", "#C9D2C0", "🌫 Pale Garden", "👀 Creaking", "🟠 Resina");
    private static readonly VersionTheme Spring = T("spring", "Spring to Life", "🍃",
        "Folhas caídas, arbustos, vagalumes e animais com variações.", "#8EE070", "🍃 Folhas caídas", "🐺 Lobos variados", "✨ Vagalumes");
    private static readonly VersionTheme Sky = T("sky", "Chase the Skies", "☁",
        "O Happy Ghast e passeios pelo céu.", "#7CC4FF", "☁ Happy Ghast", "🎈 Passeios no céu");
    private static readonly VersionTheme Copper = T("copper", "The Copper Age", "🟠",
        "Golem de cobre, ferramentas e armadura de cobre.", "#FF9A5E", "🟠 Golem de cobre", "⛏ Ferramentas de cobre");
    private static readonly VersionTheme Mounts = T("mounts", "Mounts of Mayhem", "🐎",
        "Montarias novas e armas de combate.", "#FFC070", "🐎 Montarias", "🔱 Lanças");
    private static readonly VersionTheme Year = T("year", "Era 26", "✨",
        "Numeração nova do Minecraft (por ano). Veja as novidades no site oficial.", "#FFD54F", "✨ Versão nova");
    private static readonly VersionTheme Fallback = T("plains", "Minecraft", "⛏",
        "Uma versão do Minecraft Java.", "#9BE37A");

    private static int Num(string[] parts, int i) =>
        i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

    public static VersionTheme For(string mc)
    {
        var parts = (mc ?? "").Split('.');
        int a = Num(parts, 0), b = Num(parts, 1), c = Num(parts, 2);

        if (a >= 26) return Year;
        if (a != 1) return Fallback;

        return b switch
        {
            0 or 1 => Release,
            2 => Jungle,
            3 => Trade,
            4 => Scary,
            5 => RedstoneT,
            6 => Horse,
            7 => World,
            8 => Bounty,
            9 => Combat,
            10 => Frost,
            11 => Explore,
            12 => Rainbow,
            13 => Aquatic,
            14 => Village,
            15 => Bees,
            16 => NetherT,
            17 => CavesA,
            18 => CavesB,
            19 => Wild,
            20 => c >= 5 ? Paws : Cherry,
            21 => c >= 11 ? Mounts : c >= 9 ? Copper : c >= 6 ? Sky : c == 5 ? Spring : c == 4 ? Pale : c >= 2 ? Bundles : Trials,
            _ => Fallback
        };
    }
}

// Aba Jogar no modo DETALHADO: um destaque grande da versão escolhida e cartões com a foto (bloco) e o banner de cada versão.
public partial class MainWindow
{
    private bool _playViewReady;
    private double _cardW = 216;
    private readonly Dictionary<string, Border> _versionCards = new();
    private readonly Dictionary<string, ImageBrush> _bannerBrushes = new();
    private string? _heroFor;

    // ---------- Ligar / desligar ----------

    private void InitPlayView()
    {
        _playViewReady = false;
        PlayViewList.IsChecked = !_state.PlayDetailed;
        PlayViewCards.IsChecked = _state.PlayDetailed;
        _playViewReady = true;

        VersionList.SelectionChanged += (_, _) => OnVersionSelected();
        ApplyPlayView();
    }

    private void PlayView_Checked(object sender, RoutedEventArgs e)
    {
        if (!_playViewReady || sender is not RadioButton rb) return;
        _state.PlayDetailed = rb.Tag?.ToString() == "cards";
        SaveState();
        ApplyPlayView();
    }

    private void ApplyPlayView()
    {
        var detailed = _state.PlayDetailed;
        VersionList.Visibility = detailed ? Visibility.Collapsed : Visibility.Visible;
        VersionDetailView.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;

        if (detailed) RebuildVersionCards();
        else
        {
            // A lista simples fica em branco enquanto a outra visão está à mostra (menos coisa para desenhar).
            VersionCardsPanel.Children.Clear();
            _versionCards.Clear();
        }
    }

    private void VersionCardsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = VersionCardsScroll.ActualWidth - 18; // sobra para a barra de rolagem
        if (w < 200) return;

        var cols = Math.Max(2, (int)(w / 205));
        var cardW = Math.Floor((w - 10 * cols) / cols);
        if (Math.Abs(cardW - _cardW) < 0.5) return;

        _cardW = cardW;
        foreach (var card in _versionCards.Values) card.Width = _cardW;
    }

    // ---------- Montagem ----------

    private ImageBrush BannerBrush(string art)
    {
        if (_bannerBrushes.TryGetValue(art, out var hit)) return hit;

        ImageBrush brush;
        try
        {
            brush = new ImageBrush(LoadAsset($"versions/{art}.png"))
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            brush.Freeze();
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Banner da versão não carregou ({art})", ex);
            brush = new ImageBrush();
        }

        _bannerBrushes[art] = brush;
        return brush;
    }

    private Border PixelBorder(string art, bool block, double radiusTop, double radiusBottom)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(radiusTop, radiusTop, radiusBottom, radiusBottom),
            Background = BannerBrush(block ? art + "_block" : art)
        };
        RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.NearestNeighbor); // pixel art nítida
        return b;
    }

    private void RebuildVersionCards()
    {
        if (!_state.PlayDetailed || VersionCardsPanel == null) return;

        VersionCardsPanel.Children.Clear();
        _versionCards.Clear();

        foreach (var item in VersionList.Items)
        {
            if (item is not string mc) continue;
            var card = MakeVersionCard(mc);
            _versionCards[mc] = card;
            VersionCardsPanel.Children.Add(card);
        }

        _heroFor = null;
        OnVersionSelected();
    }

    private Border MakeVersionCard(string mc)
    {
        var t = VersionLore.For(mc);
        var accent = ThemeManager.ParseColor(t.Accent, Colors.White);
        var installed = InstalledVersionConverter.Installed.Contains(mc);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Banner no topo
        var banner = PixelBorder(t.Art, false, 12.5, 0);
        grid.Children.Add(banner);

        var bannerOverlay = new Grid();
        bannerOverlay.Children.Add(new TextBlock
        {
            Text = t.Emoji,
            FontSize = 26,
            Opacity = 0.92,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 10, 2),
            IsHitTestVisible = false
        });
        if (installed)
        {
            bannerOverlay.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x8E, 0x4E)),
                Padding = new Thickness(7, 1, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 7, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = "✔ Baixada", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
            });
        }
        grid.Children.Add(bannerOverlay);

        // Texto + "foto" (bloco) da versão
        var info = new Grid { Margin = new Thickness(10, 0, 10, 9) };
        Grid.SetRow(info, 1);
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = PixelBorder(t.Art, true, 7, 7);
        icon.Width = 36;
        icon.Height = 36;
        icon.Margin = new Thickness(0, -16, 9, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.BorderThickness = new Thickness(2);
        icon.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x14, 0x12, 0x2E));
        info.Children.Add(icon);

        var texts = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        Grid.SetColumn(texts, 1);
        var ver = new TextBlock { Text = mc, FontSize = 16, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        ver.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        texts.Children.Add(ver);
        texts.Children.Add(new TextBlock
        {
            Text = t.Name,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(accent)
        });
        info.Children.Add(texts);
        grid.Children.Add(info);

        var card = new Border
        {
            Width = _cardW,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Tag = mc,
            Child = grid,
            ToolTip = $"{t.Name} · {string.Join("  ", t.Chips)}"
        };
        StyleCard(card, false);

        card.MouseEnter += (_, _) => { if (!IsSelectedCard(card)) card.SetResourceReference(Border.BackgroundProperty, "SurfaceHiBrush"); };
        card.MouseLeave += (_, _) => StyleCard(card, IsSelectedCard(card));
        card.MouseLeftButtonUp += (_, _) => { if (!_busy) VersionList.SelectedItem = mc; };
        return card;
    }

    private bool IsSelectedCard(Border card) =>
        VersionList.SelectedItem is string s && card.Tag is string mc && s == mc;

    private static void StyleCard(Border card, bool selected)
    {
        card.SetResourceReference(Border.BackgroundProperty, selected ? "AccB1_33" : "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "AccB1_FF" : "LineBrush");
    }

    // ---------- Seleção + destaque ----------

    private void OnVersionSelected()
    {
        if (!_state.PlayDetailed || VersionDetailView == null) return;

        var selected = VersionList.SelectedItem as string;
        foreach (var kv in _versionCards) StyleCard(kv.Value, kv.Key == selected);

        if (selected == null)
        {
            HeroHost.Child = null;
            HeroHost.Visibility = Visibility.Collapsed;
            _heroFor = null;
            return;
        }

        HeroHost.Visibility = Visibility.Visible;
        if (_versionCards.TryGetValue(selected, out var card)) card.BringIntoView();

        var key = selected + "|" + _loader + "|" + InstalledVersionConverter.Installed.Contains(selected);
        if (_heroFor == key) return;
        _heroFor = key;
        HeroHost.Child = MakeHero(selected);
    }

    private UIElement MakeHero(string mc)
    {
        var t = VersionLore.For(mc);
        var accent = ThemeManager.ParseColor(t.Accent, Colors.White);
        var installed = InstalledVersionConverter.Installed.Contains(mc);

        var root = new Grid();

        // Banner grande + sombra para o texto ficar legível por cima
        root.Children.Add(PixelBorder(t.Art, false, 16, 16));
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)),
            Background = new LinearGradientBrush(Color.FromArgb(0xF0, 8, 8, 22), Color.FromArgb(0x20, 8, 8, 22), 0)
        });

        var content = new Grid { Margin = new Thickness(18, 12, 18, 12) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = PixelBorder(t.Art, true, 9, 9);
        icon.Width = 64;
        icon.Height = 64;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 16, 0);
        icon.BorderThickness = new Thickness(2.5);
        icon.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x0C, 0x0B, 0x1E));
        content.Children.Add(icon);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stack, 1);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = "Minecraft " + mc,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        title.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(9, 2, 9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(installed ? Color.FromArgb(0xE6, 0x1E, 0x8E, 0x4E) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock
            {
                Text = installed ? "✔ Já baixada" : "⬇ Baixa ao jogar",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            }
        });
        title.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(9, 2, 9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock { Text = Info[_loader].Title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
        });
        stack.Children.Add(title);

        stack.Children.Add(new TextBlock
        {
            Text = t.Emoji + "  " + t.Name,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 3, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = t.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE2, 0xFA)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var chip in t.Chips)
        {
            chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(9, 2, 9, 3),
                Background = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = chip, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
            });
        }
        stack.Children.Add(chips);
        content.Children.Add(stack);
        root.Children.Add(content);

        return root;
    }
}
