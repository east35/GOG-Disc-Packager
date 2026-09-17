using System.Windows;
using GogDisc.Core;

namespace GogDisc.Packager;

public partial class DiscPlanWindow : Window
{
    public MediaSuggestion SelectedSuggestion => (MediaSuggestion)OptionsList.SelectedItem;

    public DiscPlanWindow(IReadOnlyList<MediaSuggestion> suggestions)
    {
        InitializeComponent();
        OptionsList.ItemsSource = suggestions;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsList.SelectedItem is null) return;
        DialogResult = true;
    }
}
