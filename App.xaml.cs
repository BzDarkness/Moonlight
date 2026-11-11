using System.IO;
using System.Windows;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public partial class App : Application
{
    // Mostra a causa real: desce até a exceção mais interna (a de fora costuma ser só "falhou ao criar").
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null && parts.Count < 4; e = e.InnerException)
            parts.Add(e.GetType().Name + ": " + e.Message);
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        var where = (inner.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim();
        return string.Join("\n  → ", parts) + (string.IsNullOrEmpty(where) ? "" : "\n" + where);
    }

    private static bool _showingError;
    private static int _errorCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Em vez de o launcher fechar sozinho, mostra o erro e grava no launcher.log.
        DispatcherUnhandledException += (_, ev) =>
        {
            LauncherLog.Write("ERRO NÃO TRATADO (interface)", ev.Exception);
            ev.Handled = true;

            // Só 1 aviso por vez e no máximo 3 por execução (evita a enxurrada de janelas).
            if (_showingError || _errorCount >= 3) return;
            _errorCount++;
            _showingError = true;
            try
            {
                var stack = (ev.Exception.StackTrace ?? "").Split('\n').Take(6);
                MessageBox.Show(
                    "O launcher encontrou um erro, mas continua aberto.\n\n" +
                    Describe(ev.Exception) + "\n\n" +
                    string.Join("\n", stack) +
                    "\n\nDetalhes salvos em:\n" + LauncherLog.FilePath,
                    "Moonlight Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { _showingError = false; }
        };

        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            LauncherLog.Write("ERRO EM TAREFA", ev.Exception);
            ev.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            LauncherLog.Write("ERRO FATAL", ev.ExceptionObject as Exception);

        // O launcher fica numa pasta escondida e só o atalho aparece na área de trabalho.
        // Se acabou de abrir a cópia instalada, este processo fecha aqui.
        if (SelfInstall.RelocateIfNeeded(e.Args))
        {
            Environment.Exit(0);
            return;
        }

        Updater.CleanupLeftovers(); // apaga o .exe antigo de uma atualização anterior
        Perf.Initialize(); // modo de desempenho (antes de qualquer janela abrir)
        ThemeManager.Initialize(); // tema e cores (antes de qualquer janela abrir)

        base.OnStartup(e);
    }
}

public static class LauncherLog
{
    public static readonly string FilePath = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MoonlightLauncher", "launcher.log");

    private static readonly object Gate = new();

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath)!);

                // Evita o arquivo crescer sem fim.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    File.Delete(FilePath);

                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n" +
                    (ex == null ? "" : ex + "\n") + "\n");
            }
        }
        catch { /* log nunca pode derrubar o launcher */ }
    }
}
