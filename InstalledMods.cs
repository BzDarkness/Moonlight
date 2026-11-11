using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>Um mod que está na pasta "mods" (ativo = .jar, desativado = .jar.disabled).</summary>
public class InstalledMod : INotifyPropertyChanged
{
    private string _filePath = "";
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            Raise(nameof(FilePath));
            Raise(nameof(FileName));
        }
    }

    public string FileName => IOPath.GetFileName(_filePath);

    public string DisplayName { get; init; } = "";
    public string Detail { get; init; } = "";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            Raise(nameof(IsActive));
        }
    }
}

public static class ModLibrary
{
    private const string DisabledSuffix = ".disabled";

    public static string DisabledPath(string path) =>
        path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase) ? path : path + DisabledSuffix;

    public static string ActivePath(string path) =>
        path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase) ? path[..^DisabledSuffix.Length] : path;

    public static List<InstalledMod> Scan(string dir)
    {
        var result = new List<InstalledMod>();
        if (!Directory.Exists(dir)) return result;

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = IOPath.GetFileName(file);
            var active = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            var disabled = name.EndsWith(".jar" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (!active && !disabled) continue;

            var jarName = disabled ? name[..^DisabledSuffix.Length] : name;
            var fallback = IOPath.GetFileNameWithoutExtension(jarName);

            var (title, version) = ReadMeta(file);

            long size = 0;
            try { size = new FileInfo(file).Length; } catch { /* ignora */ }

            var detail = string.IsNullOrWhiteSpace(version)
                ? FormatSize(size)
                : $"{version}  ·  {FormatSize(size)}";

            result.Add(new InstalledMod
            {
                FilePath = file,
                DisplayName = string.IsNullOrWhiteSpace(title) ? fallback : title!,
                Detail = detail,
                IsActive = active
            });
        }

        return result
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(bytes / 1024, 1)} KB";

    private static bool IsPlaceholder(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Contains("${");

    // Lê o nome bonito e a versão de dentro do .jar (Fabric, Quilt, Forge, NeoForge).
    private static (string? Name, string? Version) ReadMeta(string jar)
    {
        try
        {
            using var fs = new FileStream(jar, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            if (zip.GetEntry("fabric.mod.json") is { } fabric)
            {
                var r = ParseFabric(ReadText(fabric));
                if (!IsPlaceholder(r.Name)) return r;
            }

            if (zip.GetEntry("quilt.mod.json") is { } quilt)
            {
                var r = ParseQuilt(ReadText(quilt));
                if (!IsPlaceholder(r.Name)) return r;
            }

            var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
            if (toml != null) return ParseToml(ReadText(toml));
        }
        catch
        {
            // jar estranho/corrompido: usa só o nome do arquivo
        }

        return (null, null);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        if (entry.Length > 2_000_000) return "";
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static (string? Name, string? Version) ParseFabric(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            var version = Str(doc.RootElement, "version");
            return (Str(doc.RootElement, "name") ?? Str(doc.RootElement, "id"), IsPlaceholder(version) ? null : version);
        }
        catch
        {
            return (null, null);
        }
    }

    private static (string? Name, string? Version) ParseQuilt(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            if (!doc.RootElement.TryGetProperty("quilt_loader", out var ql)) return (null, null);

            string? name = null;
            if (ql.TryGetProperty("metadata", out var meta)) name = Str(meta, "name");
            var version = Str(ql, "version");
            return (name ?? Str(ql, "id"), IsPlaceholder(version) ? null : version);
        }
        catch
        {
            return (null, null);
        }
    }

    private static (string? Name, string? Version) ParseToml(string toml)
    {
        var name = Regex.Match(toml, "(?m)^\\s*displayName\\s*=\\s*[\"']([^\"']+)[\"']");
        var ver = Regex.Match(toml, "(?m)^\\s*version\\s*=\\s*[\"']([^\"']+)[\"']");

        var version = ver.Success ? ver.Groups[1].Value : null;
        return (name.Success ? name.Groups[1].Value : null, IsPlaceholder(version) ? null : version);
    }
}

public partial class MainWindow
{
    private async Task RefreshInstalledModsAsync()
    {
        var dir = IOPath.Combine(_root, "mods");

        List<InstalledMod> list;
        try
        {
            list = await Task.Run(() => ModLibrary.Scan(dir));
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Erro ao listar os mods instalados", ex);
            list = new List<InstalledMod>();
        }

        InstalledList.ItemsSource = list;
        InstalledEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstalledSummary();
    }

    private void UpdateInstalledSummary()
    {
        var list = InstalledList.ItemsSource as List<InstalledMod>;
        DrawerCount.Text = (list?.Count ?? 0).ToString();
        if (list == null || list.Count == 0)
        {
            InstalledSummary.Text = "";
            return;
        }

        var on = list.Count(m => m.IsActive);
        var off = list.Count - on;
        InstalledSummary.Text =
            $"{on} {(on == 1 ? "ativo" : "ativos")}  ·  {off} {(off == 1 ? "desativado" : "desativados")}";
    }

    private void InstalledRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshInstalledModsAsync();

    private void InstalledFolder_Click(object sender, RoutedEventArgs e) => OpenFolder("mods");

    // Ativar/desativar = renomear o arquivo (.jar <-> .jar.disabled). O jogo só carrega os .jar.
    private void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.Tag is not InstalledMod mod) return;

        var enable = toggle.IsChecked == true;
        try
        {
            var target = enable ? ModLibrary.ActivePath(mod.FilePath) : ModLibrary.DisabledPath(mod.FilePath);

            if (!string.Equals(target, mod.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(target))
                    throw new IOException("Já existe outro arquivo com esse nome: " + IOPath.GetFileName(target));

                File.Move(mod.FilePath, target);
                mod.FilePath = target;
            }

            mod.IsActive = enable;
            ModsStatus.Text = enable
                ? $"✔ {mod.DisplayName} ativado."
                : $"{mod.DisplayName} desativado (o arquivo continua na pasta).";
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Erro ao alternar o mod {mod.FileName}", ex);
            mod.IsActive = !enable; // volta o interruptor ao estado real
            ModsStatus.Text = "Não consegui mudar o mod: " + ex.Message;
        }

        UpdateInstalledSummary();
    }
}
