using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

namespace MoonlightLauncher;

public record CrashInfo(string Title, string Explanation, List<string> Evidence);

public static class CrashAnalyzer
{
    private record Rule(string Pattern, string Title, string Explanation);

    private static readonly Rule[] Rules =
    {
        new(@"UnsupportedClassVersionError|more recent version of the Java Runtime",
            "Versão do Java incompatível",
            "Algum mod exige uma versão do Java mais nova do que a usada pelo jogo. Atualize o mod para uma versão feita para este Minecraft, ou remova-o."),

        new(@"OutOfMemoryError|Could not reserve enough space|There is insufficient memory",
            "Memória insuficiente",
            "O jogo ficou sem memória RAM. Aumente a RAM na tela Jogar (sem passar da memória do seu PC) ou remova mods e shaders pesados."),

        new(@"Incompatible mods found|Mod resolution failed|ModResolutionException|Unmet dependency|which is missing",
            "Mods incompatíveis (Fabric)",
            "Um ou mais mods precisam de outra versão do Minecraft, do Fabric Loader ou de outro mod (como o Fabric API). Veja os detalhes, instale o que falta ou remova o mod problemático."),

        new(@"Missing or unsupported mandatory dependencies|MissingModsException|ModLoadingException|Incompatible mod set|[Dd]uplicate mod|Requested by",
            "Mods incompatíveis (Forge/NeoForge)",
            "Um mod depende de outro que está faltando, está em versão errada, ou há mods duplicados na pasta. Veja os detalhes abaixo e corrija ou remova o mod."),

        new(@"InvalidMixinException|Mixin apply failed|MixinTransformerError|Mixin transformation of|MixinApplyError|Critical injection failure",
            "Conflito entre mods (Mixin)",
            "Dois mods tentam alterar a mesma parte do jogo, ou um mod foi feito para outra versão. Remova os últimos mods adicionados, um de cada vez, para achar o culpado."),

        new(@"NoSuchMethodError|NoSuchFieldError|NoClassDefFoundError|ClassNotFoundException|AbstractMethodError|IncompatibleClassChangeError",
            "Mod feito para outra versão",
            "Um mod tenta usar algo que não existe nesta versão do Minecraft ou do loader. Confira se todos os mods são da MESMA versão do jogo e do mesmo loader (Forge, Fabric…)."),

        new(@"GLFW error|Pixel format not accelerated|EXCEPTION_ACCESS_VIOLATION|ig9icd|nvoglv|atio6axx|GL_OUT_OF_MEMORY",
            "Problema com a placa de vídeo",
            "O driver gráfico falhou. Atualize os drivers da placa de vídeo e, se estiver usando OptiFine/shaders, teste sem eles."),
    };

    private static readonly Regex GeneralEvidence = new(
        @"requires|depends|incompatible|missing|mixin|duplicate|Caused by|NoSuch|NoClassDef|ClassNotFound|Mod ?ID|UnsupportedClassVersion|OutOfMemory",
        RegexOptions.IgnoreCase);

    public static CrashInfo Analyze(string text, int exitCode)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();

        foreach (var rule in Rules)
        {
            var rx = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
            if (!rx.IsMatch(text)) continue;

            var evidence = lines.Where(l => rx.IsMatch(l)).Select(Clip).Distinct().Take(6).ToList();
            evidence.AddRange(lines.Where(l => l.TrimStart().StartsWith("Caused by", StringComparison.OrdinalIgnoreCase))
                                   .Select(Clip).Distinct().Take(2));
            return new CrashInfo(rule.Title, rule.Explanation, evidence.Distinct().ToList());
        }

        var generic = lines.Where(l => GeneralEvidence.IsMatch(l)).Select(Clip).Distinct().Take(6).ToList();
        if (generic.Count == 0)
            generic = lines.TakeLast(8).Select(Clip).ToList();

        return new CrashInfo(
            "O jogo fechou inesperadamente",
            $"O Minecraft encerrou com o código {exitCode}. Se você acabou de adicionar mods, remova-os e teste de novo. Abra o relatório para ver o motivo exato.",
            generic);
    }

    private static string Clip(string s)
    {
        s = s.Trim();
        return s.Length > 240 ? s[..240] + "…" : s;
    }
}

public static class ModScanner
{
    private static bool AtLeast(string v, string min) =>
        Version.TryParse(v, out var a) && Version.TryParse(min, out var b) && a >= b;

    // Procura mods feitos para outro modloader dentro da pasta mods.
    public static List<string> FindWrongLoaderMods(string modsDir, Loader loader, string mc)
    {
        var result = new List<string>();
        if (!Directory.Exists(modsDir)) return result;
        if (loader is Loader.Vanilla or Loader.OptiFine) return result;

        foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            var name = IOPath.GetFileName(file);
            if (name.StartsWith("OptiFine", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var zip = ZipFile.OpenRead(file);
                bool fabric = zip.GetEntry("fabric.mod.json") != null || zip.GetEntry("quilt.mod.json") != null;
                bool forge = zip.GetEntry("META-INF/mods.toml") != null || zip.GetEntry("mcmod.info") != null;
                bool neo = zip.GetEntry("META-INF/neoforge.mods.toml") != null;

                switch (loader)
                {
                    case Loader.Fabric:
                        if (!fabric && (forge || neo))
                            result.Add($"• {name} — é um mod de Forge/NeoForge e não funciona no Fabric.");
                        break;

                    case Loader.Forge:
                    case Loader.ForgeOptiFine:
                        if (!forge && fabric)
                            result.Add($"• {name} — é um mod de Fabric e não funciona no Forge.");
                        else if (!forge && neo)
                            result.Add($"• {name} — é um mod de NeoForge e não funciona no Forge.");
                        break;

                    case Loader.NeoForge:
                        if (fabric && !neo && !forge)
                            result.Add($"• {name} — é um mod de Fabric e não funciona no NeoForge.");
                        else if (forge && !neo && !fabric && AtLeast(mc, "1.20.5"))
                            result.Add($"• {name} — é um mod de Forge; no NeoForge 1.20.5+ ele pode não funcionar.");
                        break;
                }
            }
            catch
            {
                result.Add($"• {name} — não foi possível abrir o arquivo (pode estar corrompido).");
            }

            if (result.Count >= 10) break;
        }

        return result;
    }
}
