using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Codecs;
using NAudio.Wave;

namespace MoonlightLauncher;

// ====================================================================================
//  Chamadas: voz + compartilhamento de tela (com o som do PC).
//
//  Como funciona:
//   • O "toque" (ligar / atender / recusar / cancelar) viaja pelo próprio chat dos amigos, em mensagens
//     especiais que o launcher esconde (ver CallsUi.cs). Assim funciona com o servidor atual.
//   • A voz e a tela passam por um canal próprio do servidor: wss://…/call (veja servidor-chamadas/).
//     Os dois lados entram na mesma "sala" (id da chamada) e o servidor só repassa os bytes de um para o outro.
//   • Voz: microfone 16 kHz com supressão de ruído (NoiseSuppressor.cs), só enviada quando você fala (G.711 µ-law).
//     Tela: JPEG da janela ou do monitor escolhido, de 480p até 4K e de 15 até 60 quadros/s (ver ShareSources.cs).
//     Som da tela: o áudio que está saindo do PC (loopback do Windows), 24 kHz mono.
//   • A tela vai por uma segunda sala ("#m") para um quadro grande nunca atrasar a sua voz.
// ====================================================================================

internal enum CallLink { Connecting, Waiting, Ready, Reconnecting, Failed }

// ------------------------------------------------------------------------------------
//  Conexão com o canal /call (uma sala com duas pessoas). Reconecta sozinha se cair.
// ------------------------------------------------------------------------------------
internal sealed class CallTransport : IDisposable
{
    private readonly string _base, _id, _token, _room, _peer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<OutMsg> _q;
    private int _videoQueued;
    private volatile int _state = (int)CallLink.Connecting;
    private volatile bool _gaveUp;

    private readonly record struct OutMsg(byte[] Data, bool IsText, bool IsVideo);

    public CallLink State => (CallLink)_state;
    public bool Ready => State == CallLink.Ready;
    public string Error { get; private set; } = "";

    public event Action<CallLink>? StateChanged;
    public event Action<JsonElement>? Text;
    public event Action<byte[]>? Binary;

    public CallTransport(string relayBase, string myId, string token, string room, string peerId)
    {
        _base = relayBase.TrimEnd('/');
        _id = myId; _token = token; _room = room; _peer = peerId;
        _q = Channel.CreateBounded<OutMsg>(
            new BoundedChannelOptions(80) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false },
            dropped => { if (dropped.IsVideo) Volatile.Write(ref _videoQueued, 0); });
    }

    public void Start() => _ = Task.Run(RunAsync);

    private void SetState(CallLink s)
    {
        if (State == s) return;
        _state = (int)s;
        try { StateChanged?.Invoke(s); }
        catch (Exception ex) { LauncherLog.Write("Chamada: erro ao tratar o estado da conexão", ex); }
    }

    private static string Describe(Exception ex)
    {
        if (ex is OperationCanceledException) return "o servidor demorou demais para responder.";
        var m = ex.InnerException?.Message ?? ex.Message;
        if (m.Contains("404")) return "o servidor dos amigos ainda não tem o recurso de chamadas (falta atualizar o servidor).";
        if (m.Contains("502") || m.Contains("503")) return "o servidor está fora do ar ou ainda ligando.";
        return m;
    }

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        var fails = 0;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            using var conn = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var wasReady = false;
            try
            {
                SetState(fails == 0 ? CallLink.Connecting : CallLink.Reconnecting);
                using (var to = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    to.CancelAfter(TimeSpan.FromSeconds(45)); // o Render gratuito pode estar acordando
                    await ws.ConnectAsync(new Uri(_base + "/call"), to.Token);
                    var join = JsonSerializer.SerializeToUtf8Bytes(new { t = "join", id = _id, token = _token, room = _room, peer = _peer });
                    await ws.SendAsync(new ArraySegment<byte>(join), WebSocketMessageType.Text, true, to.Token);
                }
                SetState(CallLink.Waiting);
                wasReady = await ReadLoopAsync(ws, conn, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Error = Describe(ex);
                LauncherLog.Write("Chamada: conexão com o canal /call", ex);
                if (ex.InnerException?.Message.Contains("404") == true || ex.Message.Contains("404"))
                {
                    SetState(CallLink.Failed);
                    return;
                }
            }
            finally
            {
                try { conn.Cancel(); } catch { /* */ }
                try { ws.Abort(); } catch { /* */ }
            }

            if (ct.IsCancellationRequested) return;
            if (_gaveUp) { SetState(CallLink.Failed); return; }

            fails = wasReady ? 1 : fails + 1;
            if (fails > 6)
            {
                if (Error.Length == 0) Error = "a conexão da chamada caiu.";
                SetState(CallLink.Failed);
                return;
            }
            SetState(CallLink.Reconnecting);
            try { await Task.Delay(1500, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<bool> ReadLoopAsync(ClientWebSocket ws, CancellationTokenSource conn, CancellationToken ct)
    {
        var ready = false;
        var buf = new byte[32 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) return ready;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 3 * 1024 * 1024) return ready;
            } while (!r.EndOfMessage);

            if (r.MessageType == WebSocketMessageType.Binary)
            {
                try { Binary?.Invoke(ms.ToArray()); }
                catch (Exception ex) { LauncherLog.Write("Chamada: erro ao tratar pacote", ex); }
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(ms.ToArray());
                var root = doc.RootElement;
                var t = Jx.S(root, "t");
                if (t == "ready")
                {
                    ready = true;
                    while (_q.Reader.TryRead(out _)) { /* descarta o que ficou velho */ }
                    Volatile.Write(ref _videoQueued, 0);
                    StartWriter(ws, conn.Token);
                    SetState(CallLink.Ready);
                }
                else if (t == "error")
                {
                    Error = Jx.S(root, "code") switch
                    {
                        "auth" => "o servidor não reconheceu o seu launcher.",
                        "badroom" => "sala de chamada inválida.",
                        "timeout" => "o seu amigo não entrou na chamada.",
                        _ => "o servidor recusou a chamada."
                    };
                    _gaveUp = true;
                    return ready;
                }
                else
                {
                    Text?.Invoke(root.Clone());
                }
            }
            catch (JsonException) { /* mensagem estranha: ignora */ }
            catch (Exception ex) { LauncherLog.Write("Chamada: erro ao tratar mensagem", ex); }
        }
        return ready;
    }

    private void StartWriter(ClientWebSocket ws, CancellationToken ct) => _ = Task.Run(async () =>
    {
        try
        {
            while (await _q.Reader.WaitToReadAsync(ct))
            {
                while (_q.Reader.TryRead(out var m))
                {
                    if (ws.State != WebSocketState.Open) return;
                    await ws.SendAsync(new ArraySegment<byte>(m.Data),
                        m.IsText ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, ct);
                    if (m.IsVideo) Volatile.Write(ref _videoQueued, 0);
                }
            }
        }
        catch { /* conexão fechou: o RunAsync reconecta */ }
    });

    public void SendControl(object msg)
    {
        if (!Ready) return;
        _q.Writer.TryWrite(new OutMsg(JsonSerializer.SerializeToUtf8Bytes(msg), true, false));
    }

    public void SendAudio(byte[] packet)
    {
        if (!Ready) return;
        _q.Writer.TryWrite(new OutMsg(packet, false, false));
    }

    /// <summary>O quadro anterior da tela ainda está na fila/na rede (quem captura pode pular este quadro e poupar CPU).</summary>
    public bool VideoBusy => Volatile.Read(ref _videoQueued) != 0;

    /// <summary>Quadro de tela: se o anterior ainda não saiu, este é descartado (tela tolera perda, voz não).</summary>
    public bool SendVideo(byte[] packet)
    {
        if (!Ready) return false;
        if (Interlocked.CompareExchange(ref _videoQueued, 1, 0) != 0) return false;
        _q.Writer.TryWrite(new OutMsg(packet, false, true));
        return true;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* */ }
        try { _q.Writer.TryComplete(); } catch { /* */ }
    }
}

// ------------------------------------------------------------------------------------
//  Toca áudio PCM que chega da rede (G.711 µ-law → alto-falante).
// ------------------------------------------------------------------------------------
internal sealed class PcmPlayer : IDisposable
{
    private readonly WaveOutEvent _out;
    private readonly BufferedWaveProvider _buf;

    public PcmPlayer(int sampleRate)
    {
        _buf = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMilliseconds(700),
            DiscardOnBufferOverflow = true
        };
        _out = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        _out.Init(_buf);
        _out.Play();
    }

    /// <summary>Decodifica e toca. Devolve o volume (0..1) do trecho, para o indicador de "falando".</summary>
    public double Feed(byte[] packet, int offset, double gain, bool play)
    {
        var n = packet.Length - offset;
        if (n <= 0) return 0;
        var pcm = new byte[n * 2];
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            int s = MuLawDecoder.MuLawToLinearSample(packet[offset + i]);
            sum += (double)s * s;
            var v = (int)Math.Clamp(s * gain, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        if (play) _buf.AddSamples(pcm, 0, pcm.Length);
        return Math.Sqrt(sum / n) / 32768.0;
    }

    public void Clear() { try { _buf.ClearBuffer(); } catch { /* */ } }

    public void Dispose()
    {
        try { _out.Stop(); } catch { /* */ }
        try { _out.Dispose(); } catch { /* */ }
    }
}

// ------------------------------------------------------------------------------------
//  Voz: microfone → rede, rede → alto-falante, e a detecção de "está falando".
// ------------------------------------------------------------------------------------
internal sealed class VoiceEngine : IDisposable
{
    public const int Rate = 16000;
    private const double Threshold = 0.012;      // ~ -38 dBFS: abaixo disso é silêncio/ruído de fundo
    private const long HoldMs = 280;             // continua "falando" um pouquinho depois da voz parar

    private readonly Action<byte[]> _send;
    private WaveInEvent? _mic;
    private PcmPlayer? _player;
    private volatile bool _muted, _deaf;
    private long _micLoudAt, _peerLoudAt;
    private volatile int _gainPct = 100;
    private volatile bool _noiseOn = true;
    private bool _nsWasOn;
    private readonly NoiseSuppressor _ns = new();

    public string Error { get; private set; } = "";

    public VoiceEngine(Action<byte[]> sendPacket) => _send = sendPacket;

    public bool MySpeaking => !_muted && !_deaf && Environment.TickCount64 - Interlocked.Read(ref _micLoudAt) < HoldMs;
    public bool PeerSpeaking => Environment.TickCount64 - Interlocked.Read(ref _peerLoudAt) < HoldMs;

    public void SetMuted(bool v) => _muted = v;
    public void SetDeafened(bool v)
    {
        _deaf = v;
        if (v) _player?.Clear();
    }
    public void SetVolume(double v) => _gainPct = (int)Math.Clamp(v * 100, 0, 300);

    /// <summary>Liga/desliga a supressão de ruído do microfone.</summary>
    public void SetNoiseSuppression(bool on) => _noiseOn = on;

    public void Start()
    {
        try { _player = new PcmPlayer(Rate); }
        catch (Exception ex)
        {
            LauncherLog.Write("Chamada: não abriu o alto-falante", ex);
            Error = "Não consegui abrir o alto-falante.";
        }

        try
        {
            if (WaveInEvent.DeviceCount == 0)
            {
                Error = "Nenhum microfone foi encontrado. Você ouve o amigo, mas ele não ouve você.";
                return;
            }
            _mic = new WaveInEvent
            {
                DeviceNumber = -1, // microfone padrão do Windows
                WaveFormat = new WaveFormat(Rate, 16, 1),
                BufferMilliseconds = 40,
                NumberOfBuffers = 3
            };
            _mic.DataAvailable += OnMic;
            _mic.StartRecording();
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Chamada: não abriu o microfone", ex);
            Error = "Não consegui abrir o microfone (confira Configurações do Windows → Privacidade → Microfone).";
            try { _mic?.Dispose(); } catch { /* */ }
            _mic = null;
        }
    }

    private void OnMic(object? sender, WaveInEventArgs e)
    {
        var n = e.BytesRecorded / 2;
        if (n <= 0) return;

        var raw = new short[n];
        Buffer.BlockCopy(e.Buffer, 0, raw, 0, n * 2);

        // Supressão de ruído: o que passa daqui já está limpo (e é por ele que se decide se você está falando).
        short[] pcm;
        if (_noiseOn)
        {
            if (!_nsWasOn) { _ns.Reset(); _nsWasOn = true; }
            pcm = _ns.Process(raw);
        }
        else
        {
            _nsWasOn = false;
            pcm = raw;
        }

        var m = pcm.Length;
        if (m == 0) return; // o filtro ainda está juntando o primeiro pedacinho

        double sum = 0;
        for (var i = 0; i < m; i++) sum += (double)pcm[i] * pcm[i];
        var rms = Math.Sqrt(sum / m) / 32768.0;

        var now = Environment.TickCount64;
        if (rms > Threshold) Interlocked.Exchange(ref _micLoudAt, now);
        if (_muted || _deaf) return;
        if (now - Interlocked.Read(ref _micLoudAt) >= HoldMs) return; // silêncio: não gasta internet

        var pkt = new byte[m + 1];
        pkt[0] = CallPacket.Voice;
        for (var i = 0; i < m; i++) pkt[i + 1] = MuLawEncoder.LinearToMuLawSample(pcm[i]);
        _send(pkt);
    }

    public void OnPeerVoice(byte[] packet)
    {
        var level = _player?.Feed(packet, 1, _gainPct / 100.0, !_deaf) ?? 0;
        if (level > Threshold) Interlocked.Exchange(ref _peerLoudAt, Environment.TickCount64);
    }

    public void Dispose()
    {
        try { if (_mic != null) { _mic.DataAvailable -= OnMic; _mic.StopRecording(); } } catch { /* */ }
        try { _mic?.Dispose(); } catch { /* */ }
        _mic = null;
        try { _player?.Dispose(); } catch { /* */ }
        _player = null;
    }
}

internal static class CallPacket
{
    public const byte Voice = 1;       // µ-law 16 kHz
    public const byte ShareAudio = 2;  // µ-law 24 kHz (som do PC)
    public const byte Video = 3;       // JPEG
}

// ------------------------------------------------------------------------------------
//  Som do PC (o que está tocando nas caixas/fones) → mono 24 kHz → µ-law.
// ------------------------------------------------------------------------------------
internal sealed class LoopbackSender : IDisposable
{
    public const int OutRate = 24000;
    private readonly Action<byte[]> _send;
    private WasapiLoopbackCapture? _cap;
    private int _channels, _bits;
    private double _step, _phase, _prev, _prevRaw;
    private readonly List<byte> _acc = new(1200);
    private double _peak;

    public LoopbackSender(Action<byte[]> sendPacket) => _send = sendPacket;

    public void Start()
    {
        var cap = new WasapiLoopbackCapture();
        var f = cap.WaveFormat;
        _channels = Math.Max(1, f.Channels);
        _bits = f.BitsPerSample;
        _step = f.SampleRate / (double)OutRate;
        if (_bits != 32 && _bits != 16) { cap.Dispose(); throw new NotSupportedException("formato de áudio do Windows não suportado: " + _bits + " bits"); }
        cap.DataAvailable += OnData;
        cap.StartRecording();
        _cap = cap;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var bytesPer = _bits / 8;
        var frameBytes = bytesPer * _channels;
        var frames = e.BytesRecorded / frameBytes;
        var buf = e.Buffer;

        for (var i = 0; i < frames; i++)
        {
            var off = i * frameBytes;
            double m = 0;
            for (var c = 0; c < _channels; c++)
            {
                var p = off + c * bytesPer;
                m += _bits == 32 ? BitConverter.ToSingle(buf, p) : BitConverter.ToInt16(buf, p) / 32768.0;
            }
            m /= _channels;

            var x = (m + _prevRaw) * 0.5; // filtro simples antes de reduzir a taxa (evita chiado)
            _prevRaw = m;
            while (_phase < 1.0)
            {
                var y = _prev + (x - _prev) * _phase;
                Emit(y);
                _phase += _step;
            }
            _phase -= 1.0;
            _prev = x;
        }
    }

    private void Emit(double y)
    {
        var a = Math.Abs(y);
        if (a > _peak) _peak = a;
        var s = (short)Math.Clamp(y * 32767.0, short.MinValue, short.MaxValue);
        _acc.Add(MuLawEncoder.LinearToMuLawSample(s));
        if (_acc.Count < 960) return; // 40 ms

        if (_peak > 0.002) // pacote totalmente mudo não precisa ir
        {
            var pkt = new byte[_acc.Count + 1];
            pkt[0] = CallPacket.ShareAudio;
            _acc.CopyTo(pkt, 1);
            _send(pkt);
        }
        _acc.Clear();
        _peak = 0;
    }

    public void Dispose()
    {
        try { if (_cap != null) { _cap.DataAvailable -= OnData; _cap.StopRecording(); } } catch { /* */ }
        try { _cap?.Dispose(); } catch { /* */ }
        _cap = null;
    }
}

// ------------------------------------------------------------------------------------
//  Captura da janela (ou do monitor) escolhida → JPEG. Só roda enquanto o amigo está assistindo.
//  Resolução e quadros por segundo vêm de ShareOptions. Se a internet não der conta, quadros são pulados
//  (o fps real cai sozinho, mas a imagem nunca atrasa).
// ------------------------------------------------------------------------------------
internal sealed class ScreenShareSender : IDisposable
{
    private readonly Func<byte[], bool> _send;
    private readonly Func<bool> _busy;
    private readonly ShareOptions _opt;
    private readonly ShareSource _src;
    private readonly Action<string>? _lost;
    private Thread? _thread;
    private volatile bool _stop;

    public ScreenShareSender(Func<byte[], bool> sendPacket, Func<bool> networkBusy, ShareOptions options, ShareSource source, Action<string>? sourceLost = null)
    {
        _send = sendPacket; _busy = networkBusy; _opt = options; _src = source; _lost = sourceLost;
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "MoonlightScreenShare", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Loop()
    {
        timeBeginPeriod(1); // sem isso o Windows só acorda a thread de ~15 em ~15 ms e 60 fps não sai
        try
        {
            var sw = Stopwatch.StartNew();
            byte[]? px = null, prev = null;
            long lastSent = -10000;
            var period = Math.Max(1, 1000 / Math.Clamp(_opt.Fps, 1, 60));
            var maxW = _opt.MaxWidth;
            var maxH = _opt.Height;
            var fails = 0;

            while (!_stop)
            {
                var t0 = sw.ElapsedMilliseconds;
                try
                {
                    if (!_busy()) // a rede ainda está mandando o quadro anterior: pula este (nem gasta CPU capturando)
                    {
                        var r = ShareCapture.Grab(_src, maxW, maxH, true, ref px, out var w, out var h);
                        if (r == GrabResult.Gone)
                        {
                            _lost?.Invoke("A janela compartilhada foi fechada.");
                            return;
                        }
                        if (r == GrabResult.Ok && px != null)
                        {
                            fails = 0;
                            var same = prev != null && prev.Length == px.Length && prev.AsSpan().SequenceEqual(px.AsSpan());
                            if (!same || t0 - lastSent > 1500) // tela parada: manda só 1 quadro a cada ~1,5 s
                            {
                                var jpg = Encode(px, w, h);
                                var pkt = new byte[jpg.Length + 1];
                                pkt[0] = CallPacket.Video;
                                Buffer.BlockCopy(jpg, 0, pkt, 1, jpg.Length);
                                if (_send(pkt)) lastSent = t0;
                            }
                            (px, prev) = (prev, px); // o quadro novo vira "anterior"; o buffer velho é reaproveitado
                        }
                        else if (++fails == 60)
                        {
                            LauncherLog.Write("Chamada: a captura da tela está falhando (janela minimizada ou protegida?)");
                            fails = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Write("Chamada: falha ao capturar a tela", ex);
                    Thread.Sleep(800);
                }
                var wait = period - (int)(sw.ElapsedMilliseconds - t0);
                Thread.Sleep(Math.Max(1, wait));
            }
        }
        finally { timeEndPeriod(1); }
    }

    private byte[] Encode(byte[] px, int w, int h)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = _opt.JpegQuality };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _stop = true;
        try { _thread?.Join(1500); } catch { /* */ }
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
}

// ------------------------------------------------------------------------------------
//  A chamada em si: junta voz, tela e os avisos de estado (mudo, surdo, transmitindo…).
// ------------------------------------------------------------------------------------
public sealed class CallSession : IDisposable
{
    private readonly string _relay, _myId, _myToken, _peerId;
    private readonly object _gate = new();
    private CallTransport? _voiceT, _mediaT;
    private VoiceEngine? _voice;
    private ScreenShareSender? _screen;
    private LoopbackSender? _loop;
    private PcmPlayer? _streamPlayer;
    private volatile bool _disposed, _begun, _sharing, _shareAudio;
    private ShareOptions _shareOpt = new();
    private ShareSource? _shareSrc;
    private int _decoding;
    private long _peerShareLoudAt;
    private int _voiceVolPct = 100, _streamVolPct = 100;

    public string CallId { get; }

    public bool Muted { get; private set; }
    public bool Deafened { get; private set; }
    public bool PeerMuted { get; private set; }
    public bool PeerDeafened { get; private set; }
    public bool PeerSharing { get; private set; }
    public bool PeerSharingAudio { get; private set; }
    public bool PeerWatching { get; private set; }
    public bool Watching { get; private set; }
    public bool Sharing => _sharing;
    public bool SharingAudio => _sharing && _shareAudio;
    public bool ShareAudioFailed { get; private set; }
    public bool AutoWatch { get; set; } = true;

    /// <summary>Supressão de ruído do microfone (ligada por padrão).</summary>
    public bool NoiseSuppression { get; set; } = true;

    /// <summary>Texto do que está sendo transmitido, ex.: "janela: Minecraft · 1080p · 60 fps".</summary>
    public string ShareLabel
    {
        get
        {
            if (!_sharing) return "";
            var what = _shareSrc == null ? "tela" : _shareSrc.IsScreen ? _shareSrc.Title : "janela: " + Short(_shareSrc.Title, 36);
            return what + " · " + _shareOpt.Label;
        }
    }

    private static string Short(string t, int max) => t.Length <= max ? t : t[..(max - 1)] + "…";

    /// <summary>Aviso de problema não fatal (ex.: sem microfone).</summary>
    public string Notice { get; private set; } = "";

    /// <summary>A conexão de voz com o amigo está pronta (os dois entraram na sala).</summary>
    public bool Linked => _voiceT?.Ready == true;
    public string LinkText => _voiceT?.State switch
    {
        CallLink.Ready => "Conectado",
        CallLink.Waiting => "Esperando seu amigo entrar…",
        CallLink.Reconnecting => "Reconectando…",
        CallLink.Failed => "Sem conexão",
        _ => "Conectando…"
    };
    public DateTime? StartedAt { get; private set; }

    public bool MySpeaking => _voice?.MySpeaking == true;
    public bool PeerSpeaking => !PeerMuted && _voice?.PeerSpeaking == true;
    public bool StreamAudioPlaying => Environment.TickCount64 - Interlocked.Read(ref _peerShareLoudAt) < 400;

    /// <summary>Algo mudou (mudo, transmissão, conexão…). Pode vir de qualquer thread.</summary>
    public event Action? Changed;
    /// <summary>A chamada acabou sozinha (amigo desligou ou a conexão caiu). Texto em português. Pode vir de qualquer thread.</summary>
    public event Action<string>? Ended;
    /// <summary>Quadro da tela do amigo, já decodificado e congelado. Pode vir de qualquer thread.</summary>
    public event Action<BitmapSource>? Frame;

    public CallSession(string relayBase, string myId, string myToken, string peerId, string callId)
    {
        _relay = relayBase; _myId = myId; _myToken = myToken; _peerId = peerId; CallId = callId;
    }

    private void Raise()
    {
        try { Changed?.Invoke(); } catch (Exception ex) { LauncherLog.Write("Chamada: Changed", ex); }
    }

    /// <summary>Abre a conexão com o servidor (ainda sem microfone). Pode ser chamada já ao ligar para o amigo.</summary>
    public void Connect()
    {
        if (_voiceT != null) return;
        var t = new CallTransport(_relay, _myId, _myToken, CallId, _peerId);
        t.StateChanged += s => OnVoiceState(t, s);
        t.Text += HandleControl;
        t.Binary += OnVoiceBinary;
        _voiceT = t;
        t.Start();
    }

    /// <summary>A chamada foi atendida: liga o microfone e o alto-falante.</summary>
    public void Begin()
    {
        if (_begun || _disposed) return;
        _begun = true;
        Connect();
        var v = new VoiceEngine(pkt => _voiceT?.SendAudio(pkt));
        v.SetVolume(_voiceVolPct / 100.0);
        v.SetMuted(Muted);
        v.SetDeafened(Deafened);
        v.SetNoiseSuppression(NoiseSuppression);
        v.Start();
        _voice = v;
        Notice = v.Error;
        Raise();
    }

    private void OnVoiceState(CallTransport t, CallLink s)
    {
        if (_disposed) return;
        if (s == CallLink.Ready)
        {
            StartedAt ??= DateTime.UtcNow;
            SendState();
            if (_sharing) t.SendControl(new { t = "share", on = true, audio = _shareAudio });
            if (Watching) t.SendControl(new { t = "watch", on = true });
        }
        else if (s == CallLink.Failed)
        {
            Ended?.Invoke("Não consegui manter a chamada: " + (t.Error.Length > 0 ? t.Error : "a conexão caiu."));
        }
        Raise();
    }

    private void SendState() => _voiceT?.SendControl(new { t = "state", mute = Muted, deaf = Deafened });

    private void OnVoiceBinary(byte[] d)
    {
        if (d.Length > 1 && d[0] == CallPacket.Voice) _voice?.OnPeerVoice(d);
    }

    private void HandleControl(JsonElement m)
    {
        switch (Jx.S(m, "t"))
        {
            case "state":
                PeerMuted = Jx.B(m, "mute");
                PeerDeafened = Jx.B(m, "deaf");
                break;
            case "share":
                PeerSharing = Jx.B(m, "on");
                PeerSharingAudio = Jx.B(m, "audio");
                if (PeerSharing && AutoWatch) SetWatching(true);
                else if (!PeerSharing && Watching) { Watching = false; UpdateMedia(); }
                break;
            case "watch":
                PeerWatching = Jx.B(m, "on");
                UpdateMedia();
                break;
            case "bye":
                Ended?.Invoke("Seu amigo encerrou a chamada.");
                return;
        }
        Raise();
    }

    // ---- microfone / fone ----

    public void SetMuted(bool v)
    {
        Muted = v;
        _voice?.SetMuted(v);
        SendState();
        Raise();
    }

    public void SetDeafened(bool v)
    {
        Deafened = v;
        _voice?.SetDeafened(v);
        SendState();
        Raise();
    }

    public void SetVoiceVolume(double v)
    {
        _voiceVolPct = (int)Math.Clamp(v * 100, 0, 300);
        _voice?.SetVolume(v);
    }

    public void SetStreamVolume(double v) => _streamVolPct = (int)Math.Clamp(v * 100, 0, 300);

    public void SetNoiseSuppression(bool on)
    {
        NoiseSuppression = on;
        _voice?.SetNoiseSuppression(on);
    }

    // ---- tela ----

    /// <summary>Começa a transmitir a tela. A captura só liga quando o amigo abre a transmissão.</summary>
    public void StartShare(ShareOptions options, ShareSource source)
    {
        if (_sharing || _disposed) return;
        _shareOpt = options;
        _shareSrc = source;
        _sharing = true;
        _shareAudio = options.Audio;
        ShareAudioFailed = false;
        _voiceT?.SendControl(new { t = "share", on = true, audio = options.Audio });
        Raise();
    }

    /// <summary>A janela que estava sendo compartilhada fechou: para a transmissão e avisa.</summary>
    private void OnShareSourceLost(string why)
    {
        Notice = why;
        _ = Task.Run(async () =>
        {
            try { StopShare(); } catch (Exception ex) { LauncherLog.Write("Chamada: parar a transmissão", ex); }
            await Task.Delay(7000);
            if (Notice == why) { Notice = ""; Raise(); }
        });
    }

    public void StopShare()
    {
        if (!_sharing) return;
        _sharing = false;
        _voiceT?.SendControl(new { t = "share", on = false, audio = false });
        UpdateMedia();
        Raise();
    }

    /// <summary>Abre (ou fecha) a transmissão do amigo.</summary>
    public void SetWatching(bool on)
    {
        if (on && !PeerSharing) return;
        if (Watching == on) return;
        Watching = on;
        _voiceT?.SendControl(new { t = "watch", on });
        UpdateMedia();
        Raise();
    }

    private void UpdateMedia()
    {
        if (_disposed) return;
        var capture = _sharing && PeerWatching;
        var need = Watching || capture;
        CallTransport? toClose = null;
        CallTransport? created = null;
        lock (_gate)
        {
            if (need && _mediaT == null)
            {
                var t = new CallTransport(_relay, _myId, _myToken, CallId + "#m", _peerId);
                t.StateChanged += s => OnMediaState(t, s);
                t.Binary += OnMediaBinary;
                _mediaT = t;
                created = t;
            }
            else if (!need && _mediaT != null)
            {
                toClose = _mediaT;
                _mediaT = null;
            }
        }

        created?.Start();
        if (!capture) StopCapture();
        else if (_mediaT is { Ready: true } ready) StartCapture(ready);

        if (!Watching)
        {
            var p = _streamPlayer;
            _streamPlayer = null;
            try { p?.Dispose(); } catch { /* */ }
        }
        toClose?.Dispose();
    }

    private void OnMediaState(CallTransport t, CallLink s)
    {
        if (_disposed) return;
        if (s == CallLink.Ready && _sharing && PeerWatching) StartCapture(t);
        Raise();
    }

    private void StartCapture(CallTransport t)
    {
        lock (_gate)
        {
            if (_screen != null) return;
            if (_shareSrc == null) return;
            _screen = new ScreenShareSender(pkt => t.SendVideo(pkt), () => t.VideoBusy, _shareOpt, _shareSrc, OnShareSourceLost);
            _screen.Start();

            if (_shareAudio && _loop == null)
            {
                try
                {
                    var l = new LoopbackSender(pkt => t.SendAudio(pkt));
                    l.Start();
                    _loop = l;
                }
                catch (Exception ex)
                {
                    LauncherLog.Write("Chamada: não consegui capturar o som do PC", ex);
                    ShareAudioFailed = true;
                }
            }
        }
    }

    private void StopCapture()
    {
        ScreenShareSender? s;
        LoopbackSender? l;
        lock (_gate)
        {
            s = _screen; _screen = null;
            l = _loop; _loop = null;
        }
        try { s?.Dispose(); } catch { /* */ }
        try { l?.Dispose(); } catch { /* */ }
    }

    private void OnMediaBinary(byte[] d)
    {
        if (!Watching || d.Length < 2) return;

        if (d[0] == CallPacket.Video)
        {
            if (Interlocked.Exchange(ref _decoding, 1) == 1) return; // ainda decodificando o anterior: pula
            try
            {
                using var ms = new MemoryStream(d, 1, d.Length - 1);
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var fr = dec.Frames[0];
                fr.Freeze();
                Frame?.Invoke(fr);
            }
            catch { /* quadro quebrado: pula */ }
            finally { Volatile.Write(ref _decoding, 0); }
        }
        else if (d[0] == CallPacket.ShareAudio)
        {
            try
            {
                _streamPlayer ??= new PcmPlayer(LoopbackSender.OutRate);
                var level = _streamPlayer.Feed(d, 1, _streamVolPct / 100.0, !Deafened);
                if (level > 0.004) Interlocked.Exchange(ref _peerShareLoudAt, Environment.TickCount64);
            }
            catch (Exception ex) { LauncherLog.Write("Chamada: som da transmissão", ex); }
        }
    }

    // ---- encerrar ----

    /// <summary>Desliga: avisa o amigo, para o microfone na hora e fecha a conexão logo depois (para o aviso sair).</summary>
    public void Hangup()
    {
        if (_disposed) return;
        try { _voiceT?.SendControl(new { t = "bye" }); } catch { /* */ }
        try { _voice?.Dispose(); } catch { /* */ }
        _voice = null;
        StopCapture();
        _ = Task.Run(async () =>
        {
            await Task.Delay(350);
            Dispose();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        try { _voice?.Dispose(); } catch { /* */ }
        try { _streamPlayer?.Dispose(); } catch { /* */ }
        CallTransport? m, v;
        lock (_gate) { m = _mediaT; _mediaT = null; v = _voiceT; _voiceT = null; }
        try { m?.Dispose(); } catch { /* */ }
        try { v?.Dispose(); } catch { /* */ }
    }
}

// ------------------------------------------------------------------------------------
//  Sons da chamada: toque de quem recebe, "chamando" de quem liga, entrar e sair.
// ------------------------------------------------------------------------------------
public static class CallSounds
{
    private static byte[]? _ring, _ringback, _join, _leave;
    private static SoundPlayer? _loop;
    private const int Rate = 44100;

    public static void StartRing(bool incoming)
    {
        try
        {
            StopRing();
            var wav = incoming ? (_ring ??= BuildRing()) : (_ringback ??= BuildRingback());
            _loop = new SoundPlayer(new MemoryStream(wav));
            _loop.PlayLooping();
        }
        catch (Exception ex) { LauncherLog.Write("Não consegui tocar o toque da chamada", ex); }
    }

    public static void StopRing()
    {
        try { _loop?.Stop(); } catch { /* */ }
        try { _loop?.Dispose(); } catch { /* */ }
        _loop = null;
    }

    public static void PlayJoin() => PlayOnce(ref _join, true);
    public static void PlayLeave() => PlayOnce(ref _leave, false);

    private static void PlayOnce(ref byte[]? cache, bool up)
    {
        try
        {
            StopRing();
            cache ??= BuildBlip(up);
            new SoundPlayer(new MemoryStream(cache)).Play();
        }
        catch (Exception ex) { LauncherLog.Write("Som da chamada", ex); }
    }

    private static void Bell(double[] buf, double start, double dur, double freq, double gain)
    {
        var s0 = (int)(start * Rate);
        var n = (int)(dur * Rate);
        for (var i = 0; i < n && s0 + i < buf.Length; i++)
        {
            var t = i / (double)Rate;
            var env = Math.Min(1.0, t / 0.01) * Math.Exp(-t / (dur * 0.55));
            var w = Math.Sin(2 * Math.PI * freq * t) + 0.30 * Math.Sin(2 * Math.PI * freq * 2 * t) * Math.Exp(-t / 0.10);
            buf[s0 + i] += w * env * gain;
        }
    }

    // Toque de quem recebe: arpejo alegre que repete (≈ 2,6 s).
    private static byte[] BuildRing()
    {
        var buf = new double[(int)(Rate * 2.6)];
        double[] notes = { 659.3, 784.0, 988.0, 784.0 };
        for (var k = 0; k < 2; k++)
            for (var i = 0; i < notes.Length; i++)
                Bell(buf, k * 0.62 + i * 0.13, 0.30, notes[i], 0.26);
        return ToWav(buf);
    }

    // "Chamando…" de quem liga: duas batidinhas suaves e uma pausa (≈ 3 s).
    private static byte[] BuildRingback()
    {
        var buf = new double[(int)(Rate * 3.0)];
        Bell(buf, 0.00, 0.55, 523.3, 0.22);
        Bell(buf, 0.00, 0.55, 659.3, 0.18);
        Bell(buf, 0.60, 0.55, 523.3, 0.22);
        Bell(buf, 0.60, 0.55, 659.3, 0.18);
        return ToWav(buf);
    }

    private static byte[] BuildBlip(bool up)
    {
        var buf = new double[(int)(Rate * 0.40)];
        Bell(buf, 0.00, 0.20, up ? 659.3 : 880.0, 0.30);
        Bell(buf, 0.11, 0.26, up ? 880.0 : 587.3, 0.30);
        return ToWav(buf);
    }

    private static byte[] ToWav(double[] samples)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + dataLen); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(Rate); bw.Write(Rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(dataLen);
        foreach (var s in samples) bw.Write((short)(Math.Clamp(s, -1, 1) * 30000));
        bw.Flush();
        return ms.ToArray();
    }
}
