using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MoonlightLauncher;

// Troca de ícones (só com o Golden Moon ativo):
//  • ícone do aplicativo = o ícone da janela e da barra de tarefas;
//  • mini lua = a lua da barra lateral e da tela de abertura.
// O ícone do arquivo .exe no Windows é gravado na hora de gerar o programa e não muda enquanto ele roda.
public partial class MainWindow
{
    private sealed record IconDef(string Key, string Label, string File);

    private static readonly IconDef[] IconDefs =
    {
        new("", "Original", ""),
        new("redstone", "Redstone", "icons/icon_redstone.png"),
        new("anjo", "Anjo", "icons/icon_anjo.png"),
        new("mel", "Mel", "icons/icon_mel.png"),
        new("orbita", "Órbita", "icons/icon_orbita.png"),
        new("obsidiana", "Obsidiana", "icons/icon_obsidiana.png"),
        new("rei", "Rei", "icons/icon_rei.png"),
    };

    private ImageSource? _defaultAppIcon;
    private readonly Dictionary<string, BitmapImage> _assetCache = new();
    private readonly List<(Border Tile, string Key, bool IsApp)> _iconTiles = new();

    private static string IconFileOf(string key)
    {
        foreach (var d in IconDefs)
            if (d.Key == key) return d.File;
        return "";
    }

    // Carrega uma imagem de dentro do programa (pasta Assets) e guarda para não carregar duas vezes.
    private BitmapImage LoadAsset(string rel)
    {
        if (_assetCache.TryGetValue(rel, out var hit)) return hit;

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri("pack://application:,,,/Assets/" + rel, UriKind.Absolute);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        _assetCache[rel] = bi;
        return bi;
    }

    // ---------- Seletor (aba Ajustes) ----------

    private void BuildIconPickers()
    {
        IconAppHost.Children.Clear();
        IconMoonHost.Children.Clear();
        _iconTiles.Clear();

        foreach (var def in IconDefs)
        {
            IconAppHost.Children.Add(MakeIconTile(def, true));
            IconMoonHost.Children.Add(MakeIconTile(def, false));
        }
        RefreshIconTiles();
    }

    private Border MakeIconTile(IconDef def, bool isApp)
    {
        var img = new Image
        {
            Width = 52,
            Height = 52,
            Stretch = Stretch.Uniform,
            Source = LoadAsset(def.File.Length > 0 ? def.File : "logo.png")
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var label = new TextBlock
        {
            Text = def.Label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");

        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(label);

        var tile = new Border
        {
            Width = 86,
            Height = 96,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(6, 8, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = def.Key.Length == 0 ? "Original do Moonlight" : def.Label,
            Child = stack
        };
        tile.MouseLeftButtonUp += (_, _) => PickIcon(def.Key, isApp);

        _iconTiles.Add((tile, def.Key, isApp));
        return tile;
    }

    private void RefreshIconTiles()
    {
        foreach (var (tile, key, isApp) in _iconTiles)
        {
            var chosen = (isApp ? _state.AppIcon : _state.MoonIcon) ?? "";
            var selected = key == chosen;
            tile.SetResourceReference(Border.BorderBrushProperty, selected ? "AccB1_FF" : "LineBrush");
            tile.SetResourceReference(Border.BackgroundProperty, selected ? "AccB1_33" : "SurfaceBrush");
        }
    }

    private void PickIcon(string key, bool isApp)
    {
        if (!GoldenActive)
        {
            GoTo("Golden");
            return;
        }

        if (isApp) _state.AppIcon = key; else _state.MoonIcon = key;
        SaveState();
        ApplyIconChoices();
        RefreshIconTiles();

        IconInfo.Text = isApp
            ? "✔ Ícone do aplicativo trocado (janela e barra de tarefas)."
            : "✔ Mini lua trocada.";

        if (!isApp && !Perf.Light)
            SideLogo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)));
    }

    // ---------- Aplicar ----------

    // Sem o Golden Moon (ou se o arquivo falhar) volta sempre para os ícones originais.
    private void ApplyIconChoices()
    {
        _defaultAppIcon ??= Icon;

        var on = GoldenActive;
        var appFile = on ? IconFileOf(_state.AppIcon ?? "") : "";
        var moonFile = on ? IconFileOf(_state.MoonIcon ?? "") : "";

        try
        {
            Icon = appFile.Length > 0 ? LoadAsset(appFile) : _defaultAppIcon;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui trocar o ícone do aplicativo", ex);
        }

        try
        {
            var moon = LoadAsset(moonFile.Length > 0 ? moonFile : "logo.png");
            SideLogo.Source = moon;
            SplashMoon.Source = moon;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui trocar a mini lua", ex);
        }

        QueueShortcutSync(); // o atalho da área de trabalho usa o mesmo ícone do aplicativo
    }
}
