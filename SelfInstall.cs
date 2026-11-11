using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>
/// O launcher mora numa pasta escondida (%LocalAppData%\MoonlightLauncher\app) e só o ATALHO fica na área de trabalho.
/// Na primeira abertura, o .exe baixado copia a si mesmo para lá, abre a cópia, apaga o arquivo baixado
/// (se estiver na Área de Trabalho ou em Downloads) e limpa restos de atualizações antigas.
/// As atualizações automáticas passam a trocar o .exe dentro dessa pasta, sem tocar na área de trabalho.
/// </summary>
internal static class SelfInstall
{
    public const string ExeName = "MoonlightLauncher.exe";

    /// <summary>Verdadeiro quando esta execução acabou de ser instalada (o atalho é criado de novo mesmo que tenha sido apagado antes).</summary>
    public static bool JustInstalled { get; private set; }

    public static string InstallDir => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonlightLauncher", "app");

    public static string InstalledExe => IOPath.Combine(InstallDir, ExeName);

    private static bool Same(string a, string b) =>
        string.Equals(IOPath.GetFullPath(a), IOPath.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static Version VersionOf(string path)
    {
        try
        {
            var text = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return Version.TryParse(text, out var v) ? v : new Version(0, 0);
        }
        catch { return new Version(0, 0); }
    }

    /// <summary>
    /// Chamado no início de tudo. Devolve true se abriu a cópia instalada: quem chamou deve fechar este processo.
    /// Em qualquer falha devolve false e o launcher simplesmente roda de onde está.
    /// </summary>
    public static bool RelocateIfNeeded(string[] args)
    {
        try
        {
            if (args.Contains("--sem-instalar")) return false;

            var cur = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(cur) || !File.Exists(cur)) return false;
            if (!cur.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (IOPath.GetFileName(cur).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;

            // Já é a cópia instalada: segue normal (e, se acabou de ser instalada, limpa a bagunça de antes).
            if (Same(cur, InstalledExe))
            {
                var i = Array.IndexOf(args, "--instalado");
                if (i >= 0)
                {
                    JustInstalled = true;
                    var origem = i + 1 < args.Length ? args[i + 1] : "";
                    _ = Task.Run(() => CleanAfterInstall(origem));
                }
                return false;
            }

            // Compilação de desenvolvimento (dotnet run / Visual Studio): não instala.
            var low = cur.Replace('/', '\\').ToLowerInvariant();
            if (low.Contains("\\bin\\debug\\") || low.Contains("\\bin\\release\\")) return false;

            Directory.CreateDirectory(InstallDir);
            var installed = InstalledExe;

            // Só copia se não há cópia ou se a baixada é mais nova.
            if (!File.Exists(installed) || VersionOf(installed) < VersionOf(cur))
            {
                var novo = installed + ".novo";
                File.Copy(cur, novo, true);
                if (File.Exists(installed))
                    File.Move(installed, $"{installed}.{DateTime.Now.Ticks}.old"); // o Windows deixa renomear um .exe aberto
                File.Move(novo, installed);
            }

            Process.Start(new ProcessStartInfo(installed)
            {
                UseShellExecute = false,
                WorkingDirectory = InstallDir,
                ArgumentList = { "--instalado", cur }
            });
            return true;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui instalar o launcher na pasta escondida (continua de onde está)", ex);
            return false;
        }
    }

    // ---------- Depois de instalado ----------

    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static string Downloads => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static async Task CleanAfterInstall(string origem)
    {
        try
        {
            // O .exe baixado ainda está fechando: tenta apagar por alguns segundos.
            if (origem.Length > 0 && File.Exists(origem) && !Same(origem, InstalledExe))
            {
                var dir = IOPath.GetFullPath(IOPath.GetDirectoryName(origem) ?? "").TrimEnd('\\');
                var ok = dir.Equals(IOPath.GetFullPath(Desktop).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                      || dir.Equals(IOPath.GetFullPath(Downloads).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (ok)
                {
                    for (int i = 0; i < 20 && File.Exists(origem); i++)
                    {
                        try { File.Delete(origem); } catch { await Task.Delay(500); }
                    }
                }
            }

            CleanDesktopMess();
        }
        catch (Exception ex) { LauncherLog.Write("Limpeza depois de instalar", ex); }
    }

    /// <summary>Apaga da área de trabalho os restos de atualizações antigas (.old, .tmp), cópias do .exe e atalhos repetidos.</summary>
    public static void CleanDesktopMess()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Desktop))
            {
                var n = IOPath.GetFileName(f);
                var kill =
                    Regex.IsMatch(n, @"^MoonlightLauncher.*\.(old|tmp)$", RegexOptions.IgnoreCase) ||
                    n.Equals("MoonlightLauncher.update.exe", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(n, @"^MoonlightLauncher(\s*\(\d+\))?\.exe$", RegexOptions.IgnoreCase) ||
                    (Regex.IsMatch(n, @"^Moonlight ?Launcher(\s*\(\d+\))?\.lnk$", RegexOptions.IgnoreCase)
                     && !n.Equals("Moonlight Launcher.lnk", StringComparison.OrdinalIgnoreCase));

                if (!kill) continue;
                try { File.Delete(f); } catch { /* em uso: fica para a próxima */ }
            }
        }
        catch (Exception ex) { LauncherLog.Write("Limpeza da área de trabalho", ex); }
    }
}
