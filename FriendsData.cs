using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// ====================================================================================
//  Amigos — dados guardados no PC (friends.json) e ferramentas de identidade/rosto.
// ====================================================================================

public static class FriendsConfig
{
    /// <summary>
    /// Endereço do servidor dos amigos = o próprio site do launcher (o relay roda dentro dele, em /ws e /tunnel).
    /// Já vem fixo: ninguém precisa configurar nada. Se o endereço do seu site for outro, troque aqui e gere o .exe de novo.
    /// </summary>
    public const string DefaultRelay = "wss://moonlight-amigos.onrender.com";

    /// <summary>
    /// Endereço usado de fato: o do arquivo %AppData%\MoonlightLauncher\relay.txt (se existir) ou o DefaultRelay.
    /// Serve para trocar o servidor sem gerar o .exe de novo.
    /// </summary>
    public static string Effective
    {
        get
        {
            try
            {
                var f = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "relay.txt");
                if (File.Exists(f))
                {
                    var t = File.ReadAllText(f).Trim();
                    if (t.Length > 0) return t;
                }
            }
            catch { /* usa o padrão */ }
            return DefaultRelay;
        }
    }

    public static string Normalize(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        if (u.Length == 0) return "";
        if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "wss://" + u[8..];
        else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = "ws://" + u[7..];
        else if (!u.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                 !u.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) u = "wss://" + u;
        return u;
    }
}

public class FriendEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Acc { get; set; } = "Offline";   // Microsoft | Offline
    public string Face { get; set; } = "";         // PNG 8×8 em base64
}

public class ChatLine
{
    public bool Mine { get; set; }
    public string Text { get; set; } = "";
    public long Ts { get; set; }                   // milissegundos (UTC) desde 1970
}

public class FriendsStore
{
    public string Token { get; set; } = "";
    public string RelayUrl { get; set; } = "";
    public bool Dnd { get; set; }                  // Não perturbe: sem janelinha e sem som
    public bool SoundOn { get; set; } = true;
    public List<FriendEntry> Friends { get; set; } = new();
    public List<FriendEntry> Incoming { get; set; } = new();   // pedidos recebidos
    public List<string> Outgoing { get; set; } = new();        // pedidos enviados (códigos)
    public Dictionary<string, List<ChatLine>> Chats { get; set; } = new();
    public Dictionary<string, int> Unread { get; set; } = new();
    public Dictionary<string, int> Ports { get; set; } = new(); // porta local de cada amigo (fica estável)

    [JsonIgnore] public string Id => FriendsCrypto.IdFromToken(Token);

    private static readonly string FilePath = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "friends.json");
    private static readonly object Gate = new();

    public static FriendsStore Load()
    {
        FriendsStore? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<FriendsStore>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) { LauncherLog.Write("friends.json ilegível, começando do zero", ex); }

        s ??= new FriendsStore();

        // JSON antigo/incompleto não pode derrubar toda a tela de amigos.
        s.Token ??= "";
        s.RelayUrl ??= "";
        s.Friends ??= new List<FriendEntry>();
        s.Incoming ??= new List<FriendEntry>();
        s.Outgoing ??= new List<string>();
        s.Chats ??= new Dictionary<string, List<ChatLine>>();
        s.Unread ??= new Dictionary<string, int>();
        s.Ports ??= new Dictionary<string, int>();
        s.Friends = s.Friends.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        s.Incoming = s.Incoming.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        s.Outgoing = s.Outgoing.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in s.Chats.Keys.ToList())
        {
            var lines = s.Chats[key] ?? new List<ChatLine>();
            s.Chats[key] = lines.Where(line => line != null && line.Ts >= 0)
                .TakeLast(300).ToList();
            foreach (var line in s.Chats[key]) line.Text ??= "";
        }
        foreach (var key in s.Unread.Keys.ToList())
            s.Unread[key] = Math.Clamp(s.Unread[key], 0, 9999);

        if (s.Token.Length < 32)
        {
            s.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            s.Save();
        }
        return s;
    }

    public void Save()
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this));
                File.Move(tmp, FilePath, true);
            }
        }
        catch (Exception ex) { LauncherLog.Write("Não consegui salvar friends.json", ex); }
    }

    public List<ChatLine> ChatWith(string id)
    {
        if (!Chats.TryGetValue(id, out var l)) Chats[id] = l = new List<ChatLine>();
        return l;
    }

    public void AddLine(string id, ChatLine line)
    {
        var l = ChatWith(id);
        l.Add(line);
        if (l.Count > 300) l.RemoveRange(0, l.Count - 300);
    }
}

public static class FriendsCrypto
{
    /// <summary>Código do jogador = 10 primeiros caracteres hex do SHA-256 do token (igual ao do servidor).</summary>
    public static string IdFromToken(string token)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(h)[..10];
    }

    /// <summary>MOON-A3F09-C12BE</summary>
    public static string Pretty(string id) => id.Length == 10 ? $"MOON-{id[..5]}-{id[5..]}" : id;

    /// <summary>Aceita "MOON-A3F09-C12BE", "a3f09c12be" etc. Devolve null se não parece um código.</summary>
    public static string? ParseCode(string? input)
    {
        var s = new string((input ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (s.StartsWith("MOON")) s = s[4..];
        return s.Length == 10 && s.All(Uri.IsHexDigit) ? s : null;
    }
}

// ----------------------------------------------------------------------------------
//  Rosto da skin (cabeça 8×8 + camada do chapéu) — funciona com qualquer conta.
// ----------------------------------------------------------------------------------
public static class FaceTools
{
    private static BitmapSource? _default;

    /// <summary>Gera o rosto 8×8 em PNG/base64 a partir do PNG da skin (ou do boneco lunar se não houver skin).</summary>
    public static string ToFaceB64(byte[]? skinPng)
    {
        byte[] px;
        try { px = skinPng != null ? SkinTools.ToPixels(skinPng) : SkinTools.PlaceholderPixels(); }
        catch
        {
            try { px = SkinTools.PlaceholderPixels(); }
            catch { return ""; }
        }

        var face = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var b = ((8 + y) * 64 + 8 + x) * 4;   // base da cabeça
                var h = ((8 + y) * 64 + 40 + x) * 4;  // chapéu / segunda camada
                var useHat = px[h + 3] > 127;
                var s = useHat ? h : b;
                var d = (y * 8 + x) * 4;
                face[d] = px[s]; face[d + 1] = px[s + 1]; face[d + 2] = px[s + 2]; face[d + 3] = 255;
            }

        var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, face, 8 * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Converte o rosto base64 em uma imagem 64×64 nítida (sem suavizar os pixels).</summary>
    public static BitmapSource FromB64(string? b64)
    {
        try
        {
            if (!string.IsNullOrEmpty(b64))
            {
                var raw = Convert.FromBase64String(b64);
                using var ms = new MemoryStream(raw);
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                if (conv.PixelWidth == 8 && conv.PixelHeight == 8)
                {
                    var src = new byte[8 * 8 * 4];
                    conv.CopyPixels(src, 8 * 4, 0);
                    return Upscale(src);
                }
            }
        }
        catch { /* cai no rosto padrão */ }

        return _default ??= Upscale(DefaultPixels());
    }

    private static BitmapSource Upscale(byte[] src)
    {
        const int k = 8, n = 8 * k;
        var dst = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var s = ((y / k) * 8 + x / k) * 4;
                var d = (y * n + x) * 4;
                dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
            }

        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, dst, n * 4);
        bmp.Freeze();
        return bmp;
    }

    // Rosto lilás com olhinhos, usado quando a pessoa não tem skin.
    private static byte[] DefaultPixels()
    {
        var p = new byte[8 * 8 * 4];
        for (var i = 0; i < 64; i++)
        {
            p[i * 4] = 0xE0; p[i * 4 + 1] = 0x7E; p[i * 4 + 2] = 0x9B; p[i * 4 + 3] = 255; // BGRA: lilás
        }
        foreach (var (x, y) in new[] { (2, 3), (5, 3), (2, 4), (5, 4) })
        {
            var d = (y * 8 + x) * 4;
            p[d] = 0xFF; p[d + 1] = 0xFF; p[d + 2] = 0xFF;
        }
        return p;
    }
}

// ----------------------------------------------------------------------------------
//  Objetos de tela (ViewModels) das listas de amigos e do chat.
// ----------------------------------------------------------------------------------
public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record WorldInfo(string Name, string Mc, string Loader, string VersionId, int ModCount);

public sealed class FriendVm : Notifier
{
    private string _name = "", _acc = "Offline", _status = "offline", _faceB64 = "";
    private BitmapSource _face = FaceTools.FromB64(null);
    private bool _online;
    private WorldInfo? _world;
    private int _unread;

    public string Id { get; init; } = "";

    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(SubText)); } }

    public string Acc
    {
        get => _acc;
        set { if (Set(ref _acc, value)) { Raise(nameof(AccLabel)); Raise(nameof(SubText)); } }
    }

    public string FaceB64
    {
        get => _faceB64;
        set { if (Set(ref _faceB64, value)) Face = FaceTools.FromB64(value); }
    }

    public BitmapSource Face { get => _face; private set => Set(ref _face, value); }

    public bool Online
    {
        get => _online;
        set { if (Set(ref _online, value)) { Raise(nameof(StatusKind)); Raise(nameof(SubText)); Raise(nameof(HasWorld)); Raise(nameof(Preview)); } }
    }

    /// <summary>online | dnd | offline (para as bolinhas coloridas)</summary>
    public string Status
    {
        get => _status;
        set { if (Set(ref _status, value)) { Raise(nameof(StatusKind)); Raise(nameof(SubText)); Raise(nameof(Preview)); } }
    }

    public string StatusKind => !_online ? "offline" : _status == "dnd" ? "dnd" : "online";

    public WorldInfo? World
    {
        get => _world;
        set
        {
            if (!Set(ref _world, value)) return;
            Raise(nameof(HasWorld)); Raise(nameof(WorldText)); Raise(nameof(SubText)); Raise(nameof(WorldSub)); Raise(nameof(Preview));
        }
    }

    public bool HasWorld => _world != null && _online;
    public string WorldText => _world == null ? "" : $"🌍  {_world.Name}";
    public string WorldSub => _world == null ? "" :
        $"{_world.Mc}  ·  {_world.Loader}" + (_world.ModCount > 0 ? $"  ·  🧩 {_world.ModCount} mods" : "  ·  sem mods");

    public int Unread
    {
        get => _unread;
        set { if (Set(ref _unread, value)) { Raise(nameof(HasUnread)); Raise(nameof(UnreadText)); } }
    }

    public bool HasUnread => _unread > 0;
    public string UnreadText => _unread > 99 ? "99+" : _unread.ToString();
    public string AccLabel => _acc == "Microsoft" ? "MICROSOFT" : "OFFLINE";

    // Última mensagem (prévia na lista de conversas, como num app de mensagens)
    private string _lastText = "", _lastTime = "";

    public long LastTs { get; set; }

    public string LastText
    {
        get => _lastText;
        set { if (Set(ref _lastText, value)) Raise(nameof(Preview)); }
    }

    public string LastTime { get => _lastTime; set => Set(ref _lastTime, value); }

    /// <summary>Linha de baixo do nome na lista: mundo aberto > última mensagem > online/offline.</summary>
    public string Preview => HasWorld ? SubText : _lastText.Length > 0 ? _lastText : SubText;

    public string SubText => HasWorld ? $"🌍 Mundo aberto · {_world!.Mc}"
        : !_online ? "Offline"
        : _status == "dnd" ? "🌙 Não perturbe"
        : "Online";
}

public sealed class ChatVm
{
    public bool Mine { get; init; }
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
}

public sealed class FriendResultVm : Notifier
{
    private string _action = "➕  Adicionar";
    private bool _canAdd = true;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Acc { get; init; } = "Offline";
    public bool Online { get; init; }
    public BitmapSource Face { get; init; } = FaceTools.FromB64(null);
    public string AccLabel => Acc == "Microsoft" ? "MICROSOFT" : "OFFLINE";
    public string Code => FriendsCrypto.Pretty(Id);
    public string Action { get => _action; set => Set(ref _action, value); }
    public bool CanAdd { get => _canAdd; set => Set(ref _canAdd, value); }
}

public sealed class RequestVm
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string AccLabel { get; init; } = "";
    public BitmapSource Face { get; init; } = FaceTools.FromB64(null);
}
