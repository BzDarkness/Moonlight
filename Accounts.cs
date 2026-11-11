using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public partial class MainWindow
{
    private static readonly HttpClient Http = CreateHttp();
    private Account? _active;
    private bool _updatingUi;

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoonlightLauncher/1.0");
        return c;
    }

    // ---------- Lista de contas ----------

    private void RefreshAccountsUi()
    {
        _updatingUi = true;
        var list = _state.Accounts.ToList();
        AccountList.ItemsSource = list;
        _active = list.FirstOrDefault(a => a.Key == _state.ActiveKey);
        AccountList.SelectedItem = _active;
        _updatingUi = false;
        UpdateActiveLabel();
    }

    private void UpdateActiveLabel()
    {
        ActiveAccountText.Text = _active == null
            ? "Nenhuma conta — clique para criar"
            : $"{_active.Name}  ·  {_active.TypeLabel}";
        RefreshSkinUi();
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || AccountList.SelectedItem is not Account a) return;
        _state.ActiveKey = a.Key;
        _active = a;
        UpdateActiveLabel();
        SaveState();
    }

    private void AddOrReplace(Account account, bool activate)
    {
        _state.Accounts.RemoveAll(a => a.Key == account.Key);
        _state.Accounts.Add(account);
        if (activate) _state.ActiveKey = account.Key;
        SaveState();
        RefreshAccountsUi();
    }

    // Atualiza uma conta que já existe sem mudar a ordem da lista nem a conta selecionada.
    private void UpdateAccount(Account updated)
    {
        var i = _state.Accounts.FindIndex(a => a.Key == updated.Key);
        if (i < 0) return;
        _state.Accounts[i] = updated;
        SaveState();
        RefreshAccountsUi();
    }

    private void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not Account a) return;

        _state.Accounts.RemoveAll(x => x.Key == a.Key);
        if (_state.ActiveKey == a.Key)
            _state.ActiveKey = _state.Accounts.FirstOrDefault()?.Key ?? "";

        try { File.Delete(SkinFilePath(a)); } catch { /* não é crítico */ }

        SaveState();
        RefreshAccountsUi();
    }

    // ---------- Offline ----------

    private void AddOffline_Click(object sender, RoutedEventArgs e)
    {
        var nick = OfflineBox.Text.Trim();
        if (!Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
        {
            OfflineStatus.Text = "Use de 3 a 16 caracteres: letras (sem acento), números ou _.";
            return;
        }

        // Cria o perfil Moonlight (local, sem e-mail). Se já existe, só seleciona (mantém a skin dele).
        var account = _state.Accounts.FirstOrDefault(a => a.Key == $"{AccountType.Moonlight}:{nick}")
                      ?? new Account(AccountType.Moonlight, nick);

        AddOrReplace(account, activate: true);
        OfflineStatus.Text = $"Conta “{nick}” criada e selecionada. Você já está conectado aos amigos 🌙 — é só jogar!";
        OfflineBox.Clear();
    }

    // ---------- Microsoft ----------

    private async void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        MsStatus.Text = "Siga as instruções na janela de login da Microsoft…";
        try
        {
            var handler = JELoginHandlerBuilder.BuildDefault();
            var session = await handler.Authenticate();

            var name = session.Username ?? "Jogador";
            // Mantém a skin local e o modelo que a conta já tinha, se ela já estava na lista.
            var existing = _state.Accounts.FirstOrDefault(a => a.Key == $"{AccountType.Microsoft}:{name}");
            AddOrReplace(new Account(AccountType.Microsoft, name, session.UUID ?? "")
            {
                SkinSlim = existing?.SkinSlim ?? false
            }, true);
            MsStatus.Text = $"Conectado como {session.Username}. Você já está conectado aos amigos 🌙";
        }
        catch (Exception ex)
        {
            MsStatus.Text = "Não foi possível entrar: " + ex.Message +
                            "\nLembre-se: a conta precisa ter o Minecraft Java comprado.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void MicrosoftSignup_Click(object sender, RoutedEventArgs e) => OpenUrl("https://signup.live.com/");

    private void BuyMinecraft_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc");

    // ---------- Limpeza de restos do Ely.by ----------

    // Remove o que a versão antiga deixava no PC: o authlib-injector baixado em ProgramData.
    // Não mexe na pasta .minecraft (mundos, mods, configurações).
    private static void CleanupElyLeftovers()
    {
        try
        {
            var dir = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MoonlightLauncher");
            if (!Directory.Exists(dir)) return;

            foreach (var name in new[] { "authlib-injector.jar", "authlib-injector.jar.download" })
            {
                var f = IOPath.Combine(dir, name);
                if (File.Exists(f)) File.Delete(f);
            }

            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { /* limpeza é opcional */ }
    }

    // ---------- Sessão usada para jogar ----------

    // Garante que dá para entrar no jogo com qualquer conta selecionada. Se a conta Microsoft não
    // puder ser verificada (token vencido, sem internet), oferece jogar offline com o mesmo nome em
    // vez de travar. A instalação do jogo é a mesma para todas as contas.
    // Session = null quando o usuário cancela. Offline = true quando o jogo vai abrir sem login online.
    private async Task<(MSession? Session, bool Offline)> ResolveSessionAsync(Account acc)
    {
        try
        {
            var session = await GetSessionAsync(acc);
            return (session, acc.Type != AccountType.Microsoft);
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Falha ao verificar a conta {acc.Key}", ex);

            var r = await ShowPopupAsync("Não consegui verificar sua conta",
                $"Não foi possível entrar com a conta {acc.TypeLabel} “{acc.Name}” agora. " +
                "Você pode jogar offline com o mesmo nome (sem skin online e sem servidores originais).",
                ex.Message,
                ("Jogar offline", "offline"), ("Cancelar", "cancel"));

            return r == "offline"
                ? (MSession.CreateOfflineSession(acc.Name), true)
                : (null, false);
        }
    }

    private async Task<MSession> GetSessionAsync(Account acc)
    {
        if (acc.Type == AccountType.Microsoft)
        {
            StatusText.Text = "Verificando conta Microsoft…";
            var handler = JELoginHandlerBuilder.BuildDefault();
            return await handler.Authenticate();
        }

        return MSession.CreateOfflineSession(acc.Name);
    }
}
