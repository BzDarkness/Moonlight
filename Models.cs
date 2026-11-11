using System.Text.Json.Serialization;

namespace MoonlightLauncher;

public enum Loader { Vanilla, OptiFine, Forge, Fabric, NeoForge, ForgeOptiFine }

// ElyBy foi removido do launcher. O valor continua aqui só para conseguir ler um settings.json antigo
// sem apagar as outras contas; contas desse tipo são descartadas assim que o launcher abre.
public enum AccountType { Offline, Microsoft, ElyBy, Moonlight }

public record Account(AccountType Type, string Name, string Uuid = "", string AccessToken = "", string ClientToken = "",
    bool SkinSlim = false)
{
    [JsonIgnore] public string Key => $"{Type}:{Name}";

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        AccountType.Microsoft => "MICROSOFT",
        AccountType.Moonlight => "MOONLIGHT",
        _ => "OFFLINE"
    };
}

public class AppState
{
    public string Modo { get; set; } = "Vanilla";
    public int Ram { get; set; } = 4096;
    public string ActiveKey { get; set; } = "";
    public string ClientToken { get; set; } = Guid.NewGuid().ToString("N");
    public string CurseForgeKey { get; set; } = "";
    public string PerfMode { get; set; } = "Auto"; // Auto | Full | Light
    public string Theme { get; set; } = "Default"; // Default | Light | Dark | Midnight
    public string GoldenCode { get; set; } = "";   // código de ativação do Golden Moon
    public bool GoldPalettesOn { get; set; }       // usar as paletas personalizadas (só com o Golden Moon)
    public string GoldColor1 { get; set; } = "#FFD54F";
    public string GoldColor2 { get; set; } = "#FF8F00";
    public string AppIcon { get; set; } = "";      // ícone do aplicativo (só com o Golden Moon). Vazio = o original
    public string MoonIcon { get; set; } = "";     // "mini lua" da barra lateral e da abertura (só com o Golden Moon)
    public bool ModsDetailed { get; set; }         // aba Mods no modo detalhado (cartões com banner)
    public bool AutoUpdate { get; set; } = true;   // baixar e instalar novas versões do launcher sozinho
    public string AppliedUpdate { get; set; } = "";    // última versão que o launcher já baixou/aplicou (evita atualizar em loop se o .exe da Release tiver versão menor que a tag)
    public bool DesktopShortcut { get; set; } = true; // criar/manter o atalho "Moonlight Launcher" na área de trabalho
    public bool ShortcutCreated { get; set; }          // o launcher já criou o atalho alguma vez (se a pessoa apagar, não recria sozinho)
    public string ShortcutSig { get; set; } = "";      // caminho do .exe + ícone usados no último atalho gravado
    public bool PlayDetailed { get; set; } = true; // aba Jogar no modo detalhado (cartões com banner de cada versão)
    public List<Account> Accounts { get; set; } = new();
}

// Item de mod / modpack / textura / shader vindo do Modrinth ou do CurseForge.
public record ContentItem(
    string Source, string Id, string Type, string Title, string Description,
    string Author, string IconUrl, long Downloads, string PageUrl,
    string BannerUrl = "", int ColorRgb = 0, long Follows = 0, string Updated = "", string Categories = "")
{
    // Quando um modpack está sendo montado, o botão dos mods vira "No modpack" (ver Modpacks.cs).
    public static bool AddToPackMode { get; set; }

    // Cor do mod: a que o Modrinth informa ou, se não houver, a que o launcher tira da foto (ver ModsUi.cs).
    [JsonIgnore] public AccentInfo Accent { get; } = new(ColorRgb, Title);

    [JsonIgnore]
    public string ActionLabel => Type == "modpack" ? "🌐  Abrir página"
        : AddToPackMode && Type == "mod" ? "➕  No modpack"
        : "⬇  Instalar";

    private static string Compact(long n) =>
        n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1_000 ? $"{n / 1_000.0:0.#}k" : n.ToString();

    [JsonIgnore] public string DownloadsText => Compact(Downloads);
    [JsonIgnore] public string FollowsText => Compact(Follows);
    [JsonIgnore] public bool HasFollows => Follows > 0;

    [JsonIgnore]
    public string Meta => $"por {Author}  ·  {Compact(Downloads)} downloads  ·  {Source}";

    [JsonIgnore]
    public string UpdatedText
    {
        get
        {
            if (!DateTimeOffset.TryParse(Updated, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d)) return "";
            var days = (int)(DateTimeOffset.UtcNow - d).TotalDays;
            if (days < 1) return "hoje";
            if (days < 2) return "ontem";
            if (days < 31) return $"há {days} dias";
            if (days < 365) return $"há {days / 30} meses";
            return days < 730 ? "há 1 ano" : $"há {days / 365} anos";
        }
    }

    private static readonly Dictionary<string, string> TagNames = new()
    {
        ["optimization"] = "Otimização", ["adventure"] = "Aventura", ["technology"] = "Tecnologia",
        ["magic"] = "Magia", ["utility"] = "Utilidades", ["library"] = "Biblioteca", ["decoration"] = "Decoração",
        ["equipment"] = "Equipamento", ["food"] = "Comida", ["game-mechanics"] = "Mecânicas",
        ["management"] = "Gerenciamento", ["mobs"] = "Mobs", ["worldgen"] = "Geração de mundo",
        ["world-gen"] = "Geração de mundo", ["storage"] = "Armazenamento", ["transportation"] = "Transporte",
        ["social"] = "Social", ["economy"] = "Economia", ["cursed"] = "Amaldiçoado", ["minigame"] = "Minigame",
        ["combat"] = "Combate", ["quests"] = "Missões", ["lightweight"] = "Leve", ["multiplayer"] = "Multijogador",
        ["kitchen-sink"] = "Completão", ["realistic"] = "Realista", ["fantasy"] = "Fantasia", ["cartoon"] = "Cartoon",
        ["vanilla-like"] = "Estilo vanilla", ["semi-realistic"] = "Semi-realista", ["cinematic"] = "Cinematográfico",
        ["colored-lighting"] = "Luz colorida", ["atmosphere"] = "Atmosfera", ["performance-impact"] = "Desempenho",
        ["16x"] = "16x", ["32x"] = "32x", ["64x"] = "64x", ["128x"] = "128x", ["256x"] = "256x",
        ["armor-weapons-tools"] = "Armas e ferramentas", ["tech"] = "Tecnologia", ["exploration"] = "Exploração",
        ["hardcore"] = "Hardcore", ["challenging"] = "Desafiador", ["skyblock"] = "Skyblock", ["technology-"] = "Tecnologia"
    };

    private static readonly HashSet<string> Hidden = new()
    {
        "fabric", "forge", "neoforge", "quilt", "vanilla", "iris", "optifine", "minecraft", "bukkit", "spigot", "paper",
        "purpur", "folia", "sponge", "bungeecord", "velocity", "datapack", "canvas", "liteloader", "rift"
    };

    [JsonIgnore]
    public IReadOnlyList<string> TagList
    {
        get
        {
            var list = new List<string>();
            foreach (var raw in (Categories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var id = raw.Trim().ToLowerInvariant().Replace(' ', '-').Replace('&', '-').Replace("--", "-");
                if (id.Length == 0 || Hidden.Contains(id)) continue;
                var label = TagNames.TryGetValue(id, out var l) ? l
                    : char.ToUpperInvariant(raw.Trim()[0]) + raw.Trim()[1..].Replace('-', ' ');
                if (!list.Contains(label)) list.Add(label);
                if (list.Count == 4) break;
            }
            return list;
        }
    }
}
