using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MoonlightLauncher;

// ====================================================================================
//  Compartilhamento de tela: qualidade (resolução + quadros por segundo) e o que compartilhar
//  (uma janela ou uma tela inteira). O envio dos quadros fica em ScreenShareSender (Calls.cs);
//  as janelinhas de escolha ficam em ShareDialogs.cs.
// ====================================================================================

/// <summary>Qualidade escolhida para a transmissão. 1080p, 4K e 60 fps só com o Golden Moon.</summary>
public sealed class ShareOptions
{
    /// <summary>Altura máxima do vídeo: 480, 720, 1080 ou 2160 (4K).</summary>
    public int Height { get; init; } = 720;

    /// <summary>Quadros por segundo desejados: 15, 30 ou 60 (o real depende do PC e da internet).</summary>
    public int Fps { get; init; } = 30;

    /// <summary>Levar o som do PC junto.</summary>
    public bool Audio { get; init; } = true;

    public int MaxWidth => Height * 16 / 9;
    public bool NeedsPremium => Height > 720 || Fps > 30;
    public string ResLabel => Height >= 2160 ? "4K" : Height + "p";
    public string Label => ResLabel + " · " + Fps + " fps";

    /// <summary>Quanto maior a imagem, mais comprimido o JPEG, para o quadro não ficar pesado demais para a internet.</summary>
    public int JpegQuality => Height switch { >= 2160 => 45, >= 1080 => 52, >= 720 => 60, _ => 68 };

    /// <summary>Sem o Golden Moon, o máximo é 720p e 30 fps.</summary>
    public ShareOptions Clamp(bool premium) => premium
        ? this
        : new ShareOptions { Height = Math.Min(Height, 720), Fps = Math.Min(Fps, 30), Audio = Audio };
}

/// <summary>O que vai ser compartilhado: uma janela (Hwnd) ou uma tela inteira (retângulo do monitor).</summary>
public sealed class ShareSource
{
    public bool IsScreen { get; init; }
    public IntPtr Hwnd { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public string Title { get; init; } = "";
    public ImageSource? Icon { get; set; }

    /// <summary>Janelas de jogo/aceleradas por placa de vídeo saem pretas no PrintWindow; aí copiamos direto da tela.</summary>
    public bool ScreenBlt { get; set; }
}

internal enum GrabResult { Ok, Failed, Gone }

// ------------------------------------------------------------------------------------
//  Lista janelas e telas, e tira "fotos" delas (usado nas miniaturas e na transmissão).
// ------------------------------------------------------------------------------------
internal static class ShareCapture
{
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, PW_RENDERFULLCONTENT = 2;

    // ---- listas ----

    /// <summary>Janelas abertas que aparecem na barra de tarefas (sem minimizadas, sem as escondidas do Windows).</summary>
    public static List<ShareSource> ListWindows()
    {
        var list = new List<ShareSource>();
        EnumWindows((h, _) =>
        {
            try
            {
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if (GetWindow(h, 4) != IntPtr.Zero) return true; // GW_OWNER: janelinhas "filhas" (diálogos) ficam de fora
                var ex = GetWindowLong(h, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true; // TOOLWINDOW sem APPWINDOW
                if (DwmGetInt(h, 14, out var cloaked, 4) == 0 && cloaked != 0) return true; // janela escondida (apps da Loja)

                var len = GetWindowTextLength(h);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                var title = sb.ToString().Trim();
                if (title.Length == 0) return true;

                var cls = new StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                var c = cls.ToString();
                if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return true;

                if (!GetWindowRect(h, out var r)) return true;
                if (r.Right - r.Left < 80 || r.Bottom - r.Top < 60) return true;

                list.Add(new ShareSource
                {
                    Hwnd = h,
                    Title = title,
                    W = r.Right - r.Left,
                    H = r.Bottom - r.Top,
                    Icon = LoadIcon(h)
                });
            }
            catch { /* pula essa janela */ }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Todos os monitores ligados.</summary>
    public static List<ShareSource> ListScreens()
    {
        var list = new List<ShareSource>();
        var n = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref NRect rc, IntPtr data) =>
        {
            n++;
            var w = rc.Right - rc.Left;
            var h = rc.Bottom - rc.Top;
            var main = rc.Left == 0 && rc.Top == 0;
            list.Add(new ShareSource
            {
                IsScreen = true,
                X = rc.Left, Y = rc.Top, W = w, H = h,
                Title = $"Tela {n}" + (main ? " (principal)" : "") + $" · {w}×{h}"
            });
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0) // plano B: só a tela principal
        {
            var w = GetSystemMetrics(0);
            var h = GetSystemMetrics(1);
            if (w > 0 && h > 0)
                list.Add(new ShareSource { IsScreen = true, X = 0, Y = 0, W = w, H = h, Title = $"Tela 1 (principal) · {w}×{h}" });
        }
        return list;
    }

    private static ImageSource? LoadIcon(IntPtr h)
    {
        try
        {
            var ico = IntPtr.Zero;
            foreach (var kind in new[] { 1, 2, 0 }) // ICON_BIG, ICON_SMALL2, ICON_SMALL
            {
                SendMessageTimeout(h, 0x007F, (IntPtr)kind, IntPtr.Zero, 2, 80, out ico); // WM_GETICON, SMTO_ABORTIFHUNG
                if (ico != IntPtr.Zero) break;
            }
            if (ico == IntPtr.Zero)
            {
                var c = GetClassLong(h, -14); // GCL_HICON
                if (c != 0) ico = new IntPtr((long)c);
            }
            if (ico == IntPtr.Zero) return null;
            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }

    // ---- captura ----

    /// <summary>
    /// Tira uma foto da janela/tela, reduzida para caber em maxW×maxH. Devolve os pixels em BGRA (de cima para baixo).
    /// "pixels" é reaproveitado entre as chamadas quando o tamanho não muda (evita lixo de memória a 60 quadros por segundo).
    /// </summary>
    public static GrabResult Grab(ShareSource src, int maxW, int maxH, bool cursor, ref byte[]? pixels, out int dw, out int dh)
    {
        dw = 0; dh = 0;
        int ox, oy, sw, sh, cx = 0, cy = 0, fw = 0, fh = 0;

        if (src.IsScreen)
        {
            ox = src.X; oy = src.Y; sw = src.W; sh = src.H;
        }
        else
        {
            if (!IsWindow(src.Hwnd)) return GrabResult.Gone;
            if (IsIconic(src.Hwnd)) return GrabResult.Failed;
            if (!GetWindowRect(src.Hwnd, out var r)) return GrabResult.Failed;
            fw = r.Right - r.Left; fh = r.Bottom - r.Top; // tamanho total (o PrintWindow desenha tudo isso)

            // Parte visível da janela (sem as bordas invisíveis de sombra do Windows 10/11).
            if (DwmGetRect(src.Hwnd, 9, out var e, Marshal.SizeOf<NRect>()) == 0
                && e.Right - e.Left > 16 && e.Bottom - e.Top > 16
                && e.Left >= r.Left && e.Top >= r.Top && e.Right <= r.Right && e.Bottom <= r.Bottom)
            {
                cx = e.Left - r.Left; cy = e.Top - r.Top;
                ox = e.Left; oy = e.Top; sw = e.Right - e.Left; sh = e.Bottom - e.Top;
            }
            else
            {
                ox = r.Left; oy = r.Top; sw = fw; sh = fh;
            }
        }
        if (sw < 16 || sh < 16) return GrabResult.Failed;

        var scale = Math.Min(1.0, Math.Min(maxW / (double)sw, maxH / (double)sh));
        var w = Math.Max(16, (int)(sw * scale)) & ~1;
        var h = Math.Max(16, (int)(sh * scale)) & ~1;

        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        IntPtr sMem = IntPtr.Zero, sBmp = IntPtr.Zero, sOld = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return GrabResult.Failed;
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, w, h);
            old = SelectObject(mem, bmp);
            SetStretchBltMode(mem, 4); // HALFTONE: reduz com qualidade

            var need = w * h * 4;
            if (pixels == null || pixels.Length != need) pixels = new byte[need];

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var viaScreen = src.IsScreen || src.ScreenBlt || attempt == 1;
                if (viaScreen)
                {
                    if (!StretchBlt(mem, 0, 0, w, h, screen, ox, oy, sw, sh, SRCCOPY | CAPTUREBLT)) return GrabResult.Failed;
                }
                else
                {
                    if (sMem == IntPtr.Zero)
                    {
                        sMem = CreateCompatibleDC(screen);
                        sBmp = CreateCompatibleBitmap(screen, fw, fh);
                        sOld = SelectObject(sMem, sBmp);
                    }
                    if (!PrintWindow(src.Hwnd, sMem, PW_RENDERFULLCONTENT)) { src.ScreenBlt = true; continue; }
                    if (!StretchBlt(mem, 0, 0, w, h, sMem, cx, cy, sw, sh, SRCCOPY)) return GrabResult.Failed;
                }

                if (cursor) DrawCursor(mem, ox, oy, sw, sh, w, h);

                if (!ReadBits(screen, mem, bmp, old, w, h, pixels)) return GrabResult.Failed;

                // PrintWindow devolveu tudo preto (comum em jogos): tenta de novo copiando da tela.
                if (!src.IsScreen && !viaScreen && IsBlank(pixels, w, h)) { src.ScreenBlt = true; continue; }

                dw = w; dh = h;
                return GrabResult.Ok;
            }
            return GrabResult.Failed;
        }
        finally
        {
            if (sMem != IntPtr.Zero)
            {
                if (sOld != IntPtr.Zero) SelectObject(sMem, sOld);
                if (sBmp != IntPtr.Zero) DeleteObject(sBmp);
                DeleteDC(sMem);
            }
            if (mem != IntPtr.Zero)
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                DeleteDC(mem);
            }
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static void DrawCursor(IntPtr mem, int ox, int oy, int sw, int sh, int w, int h)
    {
        try // a captura do Windows não inclui o cursor do mouse: a gente desenha
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
            if (ci.x < ox || ci.y < oy || ci.x >= ox + sw || ci.y >= oy + sh) return;
            var x = (int)((ci.x - ox) * (w / (double)sw));
            var y = (int)((ci.y - oy) * (h / (double)sh));
            DrawIconEx(mem, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 0x0003 | 0x0008);
        }
        catch { /* sem cursor, sem problema */ }
    }

    private static bool ReadBits(IntPtr dc, IntPtr mem, IntPtr bmp, IntPtr old, int w, int h, byte[] px)
    {
        SelectObject(mem, old); // o bitmap não pode estar selecionado para o GetDIBits
        try
        {
            var bi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0
                }
            };
            return GetDIBits(dc, bmp, 0, (uint)h, px, ref bi, 0) != 0;
        }
        finally { SelectObject(mem, bmp); }
    }

    private static bool IsBlank(byte[] px, int w, int h)
    {
        for (var yy = 0; yy < 12; yy++)
        {
            var y = (h - 1) * yy / 11;
            for (var xx = 0; xx < 20; xx++)
            {
                var x = (w - 1) * xx / 19;
                var i = (y * w + x) * 4;
                if (px[i] != 0 || px[i + 1] != 0 || px[i + 2] != 0) return false;
            }
        }
        return true;
    }

    // ---- Windows ----

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref NRect lprc, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
        public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
        public uint biClrUsed; public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public int x; public int y; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out NRect r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, uint step, IntPtr brush, uint flags);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetInt(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetRect(IntPtr h, int attr, out NRect value, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dst, int xd, int yd, int wd, int hd, IntPtr src, int xs, int ys, int ws, int hs, uint rop);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
}
