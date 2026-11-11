using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MoonlightLauncher;

// ====================================================================================
//  Rede dos amigos: conexão com o servidor relay (presença, chat) e túnel dos mundos.
// ====================================================================================

public sealed class FriendsClient : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private ClientWebSocket? _ws;
    private string _url = "";

    /// <summary>Monta a mensagem "hello" (nome, conta, rosto, status) com os dados mais recentes.</summary>
    public Func<object>? HelloFactory { get; set; }

    public bool Connected { get; private set; }

    /// <summary>True enquanto a conexão está ligada ou tentando (depois de Start e antes de Stop).</summary>
    public bool Running => _cts != null;
    public string LastError { get; private set; } = "";

    /// <summary>Chamado (fora da thread da tela) quando conecta ou desconecta.</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>Chamado (fora da thread da tela) quando uma tentativa de conectar falha, com o motivo em português.</summary>
    public event Action<string>? ConnectFailed;

    private static string Describe(Exception ex)
    {
        if (ex is OperationCanceledException) return "o servidor demorou demais para responder (o plano gratuito do Render pode estar acordando).";
        var m = ex.InnerException?.Message ?? ex.Message;
        if (m.Contains("404")) return "o endereço responde, mas não é o servidor dos amigos (erro 404). Confira se o serviço no Render é o do site com server.js.";
        if (m.Contains("502") || m.Contains("503")) return "o servidor está fora do ar ou ainda ligando (erro " + (m.Contains("502") ? "502" : "503") + ").";
        if (m.Contains("No such host") || m.Contains("host desconhecido") || m.Contains("nome") && m.Contains("host"))
            return "o endereço do servidor não existe. Confira o endereço do seu site no Render.";
        return m;
    }

    /// <summary>Chamado (fora da thread da tela) para cada mensagem do servidor.</summary>
    public event Action<JsonElement>? Received;

    public void Start(string relayUrl)
    {
        Stop();
        _url = FriendsConfig.Normalize(relayUrl);
        if (_url.Length == 0) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var url = _url; // captura a URL desta sessão; não depende de um campo mutável
        _ = Task.Run(() => RunAsync(url, ct));
    }

    public void Stop()
    {
        var old = _cts;
        _cts = null;
        try { old?.Cancel(); } catch { /* */ }
        try { _ws?.Abort(); } catch { /* */ }
        try { old?.Dispose(); } catch { /* */ }
        SetConnected(false);
    }

    private void SetConnected(bool v)
    {
        if (Connected == v) return;
        Connected = v;
        try { ConnectionChanged?.Invoke(v); } catch (Exception ex) { LauncherLog.Write("ConnectionChanged", ex); }
    }

    private async Task RunAsync(string url, CancellationToken ct)
    {
        var delay = 2;
        while (!ct.IsCancellationRequested)
        {
            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _ws = ws;
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(60)); // o Render gratuito demora para "acordar"
                await ws.ConnectAsync(new Uri(url + "/ws"), connectCts.Token);

                LastError = "";
                await SendAsync(ws, HelloFactory?.Invoke() ?? new { t = "hello" }, ct);
                SetConnected(true);
                delay = 2;
                await ReceiveLoopAsync(ws, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                LastError = Describe(ex);
                LauncherLog.Write("Amigos: conexão com o servidor falhou", ex);
                try { ConnectFailed?.Invoke(LastError); } catch { /* */ }
            }
            finally
            {
                SetConnected(false);
                try { ws.Dispose(); } catch { /* */ }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { return; }
            delay = Math.Min(delay * 2, 30);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 4 * 1024 * 1024) return;
            } while (!r.EndOfMessage);

            if (r.MessageType != WebSocketMessageType.Text) continue;
            try
            {
                using var doc = JsonDocument.Parse(ms.ToArray());
                Received?.Invoke(doc.RootElement.Clone());
            }
            catch (JsonException) { /* mensagem estranha: ignora */ }
            catch (Exception ex) { LauncherLog.Write("Amigos: erro ao tratar mensagem", ex); }
        }
    }

    private async Task SendAsync(ClientWebSocket ws, object msg, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
        await _sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>Envia sem bloquear a interface. O retorno indica se havia uma sessão conectada.</summary>
    public bool Send(object msg)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open || !Connected) return false;
        _ = SendMessageAsync(msg);
        return true;
    }

    /// <summary>Envia e aguarda a escrita real no WebSocket. Use para mensagens que não podem ser salvas localmente antes do envio.</summary>
    public async Task<bool> SendMessageAsync(object msg)
    {
        var ws = _ws;
        var cts = _cts;
        if (ws == null || cts == null || ws.State != WebSocketState.Open || !Connected) return false;

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
            await _sendLock.WaitAsync(cts.Token);
            try
            {
                // A conexão pode ter sido substituída enquanto aguardávamos o lock.
                if (!ReferenceEquals(ws, _ws) || ws.State != WebSocketState.Open || !Connected) return false;
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
                return true;
            }
            finally { _sendLock.Release(); }
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            LauncherLog.Write("Amigos: falha ao enviar mensagem", ex);
            return false;
        }
    }

    public void Dispose() => Stop();
}

// ------------------------------------------------------------------------------------
//  Túnel: leva os bytes do Minecraft de um PC para o outro, passando pelo relay.
// ------------------------------------------------------------------------------------
internal static class TunnelPump
{
    // Liga um socket TCP local a uma conexão WebSocket do relay, nos dois sentidos.
    public static async Task RunAsync(TcpClient tcp, ClientWebSocket ws, CancellationToken ct)
    {
        tcp.NoDelay = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var net = tcp.GetStream();

        var up = Task.Run(async () =>
        {
            var buf = new byte[16 * 1024];
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    var n = await net.ReadAsync(buf.AsMemory(), linked.Token);
                    if (n <= 0) break;
                    await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), WebSocketMessageType.Binary, true, linked.Token);
                }
            }
            catch { /* conexão fechou */ }
            linked.Cancel();
        });

        var down = Task.Run(async () =>
        {
            var buf = new byte[16 * 1024];
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), linked.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (r.Count > 0) await net.WriteAsync(buf.AsMemory(0, r.Count), linked.Token);
                }
            }
            catch { /* conexão fechou */ }
            linked.Cancel();
        });

        await Task.WhenAll(up, down);
        try { tcp.Close(); } catch { /* */ }
        try { ws.Abort(); } catch { /* */ }
    }

    // Espera o servidor dizer {"t":"ready"}. Devolve false se der erro ou demorar.
    public static async Task<bool> WaitReadyAsync(ClientWebSocket ws, CancellationToken ct)
    {
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromSeconds(25));
        var buf = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), to.Token);
                if (r.MessageType == WebSocketMessageType.Close) return false;
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                if (text.Contains("\"ready\"")) return true;
                if (text.Contains("\"error\"")) return false;
            }
        }
        catch { /* tempo esgotado ou fechou */ }
        return false;
    }
}

/// <summary>Quem entra: abre uma porta local (127.0.0.1) e leva cada conexão do Minecraft até o mundo do amigo.</summary>
public sealed class GuestTunnel : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _relay, _myId, _myToken, _friendId;

    public int Port { get; }

    private GuestTunnel(TcpListener l, int port, string relay, string myId, string myToken, string friendId)
    {
        _listener = l; Port = port; _relay = relay; _myId = myId; _myToken = myToken; _friendId = friendId;
    }

    /// <summary>Tenta a porta preferida e, se estiver ocupada, procura outra livre a partir de 25600.</summary>
    public static GuestTunnel? Start(string relay, string myId, string myToken, string friendId,
        int preferredPort, ISet<int> usedPorts)
    {
        var candidates = new List<int>();
        if (preferredPort > 1024 && !usedPorts.Contains(preferredPort)) candidates.Add(preferredPort);
        for (var p = 25600; p < 25800; p++) if (!usedPorts.Contains(p) && p != preferredPort) candidates.Add(p);

        foreach (var port in candidates)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                var t = new GuestTunnel(l, port, relay, myId, myToken, friendId);
                _ = Task.Run(t.AcceptLoopAsync);
                return t;
            }
            catch (SocketException) { /* porta ocupada: tenta a próxima */ }
        }
        return null;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(tcp));
        }
    }

    private async Task HandleAsync(TcpClient tcp)
    {
        var ws = new ClientWebSocket();
        try
        {
            using var to = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            to.CancelAfter(TimeSpan.FromSeconds(30));
            await ws.ConnectAsync(new Uri(_relay + "/tunnel"), to.Token);

            var open = JsonSerializer.SerializeToUtf8Bytes(new { t = "open", id = _myId, token = _myToken, to = _friendId });
            await ws.SendAsync(new ArraySegment<byte>(open), WebSocketMessageType.Text, true, to.Token);

            if (!await TunnelPump.WaitReadyAsync(ws, _cts.Token))
            {
                LauncherLog.Write($"Túnel para {_friendId}: o amigo não respondeu");
                tcp.Close();
                return;
            }
            await TunnelPump.RunAsync(tcp, ws, _cts.Token);
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Túnel para {_friendId} falhou", ex);
            try { tcp.Close(); } catch { /* */ }
        }
        finally { try { ws.Dispose(); } catch { /* */ } }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* */ }
        try { _listener.Stop(); } catch { /* */ }
    }
}

/// <summary>Quem hospeda: quando um amigo entra, liga o relay ao mundo aberto na porta local.</summary>
public static class HostTunnel
{
    public static async Task AcceptAsync(string relay, string myId, string myToken, string tid, int localPort,
        CancellationToken ct)
    {
        var tcp = new TcpClient();
        var ws = new ClientWebSocket();
        try
        {
            await tcp.ConnectAsync(IPAddress.Loopback, localPort, ct);

            using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
            to.CancelAfter(TimeSpan.FromSeconds(30));
            await ws.ConnectAsync(new Uri(relay + "/tunnel"), to.Token);

            var accept = JsonSerializer.SerializeToUtf8Bytes(new { t = "accept", id = myId, token = myToken, tid });
            await ws.SendAsync(new ArraySegment<byte>(accept), WebSocketMessageType.Text, true, to.Token);

            if (!await TunnelPump.WaitReadyAsync(ws, ct)) { tcp.Close(); return; }
            await TunnelPump.RunAsync(tcp, ws, ct);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Túnel (anfitrião) falhou", ex);
            try { tcp.Close(); } catch { /* */ }
        }
        finally { try { ws.Dispose(); } catch { /* */ } }
    }
}
