namespace NFCBets.Classical.Constants;

public static class ArenaConstants
{
    public static readonly Dictionary<int, string> ArenaNames = new()
    {
        { 1, "Shipwreck" },
        { 2, "Lagoon" },
        { 3, "Treasure Island" },
        { 4, "Hidden Cove" },
        { 5, "Harpoon Harry's" }
    };

    public static string GetArenaName(int arenaId)
    {
        return ArenaNames.TryGetValue(arenaId, out var name) ? name : $"Arena {arenaId}";
    }

    public static int? GetArenaId(string arenaName)
    {
        var match = ArenaNames.FirstOrDefault(kvp =>
            kvp.Value.Equals(arenaName, StringComparison.OrdinalIgnoreCase));
        return match.Key != 0 ? match.Key : null;
    }
}