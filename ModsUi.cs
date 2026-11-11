using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoonlightLauncher;

// ---------- Cor de cada mod ----------
// Usada na faixa fina do topo do cartão, na barrinha da lista e nas etiquetas.
// Vem do Modrinth (campo "color") ou, quando não existe, é tirada da foto do mod.
public sealed class AccentInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Real { get; private set; }
    public Color Color { get; private set; }
    public Brush Strip { get; private set; } = Brushes.Gray;
    public Brush Soft { get; private set; } = Brushes.Transparent;
    public Brush Edge { get; private set; } = Brushes.Gray;

    public AccentInfo(int rgb, string seed)
    {
        if (rgb != 0)
        {
            Real = true;
            Apply(AccentMath.Tame(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)));
        }
        else
        {
            Apply(AccentMath.FromSeed(seed ?? ""));
        }
    }

    public void Update(Color c)
    {
        Real = true;
        Apply(AccentMath.Tame(c));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private void Apply(Color c)
    {
        Color = c;
        var next = AccentMath.Shift(c);

        var g = new LinearGradientBrush(c, next, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        Strip = g;

        var soft = new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B));
        soft.Freeze();
        Soft = soft;

        var edge = new SolidColorBrush(Color.FromArgb(0x90, c.R, c.G, c.B));
        edge.Freeze();
        Edge = edge;
    }
}

public static class AccentMath
{
    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    // Deixa a cor agradável de ver (nem escura demais, nem estourada).
    public static Color Tame(Color c)
    {
        var (h, s, v) = ToHsv(c);
        if (s > 0.12) { s = Math.Clamp(s, 0.40, 0.85); v = Math.Clamp(v, 0.50, 0.92); }
        else v = Math.Clamp(v, 0.38, 0.72);
        return FromHsv(h, s, v);
    }

    // Segunda cor do degradê: um pouco mais escura e com o tom levemente girado.
    public static Color Shift(Color c)
    {
        var (h, s, v) = ToHsv(c);
        return FromHsv(h + 28, Math.Clamp(s + 0.05, 0, 1), Math.Clamp(v * 0.62, 0.2, 1));
    }

    // Cor provisória (até a foto ser analisada): sai do nome do mod, então é sempre a mesma para o mesmo mod.
    public static Color FromSeed(string seed)
    {
        uint h = 2166136261;
        foreach (var ch in seed) h = (h ^ ch) * 16777619;
        return FromHsv(h % 360, 0.45, 0.62);
    }

    // Cor dominante de uma foto: agrupa os pixels por tom, dando mais peso aos vivos e ignorando fundo transparente.
    public static Color? Dominant(byte[] data)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(data);
        bi.DecodePixelWidth = 40;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();

        var fc = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
        var w = fc.PixelWidth;
        var h = fc.PixelHeight;
        var stride = w * 4;
        var px = new byte[stride * h];
        fc.CopyPixels(px, stride, 0);

        const int N = 12;
        var wt = new double[N];
        var sr = new double[N];
        var sg = new double[N];
        var sb = new double[N];
        double ar = 0, ag = 0, ab = 0, an = 0;

        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            if (px[i + 3] < 128) continue;
            var c = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
            var (hue, s, v) = ToHsv(c);
            ar += c.R; ag += c.G; ab += c.B; an++;
            if (v < 0.15 || s < 0.15) continue;

            var weight = s * s * v;
            var k = (int)(hue / 30) % N;
            wt[k] += weight;
            sr[k] += c.R * weight;
            sg[k] += c.G * weight;
            sb[k] += c.B * weight;
        }

        if (an == 0) return null;

        var best = 0;
        for (var k = 1; k < N; k++) if (wt[k] > wt[best]) best = k;

        // Foto quase toda cinza: usa a média simples.
        if (wt[best] < an * 0.02)
            return Color.FromRgb((byte)(ar / an), (byte)(ag / an), (byte)(ab / an));

        return Color.FromRgb((byte)(sr[best] / wt[best]), (byte)(sg[best] / wt[best]), (byte)(sb[best] / wt[best]));
    }
}

// Transforma o endereço de uma imagem em figura. O tamanho (parâmetro) limita a memória usada.
public sealed class UrlImageConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || url.Length == 0) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;

        var width = int.TryParse(parameter as string, out var p) ? p : 0;
        var key = width + "|" + url;
        if (Cache.TryGetValue(key, out var hit)) return hit;
        if (Cache.Count > 300) Cache.Clear();

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = uri;
            if (width > 0) bi.DecodePixelWidth = width;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            Cache[key] = bi;
            return bi;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class MainWindow
{
    // ---------- Ordenar ----------

    private static readonly (string Key, string Label)[] SortOptions =
    {
        ("relevance", "Relevância"),
        ("downloads", "Mais baixados"),
        ("follows", "Mais seguidos"),
        ("newest", "Mais novos"),
        ("updated", "Atualizados"),
    };

    private string _modsSort = "relevance";
    private int _modsShown;
    private bool _modsViewReady;
    private static readonly ConcurrentDictionary<string, Color> ColorCache = new();
    private static readonly SemaphoreSlim ColorGate = new(4);

    private void InitModsView()
    {
        BuildSortMenu();

        _modsViewReady = false;
        ModsViewList.IsChecked = !_state.ModsDetailed;
        ModsViewCards.IsChecked = _state.ModsDetailed;
        _modsViewReady = true;
        ApplyModsView();
        UpdateTopicsLabel();
    }

    private Border MakeMenuItem(string text, bool selected, Action onPick)
    {
        var tb = new TextBlock
        {
            Text = (selected ? "✓  " : "     ") + text,
            FontSize = 13,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, selected ? "TextBrush" : "Text2Brush");

        var item = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 18, 8),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = tb
        };
        if (selected) item.SetResourceReference(Border.BackgroundProperty, "AccB1_33");

        item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "SurfaceHiBrush");
        item.MouseLeave += (_, _) =>
        {
            if (selected) item.SetResourceReference(Border.BackgroundProperty, "AccB1_33");
            else item.Background = Brushes.Transparent;
        };
        item.MouseLeftButtonUp += (_, _) => onPick();
        return item;
    }

    private void BuildSortMenu()
    {
        SortMenuHost.Children.Clear();
        foreach (var (key, label) in SortOptions)
        {
            var k = key;
            SortMenuHost.Children.Add(MakeMenuItem(label, k == _modsSort, () =>
            {
                _modsSort = k;
                SortPopup.IsOpen = false;
                BuildSortMenu();
                _modsPage = 0;
                _ = SearchModsAsync();
            }));
        }

        var current = SortOptions.First(o => o.Key == _modsSort).Label;
        SortBtn.Content = current + "  ▾";
    }

    private void SortBtn_Click(object sender, RoutedEventArgs e) => SortPopup.IsOpen = true;

    // ---------- Sugestões ----------

    private void TopicsBtn_Click(object sender, RoutedEventArgs e) => TopicsPopup.IsOpen = true;

    private void UpdateTopicsLabel()
    {
        var n = _topics.Count(t => TopicsFor(_modsType).Any(x => x.Id == t));
        TopicsBar.Content = n > 0 ? $"💡  Sugestões ({n})  ▾" : "💡  Sugestões  ▾";
    }

    // ---------- Lista / Detalhado ----------

    private void ModsView_Checked(object sender, RoutedEventArgs e)
    {
        if (!_modsViewReady || sender is not RadioButton rb) return;
        _state.ModsDetailed = rb.Tag?.ToString() == "cards";
        SaveState();
        ApplyModsView();
    }

    private void ApplyModsView()
    {
        var detailed = _state.ModsDetailed;
        ModsList.Visibility = detailed ? Visibility.Collapsed : Visibility.Visible;
        ModsCards.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;

        // Só a lista que está à mostra recebe os itens (a outra ficaria carregando imagens à toa).
        var items = ModsList.ItemsSource ?? ModsCards.ItemsSource;
        ModsList.ItemsSource = detailed ? null : items;
        ModsCards.ItemsSource = detailed ? items : null;
        UpdateCardColumns();
    }

    private void ShowModItems(List<ContentItem>? items)
    {
        SetModsBars(true); // resultado novo = volta ao topo, com todas as barras à mostra
        _scrollLast = 0;
        _modsShown = items?.Count ?? 0;
        var detailed = _state.ModsDetailed;
        ModsList.ItemsSource = detailed ? null : items;
        ModsCards.ItemsSource = detailed ? items : null;

        var active = detailed ? ModsCards : ModsList;
        if (items is { Count: > 0 }) active.ScrollIntoView(items[0]);
        UpdateCardColumns();

        if (items is { Count: > 0 }) _ = ColorizeAsync(items);
    }

    private void ModsResults_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardColumns();

    private void UpdateCardColumns()
    {
        if (ModsCards == null || ModsList == null || ModsResultsGrid == null) return;
        ApplyColumns(ModsCards, CardColumns());
        ApplyColumns(ModsList, ListColumns());
    }

    private void ApplyColumns(ListBox box, int columns)
    {
        var panel = FindVisualChild<UniformGrid>(box);
        if (panel != null)
        {
            if (panel.Columns != columns) panel.Columns = columns;
            return;
        }

        // O painel só existe depois que a lista é desenhada: tenta de novo logo depois.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var p = FindVisualChild<UniformGrid>(box);
            if (p != null) p.Columns = box == ModsCards ? CardColumns() : ListColumns();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Durante a animação da gaveta usamos a largura final (senão as colunas ficariam pulando).
    private double ResultsWidth => _colsWidthOverride ?? ModsResultsGrid.ActualWidth;

    // Quanto mais largo o espaço (gaveta fechada), mais mods aparecem lado a lado.
    private int CardColumns()
    {
        var w = ResultsWidth;
        return w < 620 ? 1 : w < 1000 ? 2 : w < 1500 ? 3 : 4;
    }

    private int ListColumns()
    {
        var w = ResultsWidth;
        return w < 900 ? 1 : w < 1500 ? 2 : 3;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var deeper = FindVisualChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // ---------- Cor tirada da foto do mod ----------

    private async Task ColorizeAsync(List<ContentItem> items)
    {
        var jobs = items
            .Where(i => !i.Accent.Real && i.IconUrl.StartsWith("http"))
            .Select(async i =>
            {
                if (ColorCache.TryGetValue(i.IconUrl, out var cached))
                {
                    i.Accent.Update(cached);
                    return;
                }

                await ColorGate.WaitAsync();
                try
                {
                    var bytes = await ContentApi.GetBytesAsync(i.IconUrl);
                    var color = await Task.Run(() => AccentMath.Dominant(bytes));
                    if (color is Color c)
                    {
                        ColorCache[i.IconUrl] = c;
                        await Dispatcher.InvokeAsync(() => i.Accent.Update(c));
                    }
                }
                catch
                {
                    // sem foto ou foto estranha: fica com a cor provisória
                }
                finally
                {
                    ColorGate.Release();
                }
            });

        await Task.WhenAll(jobs);
    }
}
