// Kiln-native catalogue loader for the original Endless Chests defaults.
// Uses no game memory offsets, ShroudForge runtime, or third-party DLL.
using System.Reflection;
using System.Text;
using Kiln;

namespace EndlessChests;

internal sealed class IniCatalog
{
    public Dictionary<string, List<string>> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChestNameMatcher _nameMatcher = new();

    // Issue #16: four quest icon items share this display label with the
    // genuine building block. A code-level pin also fixes old, user-owned
    // endless-chests.ini files which we must never overwrite.
    internal const uint RoughWoodBlockId = 2012474258u;
    internal const string RoughWoodBlockDebugName = "Block_T1_Wood_Rough_CRAFTING";

    // Cache the display-name collision index for each item catalogue snapshot,
    // rather than scanning the whole game registry for every chest item.
    private IReadOnlyList<ItemInfo>? _indexedItems;
    private Dictionary<string, int> _displayNameCounts = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshDisplayNameIndex()
    {
        var all = Items.All;
        if (ReferenceEquals(all, _indexedItems)) return;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in all)
        {
            if (string.IsNullOrWhiteSpace(item.DisplayName)) continue;
            counts[item.DisplayName] = counts.GetValueOrDefault(item.DisplayName) + 1;
        }
        _displayNameCounts = counts;
        _indexedItems = all;
    }

    private static ItemInfo? LookupItem(string name)
    {
        if (!name.Equals("Rough Wood Block", StringComparison.OrdinalIgnoreCase))
            return Items.Lookup(name);

        // Fail closed on game data changes; never fall back to a quest icon.
        var matched = Items.Get(new ItemId(RoughWoodBlockId));
        return matched is not null && !matched.IsBlueprint &&
            string.Equals(matched.Name, RoughWoodBlockDebugName, StringComparison.Ordinal)
            ? matched : null;
    }

    public int RecognizedNames => _nameMatcher.Count;

    // Resolve a game-produced FNV hash to a known catalogue entry using
    // original/lower/upper/title spelling, or a bounded ASCII case search.
    public bool TryGameName(ulong hash, out string selection) =>
        _nameMatcher.TryResolve(hash, out selection);

    private void BuildGameNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Groups.Keys.Concat(Aliases.Keys)
                     .Concat(Groups.Values.SelectMany(x => x))
                     .Concat(DisabledItems)
                     .Append("Empty"))
        {
            if (names.Add(name)) _nameMatcher.Add(name);
        }
    }

    public bool AllowMagicChests { get; private set; }

    // The original file includes settings that require engine APIs not yet present
    // in Kiln.Items (gear enhancements and gem-level creation); never claim support.
    public bool EnableGemLevels { get; private set; }
    public bool FullyUpgradeGear { get; private set; }

    public static string DefaultText()
    {
        using var stream = typeof(IniCatalog).Assembly.GetManifestResourceStream("EndlessChests.DefaultsIni")
            ?? throw new InvalidOperationException("The packaged default catalogue resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static IniCatalog Parse(string text)
    {
        var c = new IniCatalog();
        string section = "";
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var split = line.IndexOf('=');
            if (split <= 0) throw new InvalidDataException($"Invalid INI entry on line {lineNumber}.");
            var name = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (name.Length == 0) throw new InvalidDataException($"Empty INI key on line {lineNumber}.");
            switch (section.ToLowerInvariant())
            {
                case "groups":
                    var names = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (names.Count is < 1 or > 512) throw new InvalidDataException($"Invalid group '{name}'.");
                    if (!c.Groups.TryAdd(name, names)) throw new InvalidDataException($"Duplicate group '{name}'.");
                    break;
                case "aliases":
                    if (value.Length == 0 || !c.Aliases.TryAdd(name, value))
                        throw new InvalidDataException($"Invalid alias '{name}'.");
                    break;
                case "disabledgroups":
                    if (Enabled(value)) c.DisabledGroups.Add(name);
                    break;
                case "disableditems":
                    if (Enabled(value)) c.DisabledItems.Add(name);
                    break;
                case "options":
                    if (name.Equals("AllowMagicChests", StringComparison.OrdinalIgnoreCase))
                        c.AllowMagicChests = Enabled(value);
                    if (name.Equals("EnableGemLevels", StringComparison.OrdinalIgnoreCase))
                        c.EnableGemLevels = Enabled(value);
                    if (name.Equals("FullyUpgradeGear", StringComparison.OrdinalIgnoreCase))
                        c.FullyUpgradeGear = Enabled(value);
                    break;
            }
        }
        if (c.Groups.Count == 0) throw new InvalidDataException("The catalogue has no groups.");
        c.BuildGameNames();
        return c;
    }

    private static bool Enabled(string value) =>
        value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    public static IniCatalog LoadOrCreate(string path)
    {
        if (!File.Exists(path)) File.WriteAllText(path, DefaultText());
        return Parse(File.ReadAllText(path));
    }

    public string ExpandAlias(string input)
    {
        var selection = input.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 16 && Aliases.TryGetValue(selection, out var replacement); i++)
        {
            if (!seen.Add(selection)) throw new CommandException($"Alias cycle at '{selection}'.");
            selection = replacement.Trim();
        }
        if (Aliases.ContainsKey(selection)) throw new CommandException("Alias chain is too deep.");
        return selection;
    }

    private HashSet<string> BlockedFromGroups()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in DisabledGroups)
        {
            if (Groups.TryGetValue(group, out var names))
                foreach (var name in names) blocked.Add(name);
        }
        return blocked;
    }

    // Do not fail an otherwise usable group because one catalogue entry was
    // removed or renamed in a later game build.
    public SelectionPlan Resolve(string requested, int maxItems)
    {
        var selection = ExpandAlias(requested);
        if (string.Equals(selection, "Empty", StringComparison.OrdinalIgnoreCase))
            return new SelectionPlan(selection, [], [], [], [], true);

        if (DisabledGroups.Contains(selection))
            return new SelectionPlan(selection, [], [], [$"The group '{selection}' is disabled."], [], false);

        RefreshDisplayNameIndex();
        var names = Groups.TryGetValue(selection, out var group) ? group : [selection];
        var denied = BlockedFromGroups();
        var resolved = new List<ItemInfo>();
        var unknown = new List<string>();
        var blocked = new List<string>();
        var ambiguous = new List<string>();
        foreach (var name in names)
        {
            // A disabled group excludes its members everywhere, even when
            // another enabled group or a custom alias names the same item.
            if (DisabledItems.Contains(name) || denied.Contains(name))
            {
                blocked.Add(name);
                continue;
            }
            ItemInfo? info = LookupItem(name);
            if (info is null || info.IsBlueprint)
            {
                unknown.Add(name.Equals("Rough Wood Block", StringComparison.OrdinalIgnoreCase)
                    ? $"{name} (verified ID {RoughWoodBlockId} missing or changed)" : name);
                continue;
            }
            // Preserve previous behaviour for other ambiguous item labels,
            // but log which numeric ID was selected so it can be corrected.
            if (!name.Equals("Rough Wood Block", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(info.DisplayName, name, StringComparison.OrdinalIgnoreCase) &&
                _displayNameCounts.TryGetValue(name, out var count) && count > 1)
                ambiguous.Add($"{name} ({count} IDs; selected {info.Id.Value})");
            if (resolved.All(x => x.Id != info.Id)) resolved.Add(info);
            if (resolved.Count >= maxItems) break;
        }
        return new SelectionPlan(selection, resolved, unknown, blocked, ambiguous, false);
    }
}

internal sealed record SelectionPlan(string Selection, List<ItemInfo> Items,
    List<string> Unknown, List<string> Blocked, List<string> Ambiguous, bool IsEmpty);
