using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CmlLib.Core.ProcessBuilder;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

internal static class Jx
{
    public static string S(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    public static bool B(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

    public static int I(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out var i) ? i : 0;

    public static long L(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt64(out var i) ? i : 0;
}

// ====================================================================================
//  Amigos: lista, pedidos, chat, mundos compartilhados e botão de entrar no mundo do amigo.
// ====================================================================================
public partial class MainWindow
{
    private FriendsStore _fs = new();
    private FriendsClient? _fc;
    private bool _friendsReady;
    private bool _connBlocked;       // outro launcher assumiu o código: não reconecta sozinho
    private ICollectionView? _friendView;

    private readonly ObservableCollection<FriendVm> _friendVms = new();
    private readonly ObservableCollection<ChatVm> _chatVms = new();
    private readonly ObservableCollection<RequestVm> _requestVms = new();
    private readonly ObservableCollection<FriendResultVm> _resultVms = new();
    private FriendVm? _chatWith;
    private bool _selGuard;

    private ChatToastWindow? _toast;
    private readonly Dictionary<string, GuestTunnel> _guests = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> _modWaiters = new();
    private readonly CancellationTokenSource _hostCts = new();

    private string _meName = "Jogador", _meAcc = "Offline", _myFaceB64 = "";
    private DispatcherTimer? _profileTimer, _datTimer, _typingTimer;
    private DateTime _lastTypingSent = DateTime.MinValue;
    private DateTime _connectedAt = DateTime.MinValue;
    private string _lastDatSig = "";

    private int _hostPort;           // porta do mundo que estou compartilhando (0 = nenhum)
    private object? _hostWorld;      // o que foi anunciado (reenviado se a conexão cair e voltar)
    private string _gameMc = "";     // versão do Minecraft que está aberto (ou foi aberto por último)
    private Loader _gameLoader = Loader.Vanilla;

    // [CHAT] Local game hosted on port 54321  (e as versões em português/espanhol). Mensagens de jogadores ("<nome> …") não contam.
    private static readonly Regex LanRx = new(
        @"\[CHAT\]\s*(?!<)[^<]*?(?:port|porta|puerto)\D{0,15}(\d{4,5})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string RelayUrl => FriendsConfig.Normalize(FriendsConfig.Effective);

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------- Início ----------

    private void InitFriends()
    {
        _fs = FriendsStore.Load();

        foreach (var f in _fs.Friends)
        {
            var vm = ToVm(f);
            vm.Unread = _fs.Unread.GetValueOrDefault(f.Id);
            UpdateLast(vm);
            _friendVms.Add(vm);
        }
        foreach (var r in _fs.Incoming) _requestVms.Add(ToReqVm(r));

        FriendList.ItemsSource = _friendVms;
        _friendView = CollectionViewSource.GetDefaultView(_friendVms);
        _friendView.Filter = o => o is FriendVm fv && FriendMatchesFilter(fv);
        ChatItems.ItemsSource = _chatVms;
        RequestList.ItemsSource = _requestVms;
        SearchList.ItemsSource = _resultVms;

        DndToggle.IsChecked = _fs.Dnd;
        SoundToggle.IsChecked = _fs.SoundOn;
        MeCode.Text = FriendsCrypto.Pretty(_fs.Id);
        AddPanelCode.Text = MeCode.Text;
        EmptyCode.Text = MeCode.Text;
        UpdateMyWorldUi();
        SortFriends();
        UpdateFriendsChrome();

        _profileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _profileTimer.Tick += ProfileTimer_Tick;
        _datTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _datTimer.Tick += (_, _) => { _datTimer!.Stop(); WriteServersDat(); };
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _typingTimer.Tick += (_, _) => { _typingTimer!.Stop(); TypingText.Text = ""; };

        _fc = new FriendsClient { HelloFactory = BuildHello };
        _fc.ConnectionChanged += c => Dispatcher.InvokeAsync(() => OnFriendsConnection(c));
        _fc.Received += m => Dispatcher.InvokeAsync(() => OnFriendsMessage(m));
        _fc.ConnectFailed += msg => Dispatcher.InvokeAsync(() =>
        {
            if (_connBlocked || _active == null) return;
            SetConnUi("Sem conexão — tentando de novo…", "#FFB020");
            ConnDetail.Text = "Não consegui falar com " + RelayUrl.Replace("wss://", "") + ": " + msg;
            ConnDetail.Visibility = Visibility.Visible;
        });

        Activated += (_, _) =>
        {
            if (_chatWith is { Unread: > 0 } && PageFriends.IsVisible) ClearUnread(_chatWith);
        };
        Closed += (_, _) =>
        {
            try { _hostCts.Cancel(); } catch { /* */ }
            try { _toast?.Close(); } catch { /* */ }
            try { _fc?.Dispose(); } catch { /* */ }
            foreach (var g in _guests.Values) g.Dispose();
            try { FriendsServersDat.Sync(_root, Array.Empty<(string, string)>()); } catch { /* não é crítico */ }
        };

        // Entradas [Moonlight] que sobraram de uma sessão anterior (launcher fechado com mundo de amigo na lista)
        // saem do servers.dat logo na abertura.
        _lastDatSig = "\u0001";
        _datTimer.Start();

        // A conexão com o servidor dos amigos é automática: assim que existe uma conta (offline ou Microsoft),
        // o launcher entra sozinho. O ProfileTimer_Tick (disparado por FriendsProfileChanged) cuida disso.
        _friendsReady = true;
        SetConnUi(_active == null ? "Crie uma conta para entrar" : "Conectando…", _active == null ? "#6B6F85" : "#FFB020");
        FriendsProfileChanged();
    }

    private object BuildHello() => new
    {
        t = "hello", id = _fs.Id, token = _fs.Token, name = _meName, acc = _meAcc,
        face = _myFaceB64, status = _fs.Dnd ? "dnd" : "online", v = 1
    };

    private void StartFriendsConnection()
    {
        var url = RelayUrl;
        if (url.Length == 0)
        {
            SetConnUi("Amigos indisponível nesta versão", "#6B6F85");
            return;
        }
        if (_connBlocked) return;
        if (_active == null)
        {
            SetConnUi("Crie uma conta para entrar", "#6B6F85");
            return;
        }
        SetConnUi("Conectando…", "#FFB020");
        _fc?.Start(url);
    }

    private void SetConnUi(string text, string hex)
    {
        ConnText.Text = text;
        ConnDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    // Chamado quando a conta ativa ou a skin muda (ver RefreshSkinUi).
    private void FriendsProfileChanged()
    {
        if (!_friendsReady) return;
        _meName = _active?.Name ?? "Jogador";
        _meAcc = _active?.Type == AccountType.Microsoft ? "Microsoft" : "Offline";
        MeName.Text = _meName;
        _profileTimer!.Stop();
        _profileTimer.Start();
    }

    private async void ProfileTimer_Tick(object? sender, EventArgs e)
    {
        _profileTimer!.Stop();
        try
        {
            if (_active == null)
            {
                // Sem conta: sai do servidor e espera a pessoa criar uma (offline ou Microsoft).
                _fc?.Stop();
                MeFaceBrush.ImageSource = FaceTools.FromB64(null);
                SetConnUi("Crie uma conta para entrar", "#6B6F85");
                return;
            }

            var accountKey = _active.Key;
            var face = await ComputeMyFaceAsync();
            if (_active == null || _active.Key != accountKey) return; // conta trocou enquanto a skin carregava
            _myFaceB64 = face;
            MeFaceBrush.ImageSource = FaceTools.FromB64(_myFaceB64);

            // Conta nova: conecta agora (já com nome e rosto). Conta que só mudou de skin/nome: só atualiza o perfil.
            if (_fc != null && !_fc.Running) StartFriendsConnection();
            else SendProfile();
        }
        catch (Exception ex) { LauncherLog.Write("Amigos: rosto", ex); }
    }

    private async Task<string> ComputeMyFaceAsync()
    {
        var acc = _active;
        byte[]? skin = null;
        if (acc != null)
        {
            try
            {
                var f = SkinFilePath(acc);
                if (File.Exists(f)) skin = await File.ReadAllBytesAsync(f);
            }
            catch { /* usa o rosto padrão */ }

            if (skin == null && acc.Type == AccountType.Microsoft)
            {
                try { skin = (await FetchProfileSkinAsync(acc.Uuid.Length > 0 ? acc.Uuid : acc.Name)).Bytes; }
                catch { /* sem skin própria: rosto padrão */ }
            }
        }
        return FaceTools.ToFaceB64(skin);
    }

    private void SendProfile() =>
        _fc?.Send(new { t = "profile", name = _meName, acc = _meAcc, face = _myFaceB64, status = _fs.Dnd ? "dnd" : "online" });

    private void SendFriendsList() =>
        _fc?.Send(new { t = "friends", ids = _fs.Friends.Select(f => f.Id).ToArray() });

    // ---------- Conexão ----------

    private void OnFriendsConnection(bool connected)
    {
        if (connected)
        {
            _connectedAt = DateTime.UtcNow;
            SetConnUi("Conectado", "#3DDC84");
            ConnDetail.Visibility = Visibility.Collapsed;
            SendFriendsList();
            foreach (var id in _fs.Outgoing.ToList()) _fc?.Send(new { t = "req", to = id });
            if (_hostPort > 0 && _hostWorld != null) _fc?.Send(new { t = "world", world = _hostWorld });
            return;
        }

        if (_active == null) SetConnUi("Crie uma conta para entrar", "#6B6F85");
        else if (!_connBlocked) SetConnUi("Reconectando…", "#FFB020");
        foreach (var f in _friendVms) { f.Online = false; f.Status = "offline"; f.World = null; }
        SyncWorlds();
        SortFriends();
        UpdateFriendsChrome();
        if (_chatWith != null) RefreshChatHeader();
    }

    private void OnFriendsMessage(JsonElement m)
    {
        try
        {
            switch (Jx.S(m, "t"))
            {
                case "welcome": SetConnUi("Conectado", "#3DDC84"); break;
                case "presence":
                    if (m.TryGetProperty("users", out var us) && us.ValueKind == JsonValueKind.Array)
                        foreach (var u in us.EnumerateArray()) ApplyPresence(u);
                    break;
                case "req": OnRequest(m.GetProperty("from")); break;
                case "accepted": OnAccepted(m.GetProperty("from")); break;
                case "reqcancel": RemoveIncoming(Jx.S(m, "from")); break;
                case "unfriend": RemoveFriendLocal(Jx.S(m, "from"), tellServer: false); break;
                case "msg": OnChatMessage(m); break;
                case "typing": OnTyping(Jx.S(m, "from")); break;
                case "lookup": OnLookup(m); break;
                case "search": OnSearch(m); break;
                case "reqstate":
                    SetAddStatus(Jx.S(m, "state") == "sent"
                        ? "Pedido enviado! Quando aceitar, vocês aparecem na lista."
                        : "Pedido guardado: seu amigo recebe quando abrir o launcher.");
                    break;
                case "worldmods":
                    if (_modWaiters.TryGetValue(Jx.S(m, "id"), out var w)) w.TrySetResult(m);
                    break;
                case "incoming": OnIncomingTunnel(m); break;
                case "error": OnFriendsError(m); break;
            }
        }
        catch (Exception ex) { LauncherLog.Write("Amigos: erro ao tratar " + Jx.S(m, "t"), ex); }
    }

    private void OnFriendsError(JsonElement m)
    {
        switch (Jx.S(m, "code"))
        {
            case "auth": SetConnUi("Falha ao entrar no servidor", "#FF5D6C"); break;
            case "replaced": _connBlocked = true; SetConnUi("Outro launcher entrou com seu código", "#FF5D6C"); _fc?.Stop(); break;
            case "notfriends": ShowChatNote("Essa pessoa ainda não aceitou você como amiga."); break;
        }
    }

    // ---------- Lista de amigos ----------

    private static FriendVm ToVm(FriendEntry f) => new() { Id = f.Id, Name = f.Name, Acc = f.Acc, FaceB64 = f.Face };

    private static RequestVm ToReqVm(FriendEntry r) => new()
    {
        Id = r.Id, Name = r.Name, AccLabel = r.Acc == "Microsoft" ? "MICROSOFT" : "OFFLINE", Face = FaceTools.FromB64(r.Face)
    };

    private void SortFriends()
    {
        var sorted = _friendVms
            .OrderByDescending(f => f.HasWorld)
            .ThenByDescending(f => f.LastTs)
            .ThenByDescending(f => f.Online)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selGuard = true;
        try
        {
            for (var i = 0; i < sorted.Count; i++)
            {
                var cur = _friendVms.IndexOf(sorted[i]);
                if (cur != i) _friendVms.Move(cur, i);
            }
            if (_chatWith != null && !ReferenceEquals(FriendList.SelectedItem, _chatWith))
                FriendList.SelectedItem = _chatWith;
        }
        finally { _selGuard = false; }
    }

    private void UpdateFriendsChrome()
    {
        var unread = _friendVms.Sum(f => f.Unread) + _requestVms.Count;
        FriendsBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        FriendsBadgeText.Text = unread > 99 ? "99+" : unread.ToString();

        var online = _friendVms.Count(f => f.Online);
        FriendsCountText.Text = $"CONVERSAS  ·  {online} online de {_friendVms.Count}";
        FriendsEmptyText.Visibility = _friendVms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RequestsCard.Visibility = _requestVms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPresence(JsonElement u)
    {
        var id = Jx.S(u, "id");
        var vm = _friendVms.FirstOrDefault(f => f.Id == id);
        if (vm == null) return;

        var hadWorld = vm.HasWorld;
        var online = Jx.B(u, "online");

        if (!online)
        {
            vm.Online = false; vm.Status = "offline"; vm.World = null;
        }
        else
        {
            var name = Jx.S(u, "name");
            if (name.Length > 0) vm.Name = name;
            var acc = Jx.S(u, "acc");
            if (acc.Length > 0) vm.Acc = acc;
            var face = Jx.S(u, "face");
            if (face.Length > 0) vm.FaceB64 = face;
            vm.Status = Jx.S(u, "status") == "dnd" ? "dnd" : "online";
            vm.Online = true;

            WorldInfo? w = null;
            if (u.TryGetProperty("world", out var wj) && wj.ValueKind == JsonValueKind.Object)
                w = new WorldInfo(Jx.S(wj, "name"), Jx.S(wj, "mc"), Jx.S(wj, "loader"), Jx.S(wj, "versionId"), Jx.I(wj, "modCount"));
            vm.World = w;

            var entry = _fs.Friends.FirstOrDefault(f => f.Id == id);
            if (entry != null && (entry.Name != vm.Name || entry.Acc != vm.Acc || (face.Length > 0 && entry.Face != face)))
            {
                entry.Name = vm.Name; entry.Acc = vm.Acc;
                if (face.Length > 0) entry.Face = face;
                _fs.Save();
            }
        }

        if (!hadWorld && vm.HasWorld && (DateTime.UtcNow - _connectedAt).TotalSeconds > 5)
            NotifyWorldOpened(vm);

        SyncWorlds();
        SortFriends();
        UpdateFriendsChrome();
        if (_chatWith == vm) RefreshChatHeader();
    }

    // ---------- Pedidos de amizade ----------

    private void OnRequest(JsonElement from)
    {
        var id = Jx.S(from, "id");
        if (id.Length == 0 || id == _fs.Id) return;
        var entry = new FriendEntry { Id = id, Name = Jx.S(from, "name"), Acc = Jx.S(from, "acc"), Face = Jx.S(from, "face") };

        // Já somos amigos, ou nós também tínhamos pedido: fecha a amizade sozinho.
        if (_fs.Friends.Any(f => f.Id == id) || _fs.Outgoing.Contains(id))
        {
            _fs.Outgoing.Remove(id);
            AddFriendLocal(entry);
            _fc?.Send(new { t = "accept", to = id });
            SendFriendsList();
            return;
        }

        if (_fs.Incoming.Any(r => r.Id == id)) return;
        _fs.Incoming.Add(entry);
        _fs.Save();
        _requestVms.Add(ToReqVm(entry));
        UpdateFriendsChrome();

        if (!_fs.Dnd)
        {
            if (_fs.SoundOn) FriendsSound.Play();
            ShowToastFor(id, entry.Name, FaceTools.FromB64(entry.Face), "quer ser seu amigo no Moonlight 🌙", allowReply: false);
        }
    }

    private void OnAccepted(JsonElement from)
    {
        var id = Jx.S(from, "id");
        if (id.Length == 0) return;
        _fs.Outgoing.Remove(id);
        AddFriendLocal(new FriendEntry { Id = id, Name = Jx.S(from, "name"), Acc = Jx.S(from, "acc"), Face = Jx.S(from, "face") });
        SendFriendsList();
        SetAddStatus($"✔ {Jx.S(from, "name")} aceitou! Agora vocês são amigos.");
        foreach (var r in _resultVms.Where(r => r.Id == id)) { r.Action = "✔ Amigo"; r.CanAdd = false; }

        if (!_fs.Dnd)
        {
            if (_fs.SoundOn) FriendsSound.Play();
            ShowToastFor(id, Jx.S(from, "name"), FaceTools.FromB64(Jx.S(from, "face")), "aceitou seu pedido de amizade ✔", allowReply: false);
        }
    }

    private FriendVm AddFriendLocal(FriendEntry e)
    {
        var existing = _fs.Friends.FirstOrDefault(f => f.Id == e.Id);
        if (existing == null) _fs.Friends.Add(e);
        else { existing.Name = e.Name; existing.Acc = e.Acc; if (e.Face.Length > 0) existing.Face = e.Face; }

        RemoveIncoming(e.Id, save: false);
        _fs.Save();

        var vm = _friendVms.FirstOrDefault(f => f.Id == e.Id);
        if (vm == null) { vm = ToVm(e); UpdateLast(vm); _friendVms.Add(vm); }
        else { vm.Name = e.Name; vm.Acc = e.Acc; if (e.Face.Length > 0) vm.FaceB64 = e.Face; }

        SortFriends();
        UpdateFriendsChrome();
        return vm;
    }

    private void RemoveIncoming(string id, bool save = true)
    {
        _fs.Incoming.RemoveAll(r => r.Id == id);
        foreach (var r in _requestVms.Where(r => r.Id == id).ToList()) _requestVms.Remove(r);
        if (save) _fs.Save();
        UpdateFriendsChrome();
    }

    private void RemoveFriendLocal(string id, bool tellServer)
    {
        var vm = _friendVms.FirstOrDefault(f => f.Id == id);
        _fs.Friends.RemoveAll(f => f.Id == id);
        _fs.Chats.Remove(id);
        _fs.Unread.Remove(id);
        _fs.Ports.Remove(id);
        _fs.Outgoing.Remove(id);
        _fs.Incoming.RemoveAll(r => r.Id == id);
        foreach (var waiter in _modWaiters.Where(p => p.Key == id).Select(p => p.Value).ToList())
            waiter.TrySetCanceled();
        if (vm != null) _friendVms.Remove(vm);
        if (_guests.Remove(id, out var g)) g.Dispose();

        if (_chatWith != null && _chatWith.Id == id) CloseChat();
        _fs.Save();
        if (tellServer) _fc?.Send(new { t = "unfriend", to = id });
        SendFriendsList();
        SyncWorlds();
        UpdateFriendsChrome();
    }

    private void FriendAccept_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RequestVm r) return;
        var entry = _fs.Incoming.FirstOrDefault(x => x.Id == r.Id) ?? new FriendEntry { Id = r.Id, Name = r.Name };
        AddFriendLocal(entry);
        _fc?.Send(new { t = "accept", to = r.Id });
        SendFriendsList();
    }

    private void FriendDecline_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RequestVm r) return;
        RemoveIncoming(r.Id);
        _fc?.Send(new { t = "decline", to = r.Id });
    }

    // ---------- Buscar e adicionar ----------

    private void SetAddStatus(string text)
    {
        AddStatus.Text = text;
        AddStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DoFriendSearch(); e.Handled = true; }
    }

    private void FriendSearch_Click(object sender, RoutedEventArgs e) => DoFriendSearch();

    private void AddBox_TextChanged(object sender, TextChangedEventArgs e) =>
        AddBoxHint.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void DoFriendSearch()
    {
        var q = AddBox.Text.Trim();
        if (q.Length == 0) return;

        if (_fc == null || !_fc.Connected)
        {
            SetAddStatus("Conectando ao servidor dos amigos… tente de novo em alguns segundos. " +
                         "(Na primeira vez do dia ele pode levar até 1 minuto para acordar.)");
            return;
        }

        _resultVms.Clear();
        SetAddStatus("Procurando…");
        var code = FriendsCrypto.ParseCode(q);
        if (code != null)
            _fc.Send(new { t = "lookup", id = code });
        else if (q.Length >= 2)
            _fc.Send(new { t = "search", q });
    }

    private FriendResultVm MakeResult(string id, string name, string acc, string face, bool online)
    {
        var vm = new FriendResultVm { Id = id, Name = name, Acc = acc, Online = online, Face = FaceTools.FromB64(face) };
        if (id == _fs.Id) { vm.Action = "Você"; vm.CanAdd = false; }
        else if (_fs.Friends.Any(f => f.Id == id)) { vm.Action = "✔ Amigo"; vm.CanAdd = false; }
        else if (_fs.Outgoing.Contains(id)) { vm.Action = "⏳ Enviado"; vm.CanAdd = false; }
        return vm;
    }

    private void OnLookup(JsonElement m)
    {
        var id = Jx.S(m, "id");
        if (Jx.B(m, "found") && m.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            if (_resultVms.All(r => r.Id != id))
                _resultVms.Insert(0, MakeResult(id, Jx.S(u, "name"), Jx.S(u, "acc"), Jx.S(u, "face"), Jx.B(u, "online")));
            SetAddStatus("");
        }
        else if (_resultVms.Count == 0)
        {
            // Servidor não conhece (ainda): dá para mandar o pedido mesmo assim, ele chega quando a pessoa abrir o launcher.
            _resultVms.Add(MakeResult(id, FriendsCrypto.Pretty(id), "Offline", "", false));
            SetAddStatus("Não vi essa pessoa online agora, mas você pode enviar o pedido: ele chega quando ela abrir o launcher.");
        }
    }

    private void OnSearch(JsonElement m)
    {
        var found = 0;
        if (m.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var u in arr.EnumerateArray())
            {
                var id = Jx.S(u, "id");
                if (_resultVms.Any(r => r.Id == id)) continue;
                _resultVms.Add(MakeResult(id, Jx.S(u, "name"), Jx.S(u, "acc"), Jx.S(u, "face"), Jx.B(u, "online")));
                found++;
            }

        if (found > 0) SetAddStatus("");
        else if (_resultVms.Count == 0)
            SetAddStatus("Ninguém com esse nome por aqui. Peça o código de amigo (MOON-…) para a pessoa.");
    }

    private void FriendAdd_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FriendResultVm r || !r.CanAdd) return;
        if (string.IsNullOrWhiteSpace(r.Id) || r.Id == _fs.Id || _fs.Friends.Any(f => f.Id == r.Id)) return;
        if (_fc == null || !_fc.Connected) { SetAddStatus("Sem conexão com o servidor dos amigos."); return; }

        if (!_fs.Outgoing.Contains(r.Id)) _fs.Outgoing.Add(r.Id);
        _fs.Save();
        _fc.Send(new { t = "req", to = r.Id });
        r.Action = "⏳ Enviado";
        r.CanAdd = false;
    }

    private async void FriendRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_chatWith == null) return;
        var f = _chatWith;
        var r = await ShowPopupAsync("Remover amigo",
            $"Remover {f.Name} da sua lista? A conversa também some deste PC.", null,
            ("Remover", "yes"), ("Cancelar", "no"));
        if (r == "yes") RemoveFriendLocal(f.Id, tellServer: true);
    }

    // ---------- Visual de app de mensagens: prévia, filtro, painéis ----------

    private void UpdateLast(FriendVm vm)
    {
        if (!_fs.Chats.TryGetValue(vm.Id, out var list) || list.Count == 0)
        {
            vm.LastText = ""; vm.LastTime = ""; vm.LastTs = 0;
            return;
        }
        var l = list[^1];
        var t = DateTimeOffset.FromUnixTimeMilliseconds(l.Ts).ToLocalTime();
        vm.LastTs = l.Ts;
        vm.LastText = (l.Mine ? "Você: " : "") + l.Text.Replace('\r', ' ').Replace('\n', ' ');
        vm.LastTime = t.Date == DateTime.Today ? t.ToString("HH:mm")
            : t.Date == DateTime.Today.AddDays(-1) ? "Ontem"
            : t.ToString("dd/MM/yy");
    }

    private bool FriendMatchesFilter(FriendVm f)
    {
        var q = FriendFilter?.Text?.Trim() ?? "";
        return q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void FriendFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        FriendFilterHint.Visibility = FriendFilter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _friendView?.Refresh();
    }

    private void MenuToggle_Click(object sender, RoutedEventArgs e) =>
        MenuPanel.Visibility = MenuPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void NewFriend_Click(object sender, RoutedEventArgs e)
    {
        ChatListView.Visibility = Visibility.Collapsed;
        AddPanel.Visibility = Visibility.Visible;
        SetAddStatus("");
        AddBox.Focus();
    }

    private void AddBack_Click(object sender, RoutedEventArgs e)
    {
        AddPanel.Visibility = Visibility.Collapsed;
        ChatListView.Visibility = Visibility.Visible;
        _resultVms.Clear();
        AddBox.Clear();
        SetAddStatus("");
    }

    // ---------- Chat ----------

    private void FriendList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selGuard) return;
        if (FriendList.SelectedItem is FriendVm vm) OpenChat(vm);
    }

    private void OpenChat(FriendVm vm)
    {
        _chatWith = vm;
        ChatEmpty.Visibility = Visibility.Collapsed;
        ChatPane.Visibility = Visibility.Visible;
        TypingText.Text = "";

        _chatVms.Clear();
        foreach (var l in _fs.ChatWith(vm.Id)) _chatVms.Add(ToChatVm(l));
        RefreshChatHeader();
        ClearUnread(vm);
        ScrollChatEnd();
        ChatInput.Focus();
    }

    private void CloseChat()
    {
        _chatWith = null;
        ChatPane.Visibility = Visibility.Collapsed;
        ChatEmpty.Visibility = Visibility.Visible;
        _chatVms.Clear();
        _selGuard = true;
        FriendList.SelectedItem = null;
        _selGuard = false;
    }

    private void ClearUnread(FriendVm vm)
    {
        if (vm.Unread == 0 && !_fs.Unread.ContainsKey(vm.Id)) return;
        vm.Unread = 0;
        _fs.Unread.Remove(vm.Id);
        _fs.Save();
        UpdateFriendsChrome();
    }

    private void RefreshChatHeader()
    {
        var vm = _chatWith;
        if (vm == null) return;
        ChatFaceBrush.ImageSource = vm.Face;
        ChatName.Text = vm.Name;
        ChatAcc.Text = vm.AccLabel;
        ChatStatus.Text = vm.SubText;

        var w = vm.World;
        WorldPanel.Visibility = vm.HasWorld && w != null ? Visibility.Visible : Visibility.Collapsed;
        if (w != null)
        {
            WorldTitle.Text = "🌍  " + w.Name;
            WorldSub.Text = vm.WorldSub;
            WorldHint.Text = w.ModCount > 0
                ? "Esse mundo usa mods. Se faltar algum, o launcher pergunta se você quer baixar todos antes de abrir o jogo."
                : "O launcher abre o Minecraft na mesma versão e conecta direto no mundo dele.";
            WorldJoinBtn.IsEnabled = !_busy;
        }
    }

    private static ChatVm ToChatVm(ChatLine l)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(l.Ts).ToLocalTime();
        var time = t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd/MM HH:mm");
        return new ChatVm { Mine = l.Mine, Text = l.Text, Time = time };
    }

    private void ScrollChatEnd() =>
        Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Background);

    private void ShowChatNote(string text)
    {
        TypingText.Text = text;
        _typingTimer?.Stop();
        _typingTimer?.Start();
    }

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ChatSend_Click(sender, e);
    }

    private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ChatInputHint.Visibility = ChatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_chatWith == null || ChatInput.Text.Length == 0) return;
        if ((DateTime.UtcNow - _lastTypingSent).TotalSeconds < 3) return;
        _lastTypingSent = DateTime.UtcNow;
        _fc?.Send(new { t = "typing", to = _chatWith.Id });
    }

    private async void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        if (_chatWith == null) return;
        var text = ChatInput.Text;
        var friendId = _chatWith.Id;
        if (await SendChatAsync(friendId, text) && _chatWith?.Id == friendId) ChatInput.Clear();
    }

    private async Task<bool> SendChatAsync(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || _friendVms.All(f => f.Id != id)) return false;
        if (text.Length > 400)
        {
            ShowChatNote("A mensagem pode ter no máximo 400 caracteres.");
            return false;
        }
        if (_fc == null || !await _fc.SendMessageAsync(new
            { t = "msg", to = id, text, mid = Guid.NewGuid().ToString("N")[..10] }))
        {
            ShowChatNote("Mensagem não enviada. Verifique a conexão e tente novamente.");
            return false;
        }

        // Só grava e mostra como enviada depois que o WebSocket confirmou a escrita.
        var line = new ChatLine { Mine = true, Text = text, Ts = NowMs() };
        _fs.AddLine(id, line);
        _fs.Save();
        var friend = _friendVms.FirstOrDefault(f => f.Id == id);
        if (friend != null) { UpdateLast(friend); SortFriends(); }
        if (_chatWith?.Id == id) { _chatVms.Add(ToChatVm(line)); ScrollChatEnd(); }
        return true;
    }

    private void OnChatMessage(JsonElement m)
    {
        var from = Jx.S(m, "from");
        var text = Jx.S(m, "text");
        var vm = _friendVms.FirstOrDefault(f => f.Id == from);
        if (vm == null || text.Length == 0) return;

        var ts = Jx.L(m, "ts");
        var line = new ChatLine { Mine = false, Text = text, Ts = ts > 0 ? ts : NowMs() };
        _fs.AddLine(from, line);
        UpdateLast(vm);
        SortFriends();

        var viewing = _chatWith == vm && PageFriends.IsVisible && IsActive;
        if (_chatWith == vm) { _chatVms.Add(ToChatVm(line)); ScrollChatEnd(); TypingText.Text = ""; }

        if (!viewing)
        {
            vm.Unread++;
            _fs.Unread[from] = vm.Unread;
            SortFriends();
            if (!_fs.Dnd)
            {
                if (_fs.SoundOn) FriendsSound.Play();
                ShowToastFor(vm.Id, vm.Name, vm.Face, text, allowReply: true);
            }
        }

        _fs.Save();
        UpdateFriendsChrome();
    }

    private void OnTyping(string from)
    {
        if (_chatWith == null || _chatWith.Id != from) return;
        TypingText.Text = $"{_chatWith.Name} está digitando…";
        _typingTimer?.Stop();
        _typingTimer?.Start();
    }

    // ---------- Janelinha de mensagem + Não perturbe ----------

    private void ShowToastFor(string id, string name, System.Windows.Media.Imaging.BitmapSource face, string text, bool allowReply)
    {
        if (_fs.Dnd) return;
        if (_toast == null)
        {
            _toast = new ChatToastWindow();
            _toast.ReplySent += (fid, txt) => _ = SendChatAsync(fid, txt);
            _toast.OpenRequested += OpenFriendChat;
        }
        _toast.ShowMessage(id, name, face, text, allowReply);
    }

    private void NotifyWorldOpened(FriendVm vm)
    {
        if (_fs.Dnd || vm.World == null) return;
        if (_fs.SoundOn) FriendsSound.Play();
        ShowToastFor(vm.Id, vm.Name, vm.Face,
            $"abriu um mundo ({vm.World.Mc} · {vm.World.Loader}). Clique para entrar! 🌍", allowReply: false);
    }

    /// <summary>Traz o launcher para a frente e abre a conversa com este amigo (usado ao clicar na janelinha).</summary>
    public void OpenFriendChat(string id)
    {
        if (!IsVisible) { Show(); RestoreAfterGame(); }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        GoTo("Play");
        ShowFriendsPage();
        if (AddPanel.Visibility == Visibility.Visible) AddBack_Click(this, new RoutedEventArgs());
        var vm = _friendVms.FirstOrDefault(f => f.Id == id);
        if (vm != null) { FriendList.SelectedItem = vm; OpenChat(vm); }
    }

    private void DndToggle_Click(object sender, RoutedEventArgs e)
    {
        _fs.Dnd = DndToggle.IsChecked == true;
        _fs.Save();
        SendProfile();
        if (_fs.Dnd) _toast?.Dismiss();
        StatusText.Text = _fs.Dnd
            ? "🌙 Não perturbe ligado: sem janelinha e sem som de mensagens."
            : "Notificações de amigos ligadas.";
    }

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        _fs.SoundOn = SoundToggle.IsChecked == true;
        _fs.Save();
        if (_fs.SoundOn && !_fs.Dnd) FriendsSound.Play(); // prévia do som
    }

    // ---------- Navegação ----------

    private void FriendsOpen_Click(object sender, RoutedEventArgs e) => ShowFriendsPage();

    private void ShowFriendsPage()
    {
        PagePlay.Visibility = Visibility.Collapsed;
        PageFriends.Visibility = Visibility.Visible;
        if (!Perf.Light) FadeIn(PageFriends);
        if (_chatWith != null) ClearUnread(_chatWith);
    }

    private void FriendsBack_Click(object sender, RoutedEventArgs e)
    {
        PageFriends.Visibility = Visibility.Collapsed;
        PagePlay.Visibility = Visibility.Visible;
        if (!Perf.Light) FadeIn(PagePlay);
    }

    private void NavPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PageFriends != null && PageFriends.Visibility == Visibility.Visible) FriendsBack_Click(sender, e);
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(FriendsCrypto.Pretty(_fs.Id));
            StatusText.Text = "Código de amigo copiado! Mande para quem você quer adicionar. 🌙";
        }
        catch { StatusText.Text = "Não consegui copiar. Seu código: " + FriendsCrypto.Pretty(_fs.Id); }
    }

    // ---------- Meu mundo (anfitrião) ----------

    // Chamado a cada linha que o Minecraft escreve no console (ver AppendLog).
    private void FriendsOnGameLog(string line)
    {
        if (!_friendsReady || !line.Contains("[CHAT]", StringComparison.Ordinal)) return;
        var m = LanRx.Match(line);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var port) || port < 1024 || port > 65535) return;
        Dispatcher.InvokeAsync(() => _ = StartSharingWorldAsync(port));
    }

    private void FriendsOnGameExited()
    {
        if (!_friendsReady) return;
        StopSharingWorld();
    }

    private async Task StartSharingWorldAsync(int port)
    {
        if (_fc == null) return;
        _hostPort = port;

        var mc = _gameMc.Length > 0 ? _gameMc : VersionList.SelectedItem as string ?? "";
        var loader = _gameMc.Length > 0 ? _gameLoader : _loader;
        var mods = await FriendMods.HashFolderAsync(IOPath.Combine(_root, "mods"));
        if (_hostPort != port) return; // mudou enquanto lia os mods

        _hostWorld = new
        {
            name = $"Mundo de {_meName}", mc, loader = loader.ToString(), versionId = mc,
            mods = mods.Select(x => new { n = x.Name, h = x.Sha1, s = x.Size }).ToArray()
        };
        _fc.Send(new { t = "world", world = _hostWorld });
        UpdateMyWorldUi();
        StatusText.Text = "🌍 Seu mundo está visível para os seus amigos.";
    }

    private void StopSharingWorld()
    {
        if (_hostPort == 0 && _hostWorld == null) return;
        _hostPort = 0;
        _hostWorld = null;
        _fc?.Send(new { t = "world", world = (object?)null });
        UpdateMyWorldUi();
    }

    private void UpdateMyWorldUi()
    {
        var sharing = _hostPort > 0;
        MyWorldText.Text = sharing
            ? $"✔ Compartilhando seu mundo (porta {_hostPort}). Seus amigos já veem e podem entrar."
            : "Abra um mundo no Minecraft e use “Abrir para LAN”. Ele aparece sozinho para seus amigos.";
        MyWorldBtn.Content = sharing ? "Parar" : "Compartilhar";
        MyWorldPort.IsEnabled = !sharing;
        if (sharing) MyWorldPort.Text = _hostPort.ToString();
    }

    private async void MyWorldShare_Click(object sender, RoutedEventArgs e)
    {
        if (_hostPort > 0) { StopSharingWorld(); return; }
        if (!int.TryParse(MyWorldPort.Text.Trim(), out var port) || port < 1024 || port > 65535)
        {
            StatusText.Text = "Digite a porta do seu mundo (4 ou 5 números). Ela aparece no chat do jogo ao abrir para LAN.";
            return;
        }
        await StartSharingWorldAsync(port);
    }

    private void OnIncomingTunnel(JsonElement m)
    {
        if (_hostPort == 0 || RelayUrl.Length == 0) return;
        var tid = Jx.S(m, "tid");
        if (tid.Length == 0) return;

        // Só abre o seu mundo para quem está na sua lista de amigos (não confia só no servidor).
        var fromId = m.TryGetProperty("from", out var from) ? Jx.S(from, "id") : "";
        var guest = _friendVms.FirstOrDefault(f => f.Id == fromId);
        if (guest == null)
        {
            LauncherLog.Write("Túnel recusado: " + (fromId.Length == 0 ? "sem remetente" : fromId + " não é seu amigo"));
            return;
        }

        _ = HostTunnel.AcceptAsync(RelayUrl, _fs.Id, _fs.Token, tid, _hostPort, _hostCts.Token);
        if (!_fs.Dnd) ShowToastFor(guest.Id, guest.Name, guest.Face, "está entrando no seu mundo 🌍", allowReply: false);
    }

    // ---------- Mundos dos amigos (convidado): portas locais + lista do Multijogador ----------

    private void SyncWorlds()
    {
        var url = RelayUrl;
        var want = _friendVms.Where(f => f.HasWorld).Select(f => f.Id).ToHashSet();

        foreach (var id in _guests.Keys.Where(id => !want.Contains(id)).ToList())
        {
            _guests[id].Dispose();
            _guests.Remove(id);
        }

        if (url.Length > 0)
        {
            foreach (var id in want)
            {
                if (_guests.ContainsKey(id)) continue;
                var used = _guests.Values.Select(g => g.Port).ToHashSet();
                _fs.Ports.TryGetValue(id, out var preferred);
                var g = GuestTunnel.Start(url, _fs.Id, _fs.Token, id, preferred, used);
                if (g == null) continue;
                _guests[id] = g;
                if (preferred != g.Port) { _fs.Ports[id] = g.Port; _fs.Save(); }
            }
        }

        _datTimer?.Stop();
        _datTimer?.Start();
    }

    private void WriteServersDat()
    {
        var worlds = _friendVms
            .Where(f => f.HasWorld && f.World != null && _guests.ContainsKey(f.Id))
            .Select(f => ($"§f{f.Name}§7 · {f.World!.Mc}", $"127.0.0.1:{_guests[f.Id].Port}"))
            .ToList();

        var sig = string.Join("|", worlds.Select(w => w.Item1 + w.Item2));
        if (sig == _lastDatSig) return;
        _lastDatSig = sig;

        try { FriendsServersDat.Sync(_root, worlds); }
        catch (Exception ex) { LauncherLog.Write("Não consegui atualizar o servers.dat", ex); }
    }

    // ---------- Entrar no mundo de um amigo ----------

    private async void FriendJoin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is FriendVm vm) await JoinFriendWorldAsync(vm);
    }

    private async void WorldJoin_Click(object sender, RoutedEventArgs e)
    {
        if (_chatWith != null) await JoinFriendWorldAsync(_chatWith);
    }

    private async Task<JsonElement?> RequestWorldAsync(string id)
    {
        if (_fc == null || !_fc.Connected) return null;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _modWaiters[id] = tcs;
        _fc.Send(new { t = "worldmods", id });

        var done = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        _modWaiters.Remove(id);
        if (done != tcs.Task) return null;

        var el = tcs.Task.Result;
        return Jx.B(el, "gone") ? null : el;
    }

    private async Task JoinFriendWorldAsync(FriendVm f)
    {
        if (_busy) { StatusText.Text = "Espere o que está em andamento terminar e tente de novo."; return; }
        if (!f.HasWorld) return;

        if (_active == null)
        {
            await ShowPopupAsync("Escolha uma conta",
                "Para entrar no mundo de um amigo você precisa de uma conta. Crie uma conta Moonlight (só o nome) ou entre com a Microsoft.",
                null, ("Ir para Contas", "ok"));
            GoTo("Accounts");
            return;
        }

        SyncWorlds();
        if (!_guests.TryGetValue(f.Id, out var tunnel))
        {
            await ShowPopupAsync("Não consegui abrir a ponte",
                "Não deu para abrir a porta local que liga você ao mundo do seu amigo. Feche outros programas que usem as portas 25600–25800 e tente de novo.",
                null, ("Fechar", "close"));
            return;
        }

        SetBusy(true);
        var prevLoader = _loader;
        try
        {
            StatusText.Text = $"Conferindo o mundo de {f.Name}…";
            var info = await RequestWorldAsync(f.Id);
            if (info == null)
            {
                StatusText.Text = "O mundo não está mais aberto.";
                await ShowPopupAsync("Mundo fechado",
                    $"O mundo de {f.Name} não está mais aberto (ou o servidor demorou para responder). Tente de novo em instantes.",
                    null, ("Fechar", "close"));
                return;
            }

            var w = info.Value;
            var mc = Jx.S(w, "mc");
            if (!Enum.TryParse<Loader>(Jx.S(w, "loader"), out var loader)) loader = Loader.Vanilla;
            if (mc.Length == 0) { StatusText.Text = "Mundo sem versão informada."; return; }

            var hostMods = new List<ModFile>();
            if (w.TryGetProperty("mods", out var marr) && marr.ValueKind == JsonValueKind.Array)
                foreach (var x in marr.EnumerateArray())
                    hostMods.Add(new ModFile(Jx.S(x, "n"), Jx.S(x, "h"), Jx.L(x, "s")));

            // ----- mods: avisa e oferece baixar tudo -----
            if (loader is not (Loader.Vanilla or Loader.OptiFine) && hostMods.Count > 0)
            {
                var modsDir = IOPath.Combine(_root, "mods");
                var mine = await FriendMods.HashFolderAsync(modsDir);
                var missing = FriendMods.Missing(hostMods, mine);

                if (missing.Count > 0)
                {
                    var list = string.Join("\n", missing.Take(40).Select(x => "• " + x.Name)) +
                               (missing.Count > 40 ? $"\n… e mais {missing.Count - 40}" : "");
                    var r = await ShowPopupAsync($"🧩 {f.Name} está usando mods",
                        $"Para entrar no mundo de {f.Name} você precisa de {missing.Count} mod(s) que ainda não tem. " +
                        "Quer baixar todos agora? Depois disso o Minecraft abre e conecta sozinho.",
                        list,
                        ("⬇  Instalar todos e jogar", "install"), ("Jogar sem eles", "skip"), ("Cancelar", "cancel"));

                    if (r is "cancel" or "close") { StatusText.Text = "Cancelado."; return; }

                    if (r == "install")
                    {
                        StatusText.Text = "Procurando os mods do seu amigo…";
                        Progress.IsIndeterminate = true;
                        var (found, notFound) = await FriendMods.ResolveAsync(missing);

                        await FriendMods.DownloadAsync(found, modsDir, (i, n, name) => Dispatcher.Invoke(() =>
                        {
                            Progress.IsIndeterminate = false;
                            Progress.Maximum = Math.Max(n, 1);
                            Progress.Value = i;
                            StatusText.Text = name.Length > 0 ? $"Baixando mod {i + 1}/{n}: {name}" : "Mods instalados ✔";
                        }));

                        if (notFound.Count > 0)
                        {
                            var r2 = await ShowPopupAsync("Alguns mods não deu para baixar",
                                "Esses mods não estão no Modrinth (ou são de outra fonte). Peça o arquivo para o seu amigo e coloque na pasta mods. " +
                                "Se continuar sem eles, o jogo pode não conseguir entrar no mundo.",
                                string.Join("\n", notFound.Select(x => "• " + x.Name)),
                                ("Continuar mesmo assim", "go"), ("Cancelar", "cancel"));
                            if (r2 != "go") { StatusText.Text = "Cancelado."; return; }
                        }
                    }
                }
            }

            // ----- abre o Minecraft na versão do mundo e conecta -----
            _loader = loader;
            if (loader == Loader.ForgeOptiFine) await EnsureOptiFineModAsync(mc);

            var installed = InstalledVersionConverter.Installed.Contains(mc);
            StatusText.Text = installed || loader is Loader.Vanilla or Loader.OptiFine
                ? "Preparando o jogo…"
                : $"Instalando {Info[loader].Title} para {mc} (pode demorar na primeira vez)…";

            var versionName = await PrepareVersionAsync(mc);
            var (session, offline) = await ResolveSessionAsync(_active);
            if (session == null) { StatusText.Text = "Cancelado."; return; }

            var opts = new MLaunchOption { Session = session, MaximumRamMb = (int)RamSlider.Value };

            try { SyncSkinPack(_active, mc, offline); }
            catch (Exception ex) { LauncherLog.Write("Não consegui preparar a skin offline", ex); }

            StatusText.Text = "Verificando os arquivos do jogo…";
            var process = await _launcher.InstallAndBuildProcessAsync(versionName, opts);

            AddAutoConnectArgs(process, mc, tunnel.Port);
            _gameMc = mc;
            _gameLoader = loader;
            _lastDatSig = "";
            WriteServersDat();

            StatusText.Text = $"Entrando no mundo de {f.Name}…";
            WatchProcess(process, DateTime.Now);
            RefreshVersionMarks();
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Falha ao entrar no mundo de {f.Name}", ex);
            StatusText.Text = "Erro: " + ex.Message;
            var friendly = ex is HttpRequestException
                ? "Não consegui baixar algum arquivo. Verifique sua internet e tente de novo."
                : ex.Message;
            await ShowPopupAsync("Não foi possível entrar no mundo", friendly,
                $"{ex.GetType().Name}: {ex.Message}", ("Fechar", "close"));
        }
        finally
        {
            _loader = prevLoader;
            SetBusy(false);
            if (_chatWith != null) RefreshChatHeader();
        }
    }

    // Faz o Minecraft conectar sozinho: --quickPlayMultiplayer (1.20+) ou --server/--port (versões antigas).
    private static void AddAutoConnectArgs(Process p, string mc, int port)
    {
        var quick = !Version.TryParse(mc, out var v) || v >= new Version(1, 20);
        var list = p.StartInfo.ArgumentList;

        if (list.Count > 0)
        {
            if (quick) { list.Add("--quickPlayMultiplayer"); list.Add($"127.0.0.1:{port}"); }
            else { list.Add("--server"); list.Add("127.0.0.1"); list.Add("--port"); list.Add(port.ToString()); }
        }
        else
        {
            p.StartInfo.Arguments += quick
                ? $" --quickPlayMultiplayer 127.0.0.1:{port}"
                : $" --server 127.0.0.1 --port {port}";
        }
    }
}
