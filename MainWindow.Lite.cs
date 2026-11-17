using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CmlLib.Core.Auth.Microsoft;

namespace MoonlightLauncher;

// Interface LEVE (ver LiteWindow.cs): quando o jogador escolhe "Leve" em Ajustes, o launcher troca esta janela grande
// por uma janelinha simples, parecida com a do Legacy Launcher. A MainWindow continua existindo (escondida) porque
// é ela que guarda as contas, as versões e sabe instalar e abrir o jogo; a janelinha só pede essas coisas.
public partial class MainWindow
{
    private LiteWindow? _lite;
    private bool _startLite; // o launcher abriu com o modo Leve já escolhido

    // ---------- Entrar e sair do modo leve ----------

    // Abrindo em modo leve a janela grande nunca aparece: nasce fora da tela, sem botão na barra de tarefas.
    private void PrepareLiteStart()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        ShowInTaskbar = false;
    }

    private void EnterLiteMode()
    {
        if (_lite != null) return;

        try
        {
            _lite = new LiteWindow(this);
            _lite.Show();
            _lite.Activate();
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Interface leve falhou ao abrir (volta para a completa)", ex);
            try { _lite?.Close(); } catch { /* */ }
            _lite = null;
            ShowFullWindow();
            return;
        }

        Hide(); // a janela grande some (ela fica só trabalhando por trás)
        Dispatcher.BeginInvoke(() => Perf.TrimMemory(), DispatcherPriority.ApplicationIdle);
    }

    private void ExitLiteMode()
    {
        var l = _lite;
        if (l == null) return;

        _lite = null;
        l.Switching = true; // fechar a janelinha agora não fecha o launcher
        try { l.Close(); } catch { /* */ }
        ShowFullWindow();
    }

    private void ShowFullWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
        Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        ShowInTaskbar = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    // ---------- O que a janelinha pode ler ----------

    internal static string LiteLoaderTitle(Loader l) => Info[l].Title;

    internal bool LiteBusy => _busy;
    internal string LiteStatus => StatusText.Text ?? "";
    internal Loader LiteLoader => _loader;
    internal IReadOnlyList<string> LiteVersions => _current;
    internal string? LiteSelectedVersion => VersionList.SelectedItem as string;
    internal bool LiteIsInstalled(string v) => InstalledVersionConverter.Installed.Contains(v);
    internal IReadOnlyList<Account> LiteAccounts => _state.Accounts;
    internal Account? LiteActiveAccount => _active;
    internal int LiteRam => (int)RamSlider.Value;
    internal int LiteMaxRam => (int)RamSlider.Maximum;

    internal (bool Indeterminate, double Max, double Value) LiteProgress =>
        (Progress.IsIndeterminate, Progress.Maximum, Progress.Value);

    // ---------- O que a janelinha pode pedir ----------

    internal void LiteSelectAccount(Account a)
    {
        _state.ActiveKey = a.Key;
        SaveState();
        RefreshAccountsUi();
    }

    internal void LiteRemoveAccount(Account a)
    {
        _state.Accounts.RemoveAll(x => x.Key == a.Key);
        if (_state.ActiveKey == a.Key)
            _state.ActiveKey = _state.Accounts.FirstOrDefault()?.Key ?? "";

        try { System.IO.File.Delete(SkinFilePath(a)); } catch { /* não é crítico */ }

        SaveState();
        RefreshAccountsUi();
    }

    // Devolve a mensagem para mostrar; ok = true quando a conta foi criada.
    internal string LiteAddOffline(string nick, out bool ok)
    {
        ok = false;
        if (!Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
            return "Use de 3 a 16 caracteres: letras (sem acento), números ou _.";

        var account = _state.Accounts.FirstOrDefault(a => a.Key == $"{AccountType.Moonlight}:{nick}")
                      ?? new Account(AccountType.Moonlight, nick);
        AddOrReplace(account, activate: true);
        ok = true;
        return $"Conta “{nick}” criada e selecionada. É só jogar! 🌙";
    }

    internal async Task<string> LiteMicrosoftLoginAsync()
    {
        SetBusy(true);
        try
        {
            var handler = JELoginHandlerBuilder.BuildDefault();
            var session = await handler.Authenticate();

            var name = session.Username ?? "Jogador";
            var existing = _state.Accounts.FirstOrDefault(a => a.Key == $"{AccountType.Microsoft}:{name}");
            AddOrReplace(new Account(AccountType.Microsoft, name, session.UUID ?? "")
            {
                SkinSlim = existing?.SkinSlim ?? false
            }, true);
            return $"Conectado como {session.Username}. 🌙";
        }
        catch (Exception ex)
        {
            return "Não foi possível entrar: " + ex.Message + "\nLembre-se: a conta precisa ter o Minecraft Java comprado.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal void LiteSetLoader(Loader l)
    {
        foreach (var rb in LoaderPanel.Children.OfType<RadioButton>())
            if ((rb.Tag?.ToString() ?? "") == l.ToString())
                rb.IsChecked = true; // o Loader_Checked remonta a lista de versões
    }

    internal void LiteSelectVersion(string v)
    {
        if ((VersionList.SelectedItem as string) != v && _current.Contains(v))
            VersionList.SelectedItem = v;
    }

    // Repassa o clique: é o mesmo caminho do botão Jogar da janela grande (instala, verifica e abre).
    internal void LitePlay() => Play_Click(PlayButton, new RoutedEventArgs());

    internal void LiteRescan()
    {
        RefreshInstalledSet();
    }

    internal void LiteSetRam(int mb)
    {
        RamSlider.Value = Math.Clamp(mb, (int)RamSlider.Minimum, (int)RamSlider.Maximum);
        _state.Ram = (int)RamSlider.Value;
        SaveState();
    }

    internal void LiteOpenFolder(string sub) => OpenFolder(sub);

    internal void LiteOpenLog() => OpenLauncherLog_Click(this, new RoutedEventArgs());

    // Troca o modo de desempenho pelo mesmo botão da aba Ajustes (que grava, aplica e entra/sai da interface leve).
    internal void LiteSetPerfMode(string mode)
    {
        foreach (var rb in PerfPanel.Children.OfType<RadioButton>())
            if ((rb.Tag?.ToString() ?? "") == mode)
                rb.IsChecked = true;
    }
}
