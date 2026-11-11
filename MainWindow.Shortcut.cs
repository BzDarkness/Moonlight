using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MoonlightLauncher;

// Atalho na área de trabalho: criado na primeira abertura e mantido com o ícone certo.
public partial class MainWindow
{
    private bool _installShortcutDone;

    private void InitShortcutSettings()
    {
        ShortcutToggle.IsChecked = _state.DesktopShortcut;
        ShortcutInfo.Text = DesktopShortcut.Exists
            ? "✔ O atalho está na sua área de trabalho."
            : _state.DesktopShortcut ? "Ainda não há atalho na área de trabalho." : "Atalho desligado.";
    }

    // Chamado na abertura e sempre que o ícone do aplicativo muda. Roda depois que a tela já abriu (não atrasa a abertura).
    private void QueueShortcutSync()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            // Logo depois de instalar na pasta escondida, o atalho é (re)criado mesmo que tenha sido apagado antes.
            var force = SelfInstall.JustInstalled && !_installShortcutDone;
            if (force) _installShortcutDone = true;
            SyncDesktopShortcut(force);
        }));
    }

    private bool SyncDesktopShortcut(bool force)
    {
        try
        {
            if (!_state.DesktopShortcut) return false;

            var exe = DesktopShortcut.StableExePath();
            if (exe == null)
            {
                LauncherLog.Write("Atalho: o launcher está rodando de uma pasta temporária; atalho não criado.");
                return false;
            }

            // Mesmo ícone do aplicativo que a janela usa (só com o Golden Moon ativo).
            var key = GoldenActive ? (_state.AppIcon ?? "") : "";
            var rel = IconFileOf(key);
            if (rel.Length == 0) key = "";

            var sig = exe + "|" + key;
            var exists = DesktopShortcut.Exists;

            if (!force)
            {
                // Já criamos antes e a pessoa apagou: respeita (só volta se ela pedir em Ajustes).
                if (!exists && _state.ShortcutCreated) return false;
                // Está tudo igual ao que já gravamos: não mexe no arquivo.
                if (exists && _state.ShortcutSig == sig) return true;
            }

            DesktopShortcut.Write(exe, key, rel);
            _state.ShortcutCreated = true;
            _state.ShortcutSig = sig;
            SaveState();
            SelfInstall.CleanDesktopMess(); // repetidos e restos de atualização
            return true;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui criar o atalho na área de trabalho", ex);
            return false;
        }
    }

    private void ShortcutToggle_Click(object sender, RoutedEventArgs e)
    {
        _state.DesktopShortcut = ShortcutToggle.IsChecked == true;

        if (_state.DesktopShortcut)
        {
            ShortcutInfo.Text = SyncDesktopShortcut(force: true)
                ? "✔ Atalho criado na sua área de trabalho."
                : "Não consegui criar o atalho (veja o launcher.log).";
        }
        else
        {
            DesktopShortcut.Delete();
            _state.ShortcutCreated = false;
            _state.ShortcutSig = "";
            ShortcutInfo.Text = "Atalho desligado e removido da área de trabalho.";
        }
        SaveState();
    }

    private void ShortcutNow_Click(object sender, RoutedEventArgs e)
    {
        _state.DesktopShortcut = true;
        ShortcutToggle.IsChecked = true;
        ShortcutInfo.Text = SyncDesktopShortcut(force: true)
            ? "✔ Atalho criado na sua área de trabalho."
            : "Não consegui criar o atalho (veja o launcher.log).";
        SaveState();
    }
}
