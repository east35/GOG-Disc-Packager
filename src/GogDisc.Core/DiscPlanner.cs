namespace GogDisc.Core;

public static class DiscPlanner
{
    public const long DefaultReserveBytes = 256L * 1024 * 1024;
    public const long CdReserveBytes = 128L * 1024 * 1024;
    public const long Cd650CapacityBytes = 650_000_000L;
    public const long CdCapacityBytes = 700_000_000L;
    public const long Dvd5CapacityBytes = 4_700_000_000L;
    public const long Dvd9CapacityBytes = 8_500_000_000L;
    public const long Bd25CapacityBytes = 25_000_000_000L;
    public const long Bd50CapacityBytes = 50_000_000_000L;
    public const long Bd100CapacityBytes = 100_000_000_000L;
    public const long Bd128CapacityBytes = 128_000_000_000L;

    public static PackagePlan Create(SetupFamily family, long capacityBytes, long reserveBytes = DefaultReserveBytes)
    {
        ValidateCapacity(capacityBytes, reserveBytes);
        var usable = capacityBytes - reserveBytes;
        var total = family.InstallerBytes + family.ExtrasBytes;
        var count = Math.Max(1, (int)Math.Ceiling(total / (double)usable));
        var media = new OpticalMediaType("CUSTOM", MediaName(capacityBytes), capacityBytes, reserveBytes);
        return CreateMixed(family, Enumerable.Repeat(media, count).ToList());
    }

    public static PackagePlan CreateMixed(SetupFamily family, IReadOnlyList<OpticalMediaType> media)
    {
        if (media.Count == 0) throw new InvalidDataException("No media was selected.");
        foreach (var disc in media) ValidateCapacity(disc.CapacityBytes, disc.ReserveBytes);

        var discs = new List<PlannedDisc>();
        var mediaIndex = 0;
        var current = NewDisc(DiscRole.Installer);
        foreach (var file in family.InstallerFiles.OrderBy(file => file.Sequence)) AddFile(file, DiscRole.Installer);

        var requiredDiscCount = discs.Count;
        foreach (var extra in family.Extras.OrderBy(file => file.Sequence)) AddFile(extra, DiscRole.Extras);

        return new PackagePlan
        {
            Discs = discs,
            RequiredDiscCount = requiredDiscCount,
            InstallerBytes = family.InstallerBytes,
            ExtrasBytes = family.ExtrasBytes
        };

        void AddFile(SourcePackageFile file, DiscRole newDiscRole)
        {
            var remaining = file.Size;
            var offset = 0L;
            var fragments = new List<SourcePackageFile>();
            do
            {
                var usable = current.CapacityBytes - current.ReserveBytes;
                var available = usable - current.UsedBytes;
                if (available == 0)
                {
                    current = NewDisc(newDiscRole);
                    available = current.CapacityBytes - current.ReserveBytes;
                }

                var length = Math.Min(remaining, available);
                var fragment = file with { Size = length };
                fragment.SourceOffset = offset;
                fragment.SourceSize = file.Size;
                current.Files.Add(fragment);
                fragments.Add(fragment);
                offset += length;
                remaining -= length;
            } while (remaining > 0);

            for (var index = 0; index < fragments.Count; index++)
            {
                fragments[index].PartIndex = index + 1;
                fragments[index].PartCount = fragments.Count;
            }
        }

        PlannedDisc NewDisc(DiscRole role)
        {
            if (mediaIndex >= media.Count)
                throw new InvalidDataException("The selected mixed media does not have enough usable capacity for this package.");
            var selected = media[mediaIndex++];
            var disc = new PlannedDisc
            {
                Number = discs.Count + 1,
                Role = role,
                MediaName = selected.DisplayName,
                CapacityBytes = selected.CapacityBytes,
                ReserveBytes = selected.ReserveBytes
            };
            discs.Add(disc);
            return disc;
        }
    }

    private static void ValidateCapacity(long capacityBytes, long reserveBytes)
    {
        if (capacityBytes <= reserveBytes)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes), "Disc capacity must exceed the safety reserve.");
    }

    private static string MediaName(long capacityBytes) => $"{capacityBytes / 1_000_000_000d:0.###} GB media";
}
