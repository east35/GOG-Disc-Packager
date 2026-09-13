using System.Windows;
using GogDisc.Core;

namespace GogDisc.Packager;

public partial class MediaInventoryWindow : Window
{
    private readonly List<InventoryOption> _inventory;
    public IReadOnlyList<MediaInventoryItem> Inventory => _inventory
        .Where(item => item.Count > 0)
        .Select(item => new MediaInventoryItem(item.Media, item.Count))
        .ToList();

    public MediaInventoryWindow(IEnumerable<InventoryOption> inventory)
    {
        InitializeComponent();
        _inventory = inventory.Select(item => new InventoryOption(item.Media, item.Count)).ToList();
        InventoryList.ItemsSource = _inventory;
        UpdateSummary();
    }

    private void Decrease_Click(object sender, RoutedEventArgs e) => Change(sender, -1);
    private void Increase_Click(object sender, RoutedEventArgs e) => Change(sender, 1);

    private void Change(object sender, int amount)
    {
        if ((sender as FrameworkElement)?.Tag is not InventoryOption item) return;
        item.Count = Math.Clamp(item.Count + amount, 0, 99);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = _inventory.Sum(item => item.Count);
        var usable = _inventory.Sum(item => item.Count * item.Media.UsableBytes);
        SummaryText.Text = count == 0
            ? "No blank discs recorded."
            : $"{count} blank disc(s) · {usable / 1024d / 1024d / 1024d:N2} GiB usable";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear every saved blank-disc quantity?", "Clear inventory",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var item in _inventory) item.Count = 0;
        DialogResult = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
