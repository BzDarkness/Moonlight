using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MoonlightLauncher;

/// <summary>Lista as versões do Minecraft que cada loader (Fabric, Forge, NeoForge, Quilt) realmente suporta.</summary>
internal static class VersionCatalog
{
    private static readonly Regex Release = new(@"^\d+\.\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Dictionary<string, List<string>> Cache = new();
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher/1.1");
        return c;
    }

    public static async Task<List<string>> GetAsync(string loader, IReadOnlyList<string> fallback)
    {
        loader = (loader ?? "").ToLowerInvariant();
        if (Cache.TryGetValue(loader, out var cached)) return cached;

        List<string>? list = null;
        try
        {
            list = loader switch
            {
                "fabric" => await FromGameList("https://meta.fabricmc.net/v2/versions/game"),
                "quilt" => await FromGameList("https://meta.quiltmc.org/v3/versions/game"),
                "forge" => await FromForge(),
                "neoforge" => await FromNeoForge(),
                _ => null
            };
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Não consegui a lista de versões do {loader} (uso a lista do launcher)", ex);
        }

        if (list == null || list.Count == 0)
        {
            list = FilterFallback(loader, fallback);
        }
        else
        {
            list = Sort(list);
            Cache[loader] = list; // só guarda quando veio da internet
        }
        return list;
    }

    private static List<string> FilterFallback(string loader, IReadOnlyList<string> all)
    {
        var min = loader switch
        {
            "forge" => "1.5.2",
            "fabric" or "quilt" => "1.14",
            "neoforge" => "1.20.2",
            _ => "0.0"
        };
        return all.Where(v => Version.TryParse(v, out var a) && Version.TryParse(min, out var b) && a >= b).ToList();
    }

    private static List<string> Sort(IEnumerable<string> list) =>
        list.Where(v => Release.IsMatch(v)).Distinct()
            .Select(v => (Name: v, Ver: Version.TryParse(v, out var x) ? x : new Version(0, 0)))
            .OrderByDescending(t => t.Ver)
            .Select(t => t.Name)
            .ToList();

    // Fabric e Quilt: [{"version":"1.21.4","stable":true}, ...]
    private static async Task<List<string>> FromGameList(string url)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var list = new List<string>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!e.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) continue;
            if (e.TryGetProperty("stable", out var st) && st.ValueKind == JsonValueKind.False) continue;
            list.Add(v.GetString() ?? "");
        }
        return list;
    }

    // Forge: o arquivo de metadados tem uma chave por versão do Minecraft.
    private static async Task<List<string>> FromForge()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(
            "https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json"));
        var list = new List<string>();
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var p in doc.RootElement.EnumerateObject()) list.Add(p.Name);
        return list;
    }

    // NeoForge: 21.1.209 -> Minecraft 1.21.1 ; 20.4.x -> 1.20.4 ; 26.1.x -> 26.1
    private static async Task<List<string>> FromNeoForge()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge"));
        var set = new HashSet<string>();
        if (doc.RootElement.TryGetProperty("versions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var v = e.GetString() ?? "";
                var m = Regex.Match(v, @"^(\d+)\.(\d+)");
                if (!m.Success) continue;
                var a = int.Parse(m.Groups[1].Value);
                var b = int.Parse(m.Groups[2].Value);
                set.Add(a >= 26 ? $"{a}.{b}" : b == 0 ? $"1.{a}" : $"1.{a}.{b}");
            }
        }
        return set.ToList();
    }
}

/// <summary>
/// Caixa de versão que funciona de dois jeitos: escrevendo (filtra a lista) ou tocando na seta
/// (abre a lista com todas as versões disponíveis para o loader escolhido).
/// </summary>
internal sealed class VersionPicker
{
    private readonly TextBox _box;
    private readonly Func<string> _loader;
    private readonly Func<IReadOnlyList<string>> _fallback;
    private readonly Action<string>? _picked;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBlock _title;
    private List<string> _all = new();
    private bool _setting;

    public VersionPicker(FrameworkElement window, TextBox box, Button arrow, Func<string> loader,
        Func<IReadOnlyList<string>> fallback, Action<string>? picked = null)
    {
        _box = box;
        _loader = loader;
        _fallback = fallback;
        _picked = picked;

        _title = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 2, 6, 8) };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MaxHeight = 300,
            Width = 190
        };
        _list.ItemContainerStyle = (Style)window.FindResource("PickItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) =>
        {
            if (_setting || _list.SelectedItem is not string v || v.Length == 0 || !char.IsDigit(v[0])) return;
            _popup!.IsOpen = false;
            Choose(v);
        };

        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_list);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 6, 16, 16),
            BorderThickness = new Thickness(1),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.4 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        _popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = card
        };

        arrow.Click += async (_, _) => await ToggleAsync();
        box.TextChanged += (_, _) =>
        {
            if (_popup.IsOpen && !_setting) Fill(_box.Text.Trim());
        };
        box.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Down && !_popup.IsOpen) { e.Handled = true; await ToggleAsync(); }
            else if (e.Key == Key.Escape && _popup.IsOpen) { _popup.IsOpen = false; e.Handled = true; }
        };
    }

    private void Choose(string v)
    {
        _setting = true;
        _box.Text = v;
        _box.CaretIndex = v.Length;
        _setting = false;
        _picked?.Invoke(v);
    }

    private async Task ToggleAsync()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }

        var loader = _loader();
        _title.Text = loader.Length == 0 ? "Todas as versões do Minecraft" : $"Versões com suporte a {Pretty(loader)}";
        _all = new List<string>();
        _list.ItemsSource = new[] { "Carregando…" };
        _popup.IsOpen = true;

        _all = await VersionCatalog.GetAsync(loader, _fallback());
        if (_popup.IsOpen) Fill("");
    }

    private void Fill(string filter)
    {
        var items = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(v => v.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ||
                              v.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _setting = true;
        _list.ItemsSource = items.Count == 0 ? new List<string> { "Nenhuma versão" } : items;
        _list.SelectedItem = items.FirstOrDefault(v => v == _box.Text.Trim());
        _setting = false;
    }

    private static string Pretty(string l) => l.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => l
    };
}
