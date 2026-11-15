// Identity-based Endless Chests catalogue. Group membership and game item
// identities are embedded JSON; editable INI controls only runtime settings.
// No display-name guessing and no compatibility lookup for retired aliases.
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiln;

namespace EndlessChests;

internal sealed record GroupEntry(uint? Id, string? Unresolved);
internal sealed record ItemIdentity(string DisplayName, string DebugName, string Category);

internal sealed class ItemCatalogue
{
    public Dictionary<string, List<GroupEntry>> Groups { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Aliases { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledGroups { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledItems { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int IgnoredLegacyGroups { get; private set; }

    private readonly Dictionary<uint, ItemIdentity> _identities = [];
    private readonly Dictionary<string, uint?> _directNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ChestNameMatcher _nameMatcher = new();

    public int RecognizedNames => _nameMatcher.Count;
    public bool AllowMagicChests { get; private set; }
    public bool EnableGemLevels { get; private set; }
    public bool FullyUpgradeGear { get; private set; }

    public bool TryGameName(ulong hash, out string selection) =>
        _nameMatcher.TryResolve(hash, out selection);

    private static string EmbeddedText(string resource)
    {
        using var stream = typeof(ItemCatalogue).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Missing embedded catalogue resource: {resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static string DefaultSettingsText() =>
        EmbeddedText("EndlessChests.DefaultSettingsIni");

    public static ItemCatalogue LoadOrCreate(string settingsPath)
    {
        // The packaged JSON is authoritative, including on upgrades. Never
        // load an old [Groups] section from an existing settings-only INI.
        var catalogue = ParseCatalogue(EmbeddedText("EndlessChests.CatalogueJson"));
        if (!File.Exists(settingsPath))
            File.WriteAllText(settingsPath, DefaultSettingsText());
        catalogue.ParseSettings(File.ReadAllText(settingsPath));
        catalogue.BuildGameNames();
        return catalogue;
    }

    private static ItemCatalogue ParseCatalogue(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
            throw new InvalidDataException("Unsupported Endless Chests catalogue schema");
        var catalogue = new ItemCatalogue();
        foreach (var property in root.GetProperty("items").EnumerateObject())
        {
            if (!uint.TryParse(property.Name, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var id) || id == 0)
                throw new InvalidDataException($"Invalid catalogue item ID '{property.Name}'");
            var value = property.Value;
            var display = value.GetProperty("displayName").GetString();
            var debug = value.GetProperty("debugName").GetString();
            var category = value.GetProperty("category").GetString();
            if (string.IsNullOrWhiteSpace(display) ||
                string.IsNullOrWhiteSpace(debug) || string.IsNullOrWhiteSpace(category))
                throw new InvalidDataException($"Incomplete catalogue identity {id}");
            catalogue._identities.Add(id, new ItemIdentity(display, debug, category));
        }
        foreach (var property in root.GetProperty("groups").EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) ||
                property.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Invalid catalogue group '{property.Name}'");
            var entries = new List<GroupEntry>();
            foreach (var value in property.Value.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number)
                {
                    var id = value.GetUInt32();
                    if (!catalogue._identities.ContainsKey(id))
                        throw new InvalidDataException($"Group '{property.Name}' references unknown ID {id}");
                    entries.Add(new GroupEntry(id, null));
                }
                else if (value.ValueKind == JsonValueKind.Object &&
                         value.TryGetProperty("variants", out var variants))
                {
                    // One historical group position can denote multiple real
                    // decorative forms sharing an in-game display name.
                    // IDs remain authoritative; expand in source order so
                    // all valid variants refill without a name-based guess.
                    if (value.EnumerateObject().Count() != 1 ||
                        variants.ValueKind != JsonValueKind.Array ||
                        variants.GetArrayLength() is < 2 or > 64)
                        throw new InvalidDataException(
                            $"Group '{property.Name}' has invalid variants");
                    var seen = new HashSet<uint>();
                    foreach (var variant in variants.EnumerateArray())
                    {
                        if (variant.ValueKind != JsonValueKind.Number ||
                            !variant.TryGetUInt32(out var id) || id == 0 ||
                            !catalogue._identities.ContainsKey(id) || !seen.Add(id))
                            throw new InvalidDataException(
                                $"Group '{property.Name}' has unknown or duplicated variant ID");
                        entries.Add(new GroupEntry(id, null));
                    }
                }
                else if (value.ValueKind == JsonValueKind.Object &&
                         value.TryGetProperty("unresolved", out var missing) &&
                         missing.ValueKind == JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(missing.GetString()))
                {
                    // Keep an explicit unresolved marker as a safe fallback
                    // for future game SDK changes; never guess display names.
                    entries.Add(new GroupEntry(null, missing.GetString()));
                }
                else
                {
                    throw new InvalidDataException(
                        $"Group '{property.Name}' has a malformed item reference");
                }
            }
            if (entries.Count is < 1 or > 512 ||
                !catalogue.Groups.TryAdd(property.Name, entries))
                throw new InvalidDataException($"Invalid or duplicate group '{property.Name}'");
        }
        if (catalogue.Groups.Count == 0)
            throw new InvalidDataException("The JSON catalogue has no groups");
        return catalogue;
    }

    private void ParseSettings(string settings)
    {
        var section = "";
        var lineNumber = 0;
        foreach (var raw in settings.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            var split = line.IndexOf('=');
            if (split <= 0)
                throw new InvalidDataException($"Invalid settings INI entry at line {lineNumber}");
            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (key.Length == 0)
                throw new InvalidDataException($"Empty settings key on line {lineNumber}");
            switch (section)
            {
                case "groups":
                    // Existing 0.4.x INIs can remain in place. Their legacy
                    // group entries are ignored; JSON defines every group.
                    IgnoredLegacyGroups++;
                    break;
                case "aliases":
                    if (value.Length == 0 || !Aliases.TryAdd(key, value))
                        throw new InvalidDataException($"Invalid alias '{key}'");
                    break;
                case "disabledgroups":
                    if (Enabled(value)) DisabledGroups.Add(key);
                    break;
                case "disableditems":
                    // Numeric IDs only. Historic display-name exclusions
                    // were ambiguous and could suppress valid regular items.
                    if (Enabled(value) &&
                        uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                        DisabledItems.Add(id.ToString(CultureInfo.InvariantCulture));
                    break;
                case "options":
                    if (key.Equals("AllowMagicChests", StringComparison.OrdinalIgnoreCase))
                        AllowMagicChests = Enabled(value);
                    if (key.Equals("EnableGemLevels", StringComparison.OrdinalIgnoreCase))
                        EnableGemLevels = Enabled(value);
                    if (key.Equals("FullyUpgradeGear", StringComparison.OrdinalIgnoreCase))
                        FullyUpgradeGear = Enabled(value);
                    break;
            }
        }
    }

    private static bool Enabled(string value) =>
        value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private void BuildGameNames()
    {
        // Unique displayed item names are allowed for direct chest selection.
        // Different item IDs sharing the same name must use a group or ID.
        foreach (var (id, entry) in _identities)
        {
            if (_directNames.TryGetValue(entry.DisplayName, out var previous))
            {
                if (previous != id) _directNames[entry.DisplayName] = null;
            }
            else
                _directNames.Add(entry.DisplayName, id);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Groups.Keys.Concat(Aliases.Keys)
                     .Concat(_directNames.Where(x => x.Value.HasValue).Select(x => x.Key))
                     .Concat(_identities.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture)))
                     .Append("Empty"))
        {
            if (names.Add(name)) _nameMatcher.Add(name);
        }
    }

    public string ExpandAlias(string input)
    {
        var selection = input.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 16 && Aliases.TryGetValue(selection, out var next); i++)
        {
            if (!seen.Add(selection))
                throw new CommandException($"Alias cycle at '{selection}'");
            selection = next.Trim();
        }
        if (Aliases.ContainsKey(selection))
            throw new CommandException("Alias chain is too deep");
        return selection;
    }

    private HashSet<uint> BlockedFromGroups()
    {
        var denied = new HashSet<uint>();
        foreach (var group in DisabledGroups)
        {
            if (Groups.TryGetValue(group, out var entries))
                foreach (var entry in entries)
                    if (entry.Id is uint id) denied.Add(id);
        }
        return denied;
    }

    private ItemInfo? VerifiedItem(uint id)
    {
        if (!_identities.TryGetValue(id, out var identity)) return null;
        var item = Items.Get(new ItemId(id));
        return item is not null && !item.IsBlueprint &&
            string.Equals(item.Name, identity.DebugName, StringComparison.Ordinal) &&
            string.Equals(item.DisplayName, identity.DisplayName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Category.ToString(), identity.Category, StringComparison.Ordinal)
            ? item : null;
    }

    public SelectionPlan Resolve(string requested, int maxItems)
    {
        var selection = ExpandAlias(requested);
        if (string.Equals(selection, "Empty", StringComparison.OrdinalIgnoreCase))
            return new SelectionPlan(selection, [], [], [], [], true);

        if (DisabledGroups.Contains(selection))
            return new SelectionPlan(selection, [], [],
                [$"The group '{selection}' is disabled."], [], false);

        List<GroupEntry> entries;
        if (Groups.TryGetValue(selection, out var group))
            entries = group;
        else if (uint.TryParse(selection, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var numericId) && _identities.ContainsKey(numericId))
            entries = [new GroupEntry(numericId, null)];
        else if (_directNames.TryGetValue(selection, out var id) && id is uint knownId)
            entries = [new GroupEntry(knownId, null)];
        else
            return new SelectionPlan(selection, [], [selection], [], [], false);

        var denied = BlockedFromGroups();
        var resolved = new List<ItemInfo>();
        var unknown = new List<string>();
        var blocked = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Id is not uint itemId)
            {
                unknown.Add(entry.Unresolved ?? "(unresolved catalogue entry)");
                continue;
            }
            if (DisabledItems.Contains(itemId.ToString(CultureInfo.InvariantCulture)) ||
                denied.Contains(itemId))
            {
                blocked.Add($"{itemId}: {_identities[itemId].DisplayName}");
                continue;
            }

            var item = VerifiedItem(itemId);
            if (item is null)
            {
                unknown.Add($"{_identities[itemId].DisplayName} (ID {itemId} absent or changed)");
                continue;
            }
            if (resolved.All(x => x.Id.Value != itemId)) resolved.Add(item);
            if (resolved.Count >= maxItems) break;
        }
        return new SelectionPlan(selection, resolved, unknown, blocked, [], false);
    }
}

internal sealed record SelectionPlan(string Selection, List<ItemInfo> Items,
    List<string> Unknown, List<string> Blocked, List<string> Ambiguous, bool IsEmpty);
