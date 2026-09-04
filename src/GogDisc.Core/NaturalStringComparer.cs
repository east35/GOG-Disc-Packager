using System.Globalization;

namespace GogDisc.Core;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ix = 0;
        var iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                var sx = ix;
                var sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var nx = long.Parse(x.AsSpan(sx, ix - sx), CultureInfo.InvariantCulture);
                var ny = long.Parse(y.AsSpan(sy, iy - sy), CultureInfo.InvariantCulture);
                var numeric = nx.CompareTo(ny);
                if (numeric != 0) return numeric;
            }
            else
            {
                var character = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (character != 0) return character;
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
