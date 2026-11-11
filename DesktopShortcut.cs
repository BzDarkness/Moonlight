using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>
/// Atalho "Moonlight Launcher" na área de trabalho. Usa só o próprio Windows (IShellLink):
/// nada extra é instalado. O ícone do atalho acompanha o ícone do aplicativo escolhido (Golden Moon).
/// </summary>
internal static class DesktopShortcut
{
    private const string LinkName = "Moonlight Launcher.lnk";

    public static string LinkPath => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName);

    public static bool Exists => File.Exists(LinkPath);

    /// <summary>Caminho do .exe em execução, ou null se não faz sentido apontar o atalho para ele (pasta temporária, dotnet.exe...).</summary>
    public static string? StableExePath()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (IOPath.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;

        // Rodando de dentro de um .zip aberto no Explorer / pasta temporária: o atalho quebraria depois.
        var temp = IOPath.GetFullPath(IOPath.GetTempPath()).TrimEnd('\\') + "\\";
        if (IOPath.GetFullPath(exe).StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return null;
        return exe;
    }

    /// <summary>Cria (ou refaz) o atalho. iconKey/iconRel vazios = ícone original do .exe.</summary>
    public static void Write(string exe, string iconKey, string iconRel)
    {
        string iconPath = exe;
        var iconIndex = 0;

        if (iconRel.Length > 0)
        {
            var ico = BuildIco(iconKey, iconRel);
            if (ico != null) { iconPath = ico; iconIndex = 0; }
        }

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exe);
            link.SetWorkingDirectory(IOPath.GetDirectoryName(exe) ?? "");
            link.SetDescription("Moonlight Launcher");
            link.SetIconLocation(iconPath, iconIndex);
            ((IPersistFile)link).Save(LinkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }

        // Pede ao Explorer para redesenhar os ícones (senão o antigo pode ficar na tela).
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { /* só cosmético */ }
    }

    public static void Delete()
    {
        try { if (File.Exists(LinkPath)) File.Delete(LinkPath); }
        catch (Exception ex) { LauncherLog.Write("Não consegui apagar o atalho da área de trabalho", ex); }
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    // ---------- PNG (Assets/icons) -> .ico ----------

    // Um .ico por ícone, com nome próprio: trocar de ícone nunca reaproveita a imagem velha do cache do Windows.
    private static string? BuildIco(string key, string rel)
    {
        try
        {
            var dir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MoonlightLauncher", "icons");
            Directory.CreateDirectory(dir);
            var file = IOPath.Combine(dir, "atalho_" + (key.Length > 0 ? key : "original") + ".ico");
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;

            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + rel, UriKind.Absolute));
            if (info == null) return null;

            BitmapSource src;
            using (var s = info.Stream)
            {
                var dec = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                src = dec.Frames[0];
            }

            // Cabe em 256x256 (centralizado, sem esticar).
            const int size = 256;
            var scale = Math.Min((double)size / src.PixelWidth, (double)size / src.PixelHeight);
            var w = src.PixelWidth * scale;
            var h = src.PixelHeight * scale;

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
                dc.DrawImage(src, new Rect((size - w) / 2, (size - h) / 2, w, h));

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            byte[] png;
            using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }

            // Arquivo .ico com uma imagem PNG dentro (o Windows aceita desde o Vista).
            using var fs = File.Create(file);
            using var bw = new BinaryWriter(fs);
            bw.Write((short)0);      // reservado
            bw.Write((short)1);      // tipo: ícone
            bw.Write((short)1);      // 1 imagem
            bw.Write((byte)0);       // largura 256
            bw.Write((byte)0);       // altura 256
            bw.Write((byte)0);       // sem paleta
            bw.Write((byte)0);
            bw.Write((short)1);      // planos
            bw.Write((short)32);     // bits por pixel
            bw.Write(png.Length);
            bw.Write(22);            // onde a imagem começa
            bw.Write(png);
            return file;
        }
        catch (Exception ex)
        {
            LauncherLog.Write($"Não consegui montar o ícone do atalho ({rel})", ex);
            return null;
        }
    }

    // ---------- COM do Windows ----------

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
