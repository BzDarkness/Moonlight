using System.Net.Http;
using System.Reflection;

namespace MoonlightLauncher;

/// <summary>
/// O HttpClient padrão desiste de qualquer pedido depois de 100 segundos (o erro
/// "HttpClient.Timeout of 100 seconds elapsing"). Em internet mais lenta, baixar o instalador do Forge
/// ou os arquivos do jogo passa disso. Aqui o limite é ampliado e as falhas de rede são repetidas.
/// </summary>
internal static class HttpTimeouts
{
    public static readonly TimeSpan Long = TimeSpan.FromMinutes(15);

    /// <summary>Amplia o limite de todos os HttpClient que o objeto (ex.: o MinecraftLauncher) usa por dentro.</summary>
    public static void Relax(object? root, int depth = 4)
    {
        try { Walk(root, depth, new HashSet<object>(ReferenceEqualityComparer.Instance)); }
        catch (Exception ex) { LauncherLog.Write("Não consegui ampliar o tempo limite de rede", ex); }
    }

    private static void Walk(object? o, int depth, HashSet<object> seen)
    {
        if (o == null || depth < 0) return;
        var t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is string || o is Delegate || o is Type) return;
        if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal) && o is not HttpClient) return;
        if (!seen.Add(o)) return;

        for (var bt = t; bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            foreach (var f in bt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object? v;
                try { v = f.GetValue(o); }
                catch { continue; }

                if (v is HttpClient hc) Fix(hc, f, o);
                else if (v != null && !f.FieldType.IsValueType) Walk(v, depth - 1, seen);
            }
        }
    }

    private static void Fix(HttpClient hc, FieldInfo field, object owner)
    {
        if (hc.Timeout == Timeout.InfiniteTimeSpan || hc.Timeout >= Long) return;
        try { hc.Timeout = Long; return; }
        catch (InvalidOperationException) { /* já fez pedidos: só dá para trocar o cliente inteiro */ }

        try
        {
            var nc = new HttpClient { Timeout = Long };
            foreach (var h in hc.DefaultRequestHeaders)
                nc.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
            if (hc.BaseAddress != null) nc.BaseAddress = hc.BaseAddress;
            field.SetValue(owner, nc);
        }
        catch (Exception ex) { LauncherLog.Write("Não consegui trocar o HttpClient", ex); }
    }

    /// <summary>Falha de internet (tempo esgotado, conexão caiu, servidor fora do ar).</summary>
    public static bool IsNetworkFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is TaskCanceledException or TimeoutException or HttpRequestException or System.Net.Sockets.SocketException)
                return true;
        return false;
    }

    /// <summary>Mesma coisa para métodos que devolvem ValueTask (o CmlLib usa ValueTask em InstallAndBuildProcessAsync).</summary>
    public static Task<T> RetryAsync<T>(Func<ValueTask<T>> work, Action<string>? status = null, int attempts = 3)
    {
        Func<Task<T>> asTask = async () => await work();
        return RetryAsync(asTask, status, attempts);
    }

    /// <summary>Roda a tarefa e, se a internet falhar, tenta de novo (o que já foi baixado é aproveitado).</summary>
    public static async Task<T> RetryAsync<T>(Func<Task<T>> work, Action<string>? status = null, int attempts = 3)
    {
        for (var i = 1; ; i++)
        {
            try { return await work(); }
            catch (Exception ex) when (i < attempts && IsNetworkFailure(ex))
            {
                LauncherLog.Write($"Rede falhou (tentativa {i}/{attempts}); tentando de novo", ex);
                status?.Invoke($"A conexão falhou. Tentando de novo ({i + 1}/{attempts})…");
                await Task.Delay(TimeSpan.FromSeconds(2 * i));
            }
        }
    }
}
