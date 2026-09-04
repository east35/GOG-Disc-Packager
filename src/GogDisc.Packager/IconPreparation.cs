using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GogDisc.Packager;

internal static class IconPreparation
{
    public static string? Prepare(string? source, out string? temporaryFile)
    {
        temporaryFile = null;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return source;
        if (Path.GetExtension(source).Equals(".ico", StringComparison.OrdinalIgnoreCase)) return source;

        using var input = File.OpenRead(source);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var scale = Math.Min(1d, 256d / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource image = frame;
        if (scale < 1d)
            image = new TransformedBitmap(frame, new ScaleTransform(scale, scale));

        temporaryFile = Path.Combine(Path.GetTempPath(), $"gog-disc-icon-{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(temporaryFile);
        encoder.Save(output);
        return temporaryFile;
    }
}
