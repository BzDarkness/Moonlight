using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

/// <summary>
/// Sistema de otimização do launcher.
///  • Modo Automático: detecta PC fraco (sem aceleração de vídeo, poucos núcleos, pouca RAM, acesso remoto).
///  • Modo Leve: menos estrelas, sem efeitos em movimento, 3D mais econômico, animações a 20 quadros, sem tremor/brilhos de abertura, sem efeito de mouse.
///  • Modo Completo: tudo ligado.
/// Além disso o launcher pausa as animações quando minimizado/escondido e devolve memória ao Windows.
/// </summary>
public static class Perf
{
    /// <summary>"Auto", "Full" ou "Light" (o que o jogador escolheu).</summary>
    public static string Setting { get; private set; } = "Auto";

    /// <summary>Resultado final: true = usar o visual leve.</summary>
    public static bool Light { get; private set; }

    /// <summary>Disparado quando o jogador troca o modo na aba Ajustes.</summary>
    public static event Action? Changed;

    private static readonly string SettingsFile = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonlightLauncher", "settings.json");

    /// <summary>Chamado no começo do programa, antes de qualquer janela existir.</summary>
    public static void Initialize()
    {
        string? saved = null;
        try
        {
            if (File.Exists(SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
                if (doc.RootElement.TryGetProperty("PerfMode", out var pm) && pm.ValueKind == JsonValueKind.String)
                    saved = pm.GetString();
            }
        }
        catch { /* arquivo ruim: usa o automático */ }

        Resolve(saved);

        // No modo leve, TODAS as animações do programa passam a rodar a 20 quadros por segundo.
        // (Isso só pode ser definido uma vez por execução; por isso vale a partir da próxima abertura.)
        if (Light)
        {
            try
            {
                Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = 20 });
            }
            catch { /* já tinha sido definido */ }
        }
    }

    /// <summary>Aplica um modo escolhido pelo jogador agora, sem reiniciar.</summary>
    public static void Apply(string? value)
    {
        Resolve(value);
        Changed?.Invoke();
    }

    private static void Resolve(string? value)
    {
        Setting = value is "Full" or "Light" ? value : "Auto";
        Light = Setting == "Light" || (Setting == "Auto" && LooksWeak());
    }

    /// <summary>Texto curto para mostrar na aba Ajustes.</summary>
    public static string EffectiveText =>
        Light ? "Visual leve ativo (poucas animações, 3D econômico)." : "Visual completo ativo (todas as animações).";

    private static bool LooksWeak()
    {
        try
        {
            if ((RenderCapability.Tier >> 16) == 0) return true;   // sem aceleração de vídeo
            if (SystemParameters.IsRemoteSession) return true;      // acesso remoto: desenho lento
            if (Environment.ProcessorCount <= 2) return true;
            var ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (ram > 0 && ram < 4_500_000_000L) return true;        // ~4 GB ou menos
        }
        catch { /* na dúvida, visual completo */ }
        return false;
    }

    // ---------- Memória ----------

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    public static long WorkingSetMb => Environment.WorkingSet / (1024 * 1024);

    // ---------- RAM do PC (para o Minecraft não pedir mais memória do que existe) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static bool TryReadRam(out long totalMb, out long availableMb)
    {
        totalMb = 0;
        availableMb = 0;
        try
        {
            var s = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref s)) return false;
            totalMb = (long)(s.ullTotalPhys / (1024 * 1024));
            availableMb = (long)(s.ullAvailPhys / (1024 * 1024));
            return totalMb > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Maior RAM que dá para dar ao Minecraft com segurança neste PC (múltiplo de 512 MB).
    /// Deixa pelo menos 2 GB (ou 25%) livres para o Windows, o launcher e o resto: dar toda a RAM ao
    /// jogo é a causa mais comum de crash ("Could not reserve enough space for heap" / travadas).
    /// </summary>
    public static int MaxGameRamMb
    {
        get
        {
            if (!TryReadRam(out var total, out _)) return 8192; // não deu para ler: valor moderado
            var reserve = Math.Max(2048L, total / 4);
            var max = (int)Math.Min(16384L, total - reserve);
            return Math.Max(1024, max / 512 * 512);
        }
    }

    /// <summary>Limita a RAM pedida ao máximo seguro E ao que está realmente livre agora.</summary>
    public static int ClampGameRam(int wantedMb)
    {
        var v = Math.Min(wantedMb, MaxGameRamMb);
        if (TryReadRam(out _, out var avail))
        {
            var free = (int)Math.Min(int.MaxValue, avail - 768); // folga para o resto do sistema
            if (free >= 1024 && v > free) v = free / 512 * 512;
        }
        return Math.Max(1024, v);
    }

    /// <summary>
    /// Opções de JVM que deixam o Minecraft mais leve (menos travadinhas de memória). Só entra se o
    /// jogo ainda não escolheu um coletor de lixo próprio, para nunca conflitar com a versão/loader.
    /// </summary>
    public static readonly string[] LightJvmFlags =
    {
        "-XX:+UseG1GC",
        "-XX:MaxGCPauseMillis=100",
    };

    /// <summary>Faz uma limpeza e devolve a memória que o launcher não está usando para o Windows.</summary>
    public static void TrimMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            using var me = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(me.Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { /* otimização nunca pode derrubar o launcher */ }
    }
}

/// <summary>Posição do mouse na tela inteira (funciona mesmo com o mouse fora da janela).</summary>
internal static class CursorUtil
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    public static bool TryGetPosition(Visual relativeTo, out Point position)
    {
        position = default;
        try
        {
            if (!GetCursorPos(out var p)) return false;
            if (PresentationSource.FromVisual(relativeTo) == null) return false;
            position = relativeTo.PointFromScreen(new Point(p.X, p.Y));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
