using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MoonlightLauncher;

/// <summary>
/// Quando a pessoa usa "Abrir para LAN", o Minecraft passa a mandar a cada ~1,5 s um aviso UDP para 224.0.2.60:4445
/// no formato <c>[MOTD]nome[/MOTD][AD]porta[/AD]</c>. Escutar esse aviso descobre a porta do mundo em qualquer versão e
/// em qualquer idioma, sem depender do texto que o jogo escreve no log. Só vale o aviso que sai deste PC e cuja porta
/// está de fato aberta aqui (um amigo na mesma rede não vira "meu mundo").
/// </summary>
internal sealed class LanBeacon : IDisposable
{
    private static readonly Regex AdRx = new(@"\[AD\](\d{4,5})\[/AD\]", RegexOptions.Compiled);
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");

    private readonly CancellationTokenSource _cts = new();
    private readonly Action<int> _onPort;
    private UdpClient? _udp;

    private LanBeacon(Action<int> onPort) => _onPort = onPort;

    public static LanBeacon Start(Action<int> onPort)
    {
        var b = new LanBeacon(onPort);
        _ = Task.Run(b.RunAsync);
        return b;
    }

    private async Task RunAsync()
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
            _udp = udp;

            // Entra no grupo em cada placa de rede ativa (o Minecraft manda pela placa padrão do PC).
            var joined = 0;
            foreach (var ip in LocalIPv4())
            {
                try { udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, ip)); joined++; }
                catch { /* placa sem multicast: segue */ }
            }
            if (joined == 0)
            {
                try { udp.JoinMulticastGroup(Group); }
                catch (Exception ex) { LauncherLog.Write("LAN: não consegui entrar no grupo de aviso do Minecraft", ex); }
            }

            var local = LocalIPv4().ToHashSet();
            while (!_cts.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(_cts.Token);
                var text = Encoding.UTF8.GetString(r.Buffer);
                var m = AdRx.Match(text);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var port)) continue;

                var from = r.RemoteEndPoint.Address;
                if (!IPAddress.IsLoopback(from) && !local.Contains(from))
                {
                    local = LocalIPv4().ToHashSet(); // a rede pode ter mudado
                    if (!local.Contains(from)) continue; // veio de outro PC da rede
                }
                if (!IsListeningHere(port)) continue;

                try { _onPort(port); } catch (Exception ex) { LauncherLog.Write("LAN: erro ao tratar o aviso do Minecraft", ex); }
            }
        }
        catch (OperationCanceledException) { /* parou */ }
        catch (ObjectDisposedException) { /* parou */ }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested) LauncherLog.Write("LAN: escuta do aviso do Minecraft falhou (o log do jogo continua valendo)", ex);
        }
    }

    private static IEnumerable<IPAddress> LocalIPv4()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address);
            }
        }
        catch { /* sem informação de rede */ }
        return list;
    }

    private static bool IsListeningHere(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port); }
        catch { return true; } // não deu para conferir: confia no aviso
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* */ }
        try { _udp?.Close(); } catch { /* */ }
    }
}
