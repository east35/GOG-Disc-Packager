using System.Buffers.Binary;

namespace GogDisc.Core;

public static class IconFile
{
    public static void Create(string? sourcePath, string destinationPath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            var extension = Path.GetExtension(sourcePath);
            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, true);
                return;
            }

            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                WrapPng(sourcePath, destinationPath);
                return;
            }
        }

        WriteFallback(destinationPath);
    }

    private static void WrapPng(string sourcePath, string destinationPath)
    {
        var png = File.ReadAllBytes(sourcePath);
        if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4e || png[3] != 0x47)
            throw new InvalidDataException("The selected icon PNG is invalid.");

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        using var stream = File.Create(destinationPath);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
    }

    private static void WriteFallback(string destinationPath)
    {
        const int size = 32;
        const int pixelBytes = size * size * 4;
        const int maskBytes = size * 4;
        using var stream = File.Create(destinationPath);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(40 + pixelBytes + maskBytes);
        writer.Write(22);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            writer.Write((byte)(65 + x * 3));
            writer.Write((byte)(35 + y * 2));
            writer.Write((byte)18);
            writer.Write((byte)255);
        }
        writer.Write(new byte[maskBytes]);
    }
}
