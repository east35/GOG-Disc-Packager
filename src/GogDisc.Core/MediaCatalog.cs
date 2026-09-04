using System.Text.RegularExpressions;

namespace GogDisc.Core;

public sealed record OpticalMediaType(string Id, string DisplayName, long CapacityBytes, long ReserveBytes)
{
    public long UsableBytes => CapacityBytes - ReserveBytes;
}

public sealed record MediaInventoryItem(OpticalMediaType Media, int Count);

public sealed record MediaSuggestion(IReadOnlyList<OpticalMediaType> Discs, long PayloadBytes)
{
    public long CapacityBytes => Discs.Sum(disc => disc.CapacityBytes);
    public long UsableBytes => Discs.Sum(disc => disc.UsableBytes);
    public long UnusedBytes => UsableBytes - PayloadBytes;

    public string Summary => string.Join(", ", Discs
        .GroupBy(disc => disc.Id)
        .Select(group => $"{group.Count()} x {group.First().DisplayName}"));
}

public static class MediaCatalog
{
    public static readonly OpticalMediaType Cd650 = new("CD650", "CD-R 650 MB", DiscPlanner.Cd650CapacityBytes, DiscPlanner.CdReserveBytes);
    public static readonly OpticalMediaType Cd700 = new("CD700", "CD-R 700 MB", DiscPlanner.CdCapacityBytes, DiscPlanner.CdReserveBytes);
    public static readonly OpticalMediaType Dvd5 = new("DVD5", "DVD-5", DiscPlanner.Dvd5CapacityBytes, DiscPlanner.DefaultReserveBytes);
    public static readonly OpticalMediaType Dvd9 = new("DVD9", "DVD-9", DiscPlanner.Dvd9CapacityBytes, DiscPlanner.DefaultReserveBytes);
    public static readonly OpticalMediaType Bd25 = new("BD25", "BD-25", DiscPlanner.Bd25CapacityBytes, DiscPlanner.DefaultReserveBytes);
    public static readonly OpticalMediaType Bd50 = new("BD50", "BD-50", DiscPlanner.Bd50CapacityBytes, DiscPlanner.DefaultReserveBytes);
    public static readonly OpticalMediaType Bd100 = new("BD100", "BDXL-100", DiscPlanner.Bd100CapacityBytes, DiscPlanner.DefaultReserveBytes);
    public static readonly OpticalMediaType Bd128 = new("BD128", "BDXL-128", DiscPlanner.Bd128CapacityBytes, DiscPlanner.DefaultReserveBytes);

    public static IReadOnlyList<OpticalMediaType> All { get; } = [Cd650, Cd700, Dvd5, Dvd9, Bd25, Bd50, Bd100, Bd128];

    public static IReadOnlyList<MediaInventoryItem> ParseInventory(string value)
    {
        var result = new List<MediaInventoryItem>();
        foreach (var token in value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(token, @"^(?<name>.+?)(?:\s*[x*]\s*(?<count>\d+))?$", RegexOptions.IgnoreCase);
            if (!match.Success) throw new FormatException($"Invalid media inventory entry: {token}");
            var media = Find(match.Groups["name"].Value) ??
                throw new FormatException($"Unknown media type '{match.Groups["name"].Value.Trim()}'. Use CD650, CD700, DVD5, DVD9, BD25, BD50, BD100, or BD128.");
            var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value) : 1;
            if (count is < 1 or > 99) throw new FormatException("Media quantities must be between 1 and 99.");
            result.Add(new MediaInventoryItem(media, count));
        }
        if (result.Count == 0) throw new FormatException("Enter at least one available disc.");
        return result.GroupBy(item => item.Media.Id)
            .Select(group => new MediaInventoryItem(group.First().Media, Math.Min(99, group.Sum(item => item.Count))))
            .ToList();
    }

    public static MediaSuggestion Suggest(long payloadBytes, IReadOnlyList<MediaInventoryItem> inventory)
    {
        if (payloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        var states = new Dictionary<long, List<OpticalMediaType>> { [0] = [] };
        foreach (var item in inventory)
        {
            var usefulCount = Math.Min(item.Count, (int)Math.Ceiling(payloadBytes / (double)item.Media.UsableBytes) + 1);
            for (var unit = 0; unit < usefulCount; unit++)
            {
                foreach (var state in states.ToArray())
                {
                    var usable = state.Key + item.Media.UsableBytes;
                    var discs = new List<OpticalMediaType>(state.Value) { item.Media };
                    if (!states.TryGetValue(usable, out var existing) || IsBetter(discs, existing)) states[usable] = discs;
                }
            }
        }

        var best = states.Where(state => state.Key >= payloadBytes)
            .Select(state => state.Value)
            .OrderBy(discs => discs.Sum(disc => disc.CapacityBytes))
            .ThenBy(discs => discs.Count)
            .ThenBy(discs => discs.Sum(disc => disc.UsableBytes) - payloadBytes)
            .FirstOrDefault() ?? throw new InvalidDataException("The available media does not have enough usable capacity for this package.");
        return new MediaSuggestion(best.OrderByDescending(disc => disc.CapacityBytes).ToList(), payloadBytes);
    }

    private static bool IsBetter(IReadOnlyList<OpticalMediaType> candidate, IReadOnlyList<OpticalMediaType> existing) =>
        candidate.Sum(disc => disc.CapacityBytes) < existing.Sum(disc => disc.CapacityBytes) ||
        candidate.Sum(disc => disc.CapacityBytes) == existing.Sum(disc => disc.CapacityBytes) && candidate.Count < existing.Count;

    private static OpticalMediaType? Find(string value)
    {
        var normalized = Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]+", "");
        return normalized switch
        {
            "CD650" or "CDR650" => Cd650,
            "CD700" or "CDR700" or "CD" or "CDR" => Cd700,
            "DVD5" or "DVD47GB" => Dvd5,
            "DVD9" or "DVD85GB" => Dvd9,
            "BD25" or "BDR25" => Bd25,
            "BD50" or "BDR50" => Bd50,
            "BD100" or "BDXL100" => Bd100,
            "BD128" or "BDXL128" => Bd128,
            _ => null
        };
    }
}
