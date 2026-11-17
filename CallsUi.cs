using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// ====================================================================================
//  Chamadas na aba de amigos: ligar, atender, recusar, desligar, tela do amigo.
//  (O motor — áudio, tela e rede — está em Calls.cs; o desenho dos painéis em CallWindows.cs.)
// ====================================================================================
public partial class MainWindow
{
    private CallSession? _call;
    private CallPhase _callPhase = CallPhase.None;
    private FriendVm? _callPeer;
    private string _callId = "";
    private DispatcherTimer? _callTimer, _ringTimeout;
    private CallRingWindow? _ringWin;
    private CallMiniWindow? _miniWin;
    private ShareFullscreenWindow? _fullWin;
    private BitmapSource? _latestFrame, _callMeFace;
    private int _frameQueued;
    private bool _hasFrame;

    // O "toque" viaja pelo chat como uma mensagem especial que o launcher esconde:  [[moon-call|ring|ID]]
    private const string CallTag = "[[moon-call|";

    private static string CallMsg(string op, string id) => $"{CallTag}{op}|{id}]]";

    private static bool TryParseCallMsg(string text, out string op, out string id)
    {
        op = ""; id = "";
        if (!text.StartsWith(CallTag, StringComparison.Ordinal) || !text.EndsWith("]]", StringComparison.Ordinal)) return false;
        var parts = text[CallTag.Length..^2].Split('|');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
        op = parts[0]; id = parts[1];
        return true;
    }

    /// <summary>Endereço do canal de chamadas: o mesmo servidor dos amigos (/call), ou o de %AppData%\MoonlightLauncher\callrelay.txt se existir.</summary>
    private static string CallRelayUrl
    {
        get
        {
            try
            {
                var f = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "callrelay.txt");
                if (File.Exists(f))
                {
                    var t = FriendsConfig.Normalize(File.ReadAllText(f));
                    if (t.Length > 0) return t;
                }
            }
            catch { /* usa o padrão */ }
            return RelayUrl; // as chamadas vão pelo mesmo servidor dos amigos (/call)
        }
    }

    // ---------- Início ----------

    private void InitCalls()
    {
        CallStageCtl.MuteClicked += () =>
        {
            if (_call == null) return;
            if (_call.Deafened) _call.SetDeafened(false);
            else _call.SetMuted(!_call.Muted);
            RefreshCallUi();
        };
        CallStageCtl.DeafClicked += () => { if (_call != null) { _call.SetDeafened(!_call.Deafened); RefreshCallUi(); } };
        CallStageCtl.ShareClicked += () =>
        {
            if (_call == null) return;
            if (_call.Sharing) _call.StopShare();
            else BeginShareFlow();
            RefreshCallUi();
        };
        CallStageCtl.NoiseSuppressionChanged += on =>
        {
            _state.CallNoiseSuppression = on;
            SaveState();
            _call?.SetNoiseSuppression(on);
        };
        CallStageCtl.WatchClicked += () => { if (_call != null) { _call.SetWatching(!_call.Watching); RefreshCallUi(); } };
        CallStageCtl.VoiceVolumeChanged += v => _call?.SetVoiceVolume(v);
        CallStageCtl.StreamVolumeChanged += v => _call?.SetStreamVolume(v);
        CallStageCtl.HangupClicked += HangUp;
        CallStageCtl.AcceptClicked += AcceptIncoming;
        CallStageCtl.DeclineClicked += DeclineIncoming;
        CallStageCtl.FullscreenClicked += OpenShareFullscreen;

        _callTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _callTimer.Tick += (_, _) => RefreshCallUi();
        _ringTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(42) };
        _ringTimeout.Tick += RingTimeout_Tick;

        Closed += (_, _) =>
        {
            try { _call?.Dispose(); } catch { /* */ }
            try { CallSounds.StopRing(); } catch { /* */ }
            try { _ringWin?.Close(); } catch { /* */ }
            try { _miniWin?.Close(); } catch { /* */ }
            try { _fullWin?.Close(); } catch { /* */ }
        };
    }

    // ---------- Ligar ----------

    private async void CallStart_Click(object sender, RoutedEventArgs e)
    {
        if (_chatWith != null) await StartCallAsync(_chatWith);
    }

    private async Task StartCallAsync(FriendVm f)
    {
        if (_callPhase != CallPhase.None) { ShowChatNote("Você já está em uma chamada. Desligue antes de ligar para outra pessoa."); return; }
        if (_active == null)
        {
            await ShowPopupAsync("Escolha uma conta", "Para ligar para um amigo você precisa de uma conta (Moonlight ou Microsoft).", null, ("Ir para Contas", "ok"));
            GoTo("Accounts");
            return;
        }
        if (_fc == null || !_fc.Connected) { ShowChatNote("Sem conexão com o servidor dos amigos. Tente de novo em alguns segundos."); return; }
        if (!f.Online) { ShowChatNote($"{f.Name} está offline agora. Ligue quando a pessoa estiver online."); return; }
        if (CallRelayUrl.Length == 0) { ShowChatNote("Chamadas indisponíveis nesta versão."); return; }

        var id = Guid.NewGuid().ToString("N")[..12];
        if (!await _fc.SendMessageAsync(new { t = "msg", to = f.Id, text = CallMsg("ring", id), mid = Guid.NewGuid().ToString("N")[..10] }))
        {
            ShowChatNote("Não consegui chamar. Verifique a conexão e tente de novo.");
            return;
        }
        if (_callPhase != CallPhase.None) return; // algo mudou enquanto enviava

        _callId = id;
        _callPeer = f;
        _callPhase = CallPhase.Outgoing;
        _call = MakeCallSession(f, id);
        _call.Connect(); // já vai abrindo a conexão para a chamada começar rápido quando atenderem
        AddCallLine(f.Id, "📞 Você iniciou uma chamada");
        if (_fs.SoundOn) CallSounds.StartRing(false);
        _ringTimeout!.Stop();
        _ringTimeout.Start();
        BeginCallUi();
    }

    private CallSession MakeCallSession(FriendVm f, string id)
    {
        var s = new CallSession(CallRelayUrl, _fs.Id, _fs.Token, f.Id, id) { NoiseSuppression = _state.CallNoiseSuppression };
        s.Changed += () => Dispatcher.InvokeAsync(RefreshCallUi);
        s.Ended += reason => Dispatcher.InvokeAsync(() => OnCallEnded(s, reason));
        s.Frame += OnCallFrame;
        return s;
    }

    // ---------- Sinais (chegam pelo chat) ----------

    private void SendCallSignal(string peerId, string op, string id) =>
        _fc?.Send(new { t = "msg", to = peerId, text = CallMsg(op, id), mid = Guid.NewGuid().ToString("N")[..10] });

    private bool IsCurrentCall(FriendVm vm, string id) => _callPeer?.Id == vm.Id && _callId == id;

    private void OnCallSignal(FriendVm vm, string op, string id, long ts)
    {
        switch (op)
        {
            case "ring":
            {
                // Pedido guardado enquanto você estava offline (chega logo depois de conectar): vira "chamada perdida".
                var stale = ts > 0 && NowMs() - ts > 45000 && (DateTime.UtcNow - _connectedAt).TotalSeconds < 8;
                if (stale) { MissedCall(vm, notify: true); return; }

                if (_callPhase != CallPhase.None || _active == null || _fs.Dnd)
                {
                    if (IsCurrentCall(vm, id)) return; // toque repetido da chamada que já está tocando
                    SendCallSignal(vm.Id, "busy", id);
                    MissedCall(vm, notify: !_fs.Dnd);
                    return;
                }

                _callId = id;
                _callPeer = vm;
                _callPhase = CallPhase.Incoming;
                if (_fs.SoundOn) CallSounds.StartRing(true);
                ShowRingWindow(vm);
                _ringTimeout!.Stop();
                _ringTimeout.Start();
                BeginCallUi();
                break;
            }
            case "accept":
                if (_callPhase == CallPhase.Outgoing && IsCurrentCall(vm, id)) ActivateCall();
                break;
            case "decline":
                if (_callPhase == CallPhase.Outgoing && IsCurrentCall(vm, id))
                    EndCallLocal($"📵 {vm.Name} recusou a chamada", sendBye: false, sound: true);
                break;
            case "busy":
                if (_callPhase == CallPhase.Outgoing && IsCurrentCall(vm, id))
                    EndCallLocal($"📵 {vm.Name} não pode atender agora", sendBye: false, sound: true);
                break;
            case "cancel":
                if (_callPhase == CallPhase.Incoming && IsCurrentCall(vm, id))
                {
                    EndCallLocal(null, sendBye: false, sound: false);
                    MissedCall(vm, notify: true);
                }
                break;
            case "end":
                if (_callPhase == CallPhase.Active && IsCurrentCall(vm, id))
                    EndCallLocal("📞 Chamada encerrada · " + DurationText(), sendBye: false, sound: true);
                break;
        }
    }

    private void ShowRingWindow(FriendVm vm)
    {
        if (_ringWin == null)
        {
            _ringWin = new CallRingWindow();
            _ringWin.Accepted += AcceptIncoming;
            _ringWin.Declined += DeclineIncoming;
            _ringWin.OpenRequested += () => { if (_callPeer != null) OpenFriendChat(_callPeer.Id); };
        }
        _ringWin.ShowCall(vm.Name, vm.Face);
    }

    private void MissedCall(FriendVm vm, bool notify)
    {
        AddCallLine(vm.Id, "📵 Chamada perdida");
        var viewing = _chatWith == vm && PageFriends.IsVisible && IsActive;
        if (viewing) return;
        vm.Unread++;
        _fs.Unread[vm.Id] = vm.Unread;
        _fs.Save();
        UpdateFriendsChrome();
        if (notify && !_fs.Dnd)
        {
            if (_fs.SoundOn) FriendsSound.Play();
            ShowToastFor(vm.Id, vm.Name, vm.Face, "tentou te ligar 📞 (chamada perdida)", allowReply: false);
        }
    }

    private void RingTimeout_Tick(object? sender, EventArgs e)
    {
        _ringTimeout?.Stop();
        var peer = _callPeer;
        if (peer == null) return;
        if (_callPhase == CallPhase.Outgoing)
        {
            SendCallSignal(peer.Id, "cancel", _callId);
            EndCallLocal($"📵 {peer.Name} não atendeu", sendBye: true, sound: true);
        }
        else if (_callPhase == CallPhase.Incoming)
        {
            SendCallSignal(peer.Id, "decline", _callId);
            EndCallLocal(null, sendBye: false, sound: false);
            MissedCall(peer, notify: true);
        }
    }

    // ---------- Atender / recusar / desligar ----------

    private void AcceptIncoming()
    {
        if (_callPhase != CallPhase.Incoming || _callPeer == null) return;
        var peer = _callPeer;
        _call = MakeCallSession(peer, _callId);
        _call.Connect();
        SendCallSignal(peer.Id, "accept", _callId);
        ActivateCall();
    }

    private void DeclineIncoming()
    {
        if (_callPhase != CallPhase.Incoming || _callPeer == null) return;
        var peer = _callPeer;
        SendCallSignal(peer.Id, "decline", _callId);
        EndCallLocal("📵 Você recusou a chamada", sendBye: false, sound: false);
    }

    private void HangUp()
    {
        var peer = _callPeer;
        if (peer == null) return;
        switch (_callPhase)
        {
            case CallPhase.Outgoing:
                SendCallSignal(peer.Id, "cancel", _callId);
                EndCallLocal("📵 Você cancelou a chamada", sendBye: true, sound: false);
                break;
            case CallPhase.Active:
                SendCallSignal(peer.Id, "end", _callId);
                EndCallLocal("📞 Chamada encerrada · " + DurationText(), sendBye: true, sound: true);
                break;
        }
    }

    private void ActivateCall()
    {
        _callPhase = CallPhase.Active;
        _ringTimeout?.Stop();
        CallSounds.StopRing();
        _ringWin?.Dismiss();
        if (_fs.SoundOn) CallSounds.PlayJoin();
        _call?.Begin();
        if (_callPeer != null) _callPeer.InCall = true;
        if (_chatWith != null) RefreshChatHeader();
        RefreshCallUi();
    }

    /// <summary>Alguém desligou ou a conexão caiu sozinha.</summary>
    private void OnCallEnded(CallSession s, string reason)
    {
        if (!ReferenceEquals(s, _call)) return;
        var peer = _callPeer;
        var wasOutgoing = _callPhase == CallPhase.Outgoing;
        if (wasOutgoing && peer != null) SendCallSignal(peer.Id, "cancel", _callId);

        var friendHungUp = reason.StartsWith("Seu amigo", StringComparison.Ordinal);
        EndCallLocal(friendHungUp ? "📞 Chamada encerrada · " + DurationText() : "⚠ " + reason, sendBye: false, sound: true);
        if (!friendHungUp) StatusText.Text = reason;
    }

    private static string Clock(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{(int)d.TotalMinutes:00}:{d.Seconds:00}";

    private string DurationText()
    {
        var start = _call?.StartedAt;
        if (start == null) return "0 s";
        var d = DateTime.UtcNow - start.Value;
        return d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min {d.Seconds} s" : $"{Math.Max(0, d.Seconds)} s";
    }

    private void EndCallLocal(string? line, bool sendBye, bool sound)
    {
        if (_callPhase == CallPhase.None) return;
        var wasActive = _callPhase == CallPhase.Active;
        var peer = _callPeer;
        var call = _call;

        _callPhase = CallPhase.None;
        _call = null;
        _callPeer = null;
        _callId = "";
        _ringTimeout?.Stop();
        _callTimer?.Stop();
        CallSounds.StopRing();
        _ringWin?.Dismiss();
        _miniWin?.Dismiss();
        try { _fullWin?.Close(); } catch { /* */ }
        _fullWin = null;
        _hasFrame = false;
        _latestFrame = null;
        CallStageCtl.ShareImage.Source = null;

        if (call != null)
        {
            try { if (sendBye) call.Hangup(); else call.Dispose(); } catch { /* */ }
        }
        if (peer != null)
        {
            peer.InCall = false;
            if (line != null) AddCallLine(peer.Id, line);
        }
        if (sound && wasActive && _fs.SoundOn) CallSounds.PlayLeave();

        CallStageCtl.Render(new CallView(CallPhase.None, "", false, false, false, false, false, false, false, false, false, false, false, 0));
        UpdateCallButton();
        if (_chatWith != null) RefreshChatHeader();
    }

    // ---------- Linha no chat ("📞 Você iniciou uma chamada" etc.) ----------

    private void AddCallLine(string friendId, string text)
    {
        var line = new ChatLine { Mine = false, Text = text, Ts = NowMs(), Kind = "call" };
        _fs.AddLine(friendId, line);
        _fs.Save();
        var vm = _friendVms.FirstOrDefault(f => f.Id == friendId);
        if (vm != null) { UpdateLast(vm); SortFriends(); }
        if (_chatWith?.Id == friendId) { _chatVms.Add(ToChatVm(line)); ScrollChatEnd(); }
    }

    // ---------- Compartilhar a minha tela ----------

    /// <summary>
    /// 1) "Configurar compartilhamento" (resolução, fps, áudio; 1080p, 4K e 60 fps só com o Golden Moon)
    /// 2) "Escolha o que compartilhar" (uma janela ou a tela inteira)
    /// 3) começa a transmitir.
    /// </summary>
    private void BeginShareFlow()
    {
        var call = _call;
        if (call == null || call.Sharing) return;
        var premium = GoldenActive;

        var setup = new ShareSetupWindow(premium, _state.ShareHeight, _state.ShareFps, _state.ShareAudio) { Owner = this };
        setup.ShowDialog();
        if (setup.WantsGolden) { GoTo("Golden"); return; }
        if (!setup.Accepted || !ReferenceEquals(call, _call)) return; // cancelou (ou a chamada acabou enquanto a janelinha estava aberta)

        var opt = setup.Result.Clamp(premium); // sem o Golden Moon nunca passa de 720p / 30 fps
        var picker = new ShareSourcePickerWindow(_callPeer?.Name ?? "seu amigo") { Owner = this };
        picker.ShowDialog();
        if (picker.Chosen == null || !ReferenceEquals(call, _call)) return;

        _state.ShareHeight = opt.Height;
        _state.ShareFps = opt.Fps;
        _state.ShareAudio = opt.Audio;
        SaveState();
        call.StartShare(opt, picker.Chosen);
    }

    // ---------- Tela do amigo ----------

    private void OnCallFrame(BitmapSource frame)
    {
        _latestFrame = frame;
        if (Interlocked.Exchange(ref _frameQueued, 1) == 1) return;
        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _frameQueued, 0);
            var f = _latestFrame;
            if (f == null || _call == null) return;
            _hasFrame = true;
            CallStageCtl.ShareImage.Source = f;
            _fullWin?.SetFrame(f);
        }, DispatcherPriority.Render);
    }

    private void OpenShareFullscreen()
    {
        if (_fullWin != null) return;
        _fullWin = new ShareFullscreenWindow();
        _fullWin.Closed += (_, _) => _fullWin = null;
        if (_latestFrame != null) _fullWin.SetFrame(_latestFrame);
        _fullWin.Show();
    }

    // ---------- Desenho ----------

    private void BeginCallUi()
    {
        _callMeFace = FaceTools.FromB64(_myFaceB64);
        _hasFrame = false;
        CallStageCtl.SetNoiseSuppression(_state.CallNoiseSuppression);
        if (_callPeer != null)
        {
            CallStageCtl.SetPeople(_meName, _callMeFace, _callPeer.Name, _callPeer.Face);
            if (_callPhase != CallPhase.Incoming) { /* quem liga já aparece "em chamada" só depois de atender */ }
        }
        _callTimer?.Start();
        UpdateCallButton();
        RefreshCallUi();
    }

    private void UpdateCallButton()
    {
        var f = _chatWith;
        CallBtn.IsEnabled = f != null && f.Online && _callPhase == CallPhase.None;
        CallBtn.ToolTip = _callPhase != CallPhase.None ? "Você já está em uma chamada"
            : f is { Online: false } ? "Seu amigo está offline" : "Ligar (chamada de voz)";
    }

    private void RefreshCallUi()
    {
        if (_callPhase == CallPhase.None || _callPeer == null)
        {
            CallStageCtl.Visibility = Visibility.Collapsed;
            return;
        }

        var peer = _callPeer;
        var c = _call;
        if (c != null && !c.Watching) _hasFrame = false;

        var dur = c?.StartedAt is { } st ? Clock(DateTime.UtcNow - st) : "00:00";

        var status = _callPhase switch
        {
            CallPhase.Outgoing => $"📞 Chamando {peer.Name}…",
            CallPhase.Incoming => $"📞 {peer.Name} está te ligando…",
            _ => c == null ? "" : c.Linked ? $"🟢 Chamada de voz · {dur}" : $"⏳ {c.LinkText}"
        };
        if (_callPhase == CallPhase.Active && c is { Notice.Length: > 0 }) status += "  ·  ⚠ " + c.Notice;

        var pulse = 0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 260.0);
        var view = new CallView(_callPhase, status,
            c?.Muted ?? false, c?.Deafened ?? false, c?.PeerMuted ?? false, c?.PeerDeafened ?? false,
            c?.MySpeaking ?? false, c?.PeerSpeaking ?? false,
            c?.Sharing ?? false, c?.PeerSharing ?? false, c?.Watching ?? false, _hasFrame,
            c?.ShareAudioFailed ?? false, pulse, c?.ShareLabel ?? "");

        var stageVisible = _chatWith?.Id == peer.Id && PageFriends.IsVisible && IsVisible && WindowState != WindowState.Minimized;
        if (stageVisible)
        {
            CallStageCtl.Render(view);
            _miniWin?.Dismiss();
        }
        else
        {
            CallStageCtl.Visibility = Visibility.Collapsed;
            if (_callPhase == CallPhase.Active)
            {
                if (_miniWin == null)
                {
                    _miniWin = new CallMiniWindow();
                    _miniWin.MuteClicked += () =>
                    {
                        if (_call == null) return;
                        if (_call.Deafened) _call.SetDeafened(false); else _call.SetMuted(!_call.Muted);
                    };
                    _miniWin.HangupClicked += HangUp;
                    _miniWin.OpenRequested += () => { if (_callPeer != null) OpenFriendChat(_callPeer.Id); };
                }
                _miniWin.Update(peer.Name, peer.Face, _meName, _callMeFace ?? peer.Face, dur,
                    c?.Muted == true || c?.Deafened == true, c?.MySpeaking == true, c?.PeerSpeaking == true);
            }
        }
    }
}
