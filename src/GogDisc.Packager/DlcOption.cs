using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace GogDisc.Packager;

/// <summary>One owned add-on, and the artwork its disc carries when the set is split one product per disc.</summary>
public sealed class DlcOption : INotifyPropertyChanged
{
    private bool _selected = true;
    private Visibility _artVisibility = Visibility.Collapsed;
    private string? _coverImage;
    private string? _backgroundImage;
    private string? _iconImage;

    public required string ProductId { get; init; }
    public required string Title { get; init; }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Visibility ArtVisibility
    {
        get => _artVisibility;
        set => Set(ref _artVisibility, value);
    }

    public string? CoverImage
    {
        get => _coverImage;
        set { Set(ref _coverImage, value); Notify(nameof(ArtSummary)); }
    }

    public string? BackgroundImage
    {
        get => _backgroundImage;
        set { Set(ref _backgroundImage, value); Notify(nameof(ArtSummary)); }
    }

    public string? IconImage
    {
        get => _iconImage;
        set { Set(ref _iconImage, value); Notify(nameof(ArtSummary)); }
    }

    public string ArtSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_backgroundImage)) parts.Add("background " + Path.GetFileName(_backgroundImage));
            if (!string.IsNullOrWhiteSpace(_coverImage)) parts.Add("cover " + Path.GetFileName(_coverImage));
            if (!string.IsNullOrWhiteSpace(_iconImage)) parts.Add("icon " + Path.GetFileName(_iconImage));
            return parts.Count == 0 ? "No disc art chosen — this disc reuses the generic icon." : string.Join(", ", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
