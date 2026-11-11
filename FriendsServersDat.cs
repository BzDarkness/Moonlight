using System.Buffers.Binary;
using System.IO;
using System.Text;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

// ====================================================================================
//  Escreve os mundos dos amigos no servers.dat (a lista do menu Multijogador do Minecraft).
//  O arquivo é NBT sem compressão. Os servidores que a pessoa já tinha são preservados;
//  só mexemos nas linhas que começam com a marca do Moonlight.
// ====================================================================================
internal sealed class NbtNode
{
    public byte Type;
    public object? Value;
    public NbtNode(byte type, object? value) { Type = type; Value = value; }
}

internal sealed class NbtCompound : List<KeyValuePair<string, NbtNode>>
{
    public NbtNode? Get(string name) => this.FirstOrDefault(p => p.Key == name).Value;
    public void Put(string name, NbtNode n)
    {
        var i = FindIndex(p => p.Key == name);
        if (i >= 0) this[i] = new(name, n); else Add(new(name, n));
    }
}

internal sealed class NbtList
{
    public byte ElemType = 10;
    public List<NbtNode> Items = new();
}

internal static class Nbt
{
    // ---- leitura ----
    public static NbtCompound ReadRoot(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        var type = br.ReadByte();
        if (type != 10) throw new InvalidDataException("NBT sem compound na raiz");
        ReadString(br); // nome da raiz
        return (NbtCompound)ReadPayload(br, 10);
    }

    private static string ReadString(BinaryReader br)
    {
        var len = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
        return Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    private static int ReadInt(BinaryReader br) => BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));

    private static object ReadPayload(BinaryReader br, byte type)
    {
        switch (type)
        {
            case 1: return br.ReadSByte();
            case 2: return BinaryPrimitives.ReadInt16BigEndian(br.ReadBytes(2));
            case 3: return ReadInt(br);
            case 4: return BinaryPrimitives.ReadInt64BigEndian(br.ReadBytes(8));
            case 5: return BinaryPrimitives.ReadSingleBigEndian(br.ReadBytes(4));
            case 6: return BinaryPrimitives.ReadDoubleBigEndian(br.ReadBytes(8));
            case 7: return br.ReadBytes(ReadInt(br));
            case 8: return ReadString(br);
            case 9:
            {
                var l = new NbtList { ElemType = br.ReadByte() };
                var n = ReadInt(br);
                for (var i = 0; i < n; i++) l.Items.Add(new NbtNode(l.ElemType, ReadPayload(br, l.ElemType)));
                return l;
            }
            case 10:
            {
                var c = new NbtCompound();
                while (true)
                {
                    var t = br.ReadByte();
                    if (t == 0) break;
                    var name = ReadString(br);
                    c.Add(new(name, new NbtNode(t, ReadPayload(br, t))));
                }
                return c;
            }
            case 11:
            {
                var n = ReadInt(br);
                var a = new int[n];
                for (var i = 0; i < n; i++) a[i] = ReadInt(br);
                return a;
            }
            case 12:
            {
                var n = ReadInt(br);
                var a = new long[n];
                for (var i = 0; i < n; i++) a[i] = BinaryPrimitives.ReadInt64BigEndian(br.ReadBytes(8));
                return a;
            }
            default: throw new InvalidDataException("Tag NBT desconhecida: " + type);
        }
    }

    // ---- escrita ----
    public static byte[] WriteRoot(NbtCompound root)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(10);
        WriteString(ms, "");
        WritePayload(ms, 10, root);
        return ms.ToArray();
    }

    private static void WriteString(Stream s, string v)
    {
        var b = Encoding.UTF8.GetBytes(v);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)b.Length);
        s.Write(len);
        s.Write(b);
    }

    private static void WriteInt(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }

    private static void WritePayload(Stream s, byte type, object? v)
    {
        Span<byte> b8 = stackalloc byte[8];
        switch (type)
        {
            case 1: s.WriteByte((byte)(sbyte)v!); break;
            case 2: BinaryPrimitives.WriteInt16BigEndian(b8, (short)v!); s.Write(b8[..2]); break;
            case 3: WriteInt(s, (int)v!); break;
            case 4: BinaryPrimitives.WriteInt64BigEndian(b8, (long)v!); s.Write(b8); break;
            case 5: BinaryPrimitives.WriteSingleBigEndian(b8, (float)v!); s.Write(b8[..4]); break;
            case 6: BinaryPrimitives.WriteDoubleBigEndian(b8, (double)v!); s.Write(b8); break;
            case 7: { var a = (byte[])v!; WriteInt(s, a.Length); s.Write(a); break; }
            case 8: WriteString(s, (string)v!); break;
            case 9:
            {
                var l = (NbtList)v!;
                s.WriteByte(l.Items.Count == 0 ? (byte)0 : l.ElemType);
                WriteInt(s, l.Items.Count);
                foreach (var it in l.Items) WritePayload(s, l.ElemType, it.Value);
                break;
            }
            case 10:
            {
                foreach (var kv in (NbtCompound)v!)
                {
                    s.WriteByte(kv.Value.Type);
                    WriteString(s, kv.Key);
                    WritePayload(s, kv.Value.Type, kv.Value.Value);
                }
                s.WriteByte(0);
                break;
            }
            case 11: { var a = (int[])v!; WriteInt(s, a.Length); foreach (var x in a) WriteInt(s, x); break; }
            case 12:
            {
                var a = (long[])v!;
                WriteInt(s, a.Length);
                foreach (var x in a) { BinaryPrimitives.WriteInt64BigEndian(b8, x); s.Write(b8); }
                break;
            }
        }
    }
}

public static class FriendsServersDat
{
    /// <summary>Marca no começo do nome: é assim que reconhecemos (e atualizamos) só as entradas do Moonlight.</summary>
    public const string Marker = "§b[Moonlight] ";

    /// <summary>
    /// Troca as entradas do Moonlight no servers.dat por esta lista (nome, endereço). Lista vazia = só remove.
    /// Os outros servidores da pessoa continuam como estavam.
    /// </summary>
    public static void Sync(string mcRoot, IReadOnlyList<(string Name, string Address)> worlds)
    {
        var file = IOPath.Combine(mcRoot, "servers.dat");
        NbtCompound root;
        try
        {
            root = File.Exists(file) ? Nbt.ReadRoot(File.ReadAllBytes(file)) : new NbtCompound();
        }
        catch (Exception ex)
        {
            LauncherLog.Write("servers.dat ilegível: não vou mexer nele", ex);
            return;
        }

        var list = root.Get("servers")?.Value as NbtList;
        if (list == null)
        {
            list = new NbtList { ElemType = 10 };
            root.Put("servers", new NbtNode(9, list));
        }

        // O Minecraft grava uma lista vazia com tipo de elemento 0 (TAG_End). Se mantivéssemos esse 0 ao inserir
        // os mundos, o arquivo sairia corrompido. Servidores são sempre compounds (tipo 10).
        list.ElemType = 10;

        var removed = list.Items.RemoveAll(n =>
            n.Value is NbtCompound c && c.Get("name")?.Value is string name &&
            name.StartsWith(Marker, StringComparison.Ordinal));
        if (removed == 0 && worlds.Count == 0) return; // nada mudou: não mexe no arquivo da pessoa

        var mine = new List<NbtNode>();
        foreach (var (name, addr) in worlds)
        {
            var c = new NbtCompound();
            c.Add(new("ip", new NbtNode(8, addr)));
            c.Add(new("name", new NbtNode(8, Marker + name)));
            mine.Add(new NbtNode(10, c));
        }
        list.Items.InsertRange(0, mine);

        Directory.CreateDirectory(mcRoot);
        var tmp = file + ".moonlight";
        File.WriteAllBytes(tmp, Nbt.WriteRoot(root));
        File.Move(tmp, file, true);
    }
}
