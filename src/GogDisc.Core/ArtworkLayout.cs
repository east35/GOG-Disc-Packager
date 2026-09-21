namespace GogDisc.Core;

public readonly record struct PixelCrop(int X, int Y, int Width, int Height);

public static class ArtworkLayout
{
    public static PixelCrop HorizontalCrop(int width, int height, double targetAspect, double position)
    {
        if (width <= 0 || height <= 0 || targetAspect <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Artwork and target dimensions must be positive.");
        var cropWidth = Math.Min(width, Math.Max(1, (int)Math.Round(height * targetAspect)));
        var x = (int)Math.Round((width - cropWidth) * Math.Clamp(position, 0, 1));
        return new PixelCrop(x, 0, cropWidth, height);
    }
}
