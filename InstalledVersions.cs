using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>Mostra o selo verde nas versões que já estão baixadas.</summary>
public class InstalledVersionConverter : IValueConverter
{
    // Conjunto da lista que está na tela (muda quando o usuário troca de modloader).
    public static HashSet<string> Installed { get; set; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && Installed.Contains(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Uma versão que existe na pasta .minecraft/versions (Mc = versão do Minecraft a que ela pertence).</summary>
public sealed record LocalVersion(string Id, string Mc, Loader Kind);

public partial class MainWindow
{
    // ForgeOptiFine usa a mesma instalação do Forge.
    private static Loader KindFor(Loader l) => l == Loader.ForgeOptiFine ? Loader.Forge : l;

    // Olha a pasta versions e descobre o que já está baixado (Vanilla, Fabric, Forge, NeoForge).
    // As instalações valem para QUALQUER conta: a pasta .minecraft é compartilhada.
    private List<LocalVersion> ScanLocalVersions()
    {
        var list = new List<LocalVersion>();
        var dir = IOPath.Combine(_root, "versions");
        if (!Directory.Exists(dir)) return list;

        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            var id = IOPath.GetFileName(d);

            if (VanillaName.IsMatch(id))
            {
                if (File.Exists(IOPath.Combine(d, id + ".jar")))
                    list.Add(new LocalVersion(id, id, Loader.Vanilla));
                continue;
            }

            var jsonPath = IOPath.Combine(d, id + ".json");
            if (!File.Exists(jsonPath)) continue;

            Loader kind;
            if (id.Contains("optifine", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("forge", StringComparison.OrdinalIgnoreCase)) kind = Loader.OptiFine;
            else if (id.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase)) kind = Loader.Fabric;
            else if (id.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) kind = Loader.NeoForge;
            else if (id.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                     !id.Contains("optifine", StringComparison.OrdinalIgnoreCase)) kind = Loader.Forge;
            else continue;

            string? mc = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                foreach (var prop in new[] { "inheritsFrom", "jar" })
                {
                    if (doc.RootElement.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        mc = p.GetString();
                        break;
                    }
                }
            }
            catch
            {
                continue; // json quebrado: ignora
            }

            if (string.IsNullOrWhiteSpace(mc))
            {
                var m = Regex.Match(id, @"\d+\.\d+(\.\d+)?");
                if (!m.Success) continue;
                mc = m.Value;
            }

            // O Minecraft base precisa estar baixado também.
            if (!File.Exists(IOPath.Combine(dir, mc, mc + ".jar"))) continue;

            list.Add(new LocalVersion(id, mc, kind));
        }

        return list;
    }

    // Para escolher o loader mais novo quando há mais de um instalado.
    private static Version VersionKey(LocalVersion v)
    {
        var groups = Regex.Matches(v.Id, @"\d+(?:\.\d+)+").Select(m => m.Value).ToList();
        var pick = v.Kind == Loader.Forge ? groups.LastOrDefault() : groups.FirstOrDefault();
        return Version.TryParse(pick, out var ver) ? ver : new Version(0, 0);
    }

    private LocalVersion? FindLocalVersion(string mc, Loader loader)
    {
        var kind = KindFor(loader);
        return ScanLocalVersions()
            .Where(v => v.Kind == kind && v.Mc == mc)
            .OrderByDescending(VersionKey)
            .FirstOrDefault();
    }

    // Atualiza o conjunto que pinta de verde as versões da lista atual.
    private void RefreshInstalledSet()
    {
        // OptiFine e Forge + OptiFine: verde = já existe a instalação (o OptiFine puro fica em versions;
        // no Forge + OptiFine vale a do Forge).
        var kind = _loader == Loader.ForgeOptiFine ? Loader.Forge : KindFor(_loader);
        InstalledVersionConverter.Installed = ScanLocalVersions()
            .Where(v => v.Kind == kind)
            .Select(v => v.Mc)
            .ToHashSet();
    }

    // Repinta a lista sem perder a versão selecionada (ex.: depois de baixar uma versão nova).
    private void RefreshVersionMarks()
    {
        RefreshInstalledSet();

        var selected = VersionList.SelectedItem;
        CollectionViewSource.GetDefaultView(VersionList.ItemsSource)?.Refresh();
        if (selected != null) VersionList.SelectedItem = selected;

        RebuildVersionCards(); // atualiza o "✔ Baixada" dos cartões
    }
}
