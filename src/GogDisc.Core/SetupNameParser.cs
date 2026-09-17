namespace GogDisc.Core;

public static class SetupNameParser
{
    public static string InferTitle(string setupPath)
    {
        var stem = Path.GetFileNameWithoutExtension(setupPath);
        if (stem.StartsWith("setup_", StringComparison.OrdinalIgnoreCase)) stem = stem["setup_".Length..];
        var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var versionIndex = Array.FindIndex(parts, part => part.Length > 0 && char.IsDigit(part[0]) && part.Contains('.'));
        if (versionIndex < 1) versionIndex = parts.Length;
        return string.Join(' ', parts.Take(versionIndex));
    }

    public static string CollectionGameTitle(string collectionTitle, string gameTitle, string setupPath)
    {
        var inferred = InferTitle(setupPath);
        var parent = collectionTitle.Trim();
        if (parent.EndsWith(" Collection", StringComparison.OrdinalIgnoreCase))
            parent = parent[..^" Collection".Length].TrimEnd();

        // A mixed, user-named collection does not describe each product; its setup name does.
        if (!SharesWord(parent, inferred)) return inferred;

        var child = gameTitle.Trim();
        if (child.Equals("Base", StringComparison.OrdinalIgnoreCase) ||
            child.Equals("Base Game", StringComparison.OrdinalIgnoreCase)) return parent;

        var normalizedParent = Normalize(parent);
        var normalizedChild = Normalize(child);
        if (normalizedChild.StartsWith(normalizedParent + " ", StringComparison.Ordinal) || normalizedChild == normalizedParent)
            return child;
        if (normalizedParent.StartsWith(normalizedChild + " ", StringComparison.Ordinal) || normalizedParent == normalizedChild)
            return parent;
        return $"{parent} {child}";
    }

    private static bool SharesWord(string first, string second)
    {
        var secondWords = Normalize(second).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return Normalize(first).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Length >= 3 && secondWords.Contains(word));
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray().AsSpan().ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
