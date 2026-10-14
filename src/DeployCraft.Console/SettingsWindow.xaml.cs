using System.Windows;
using System.Windows.Controls;

namespace DeployCraft.Console;

/// <summary>Préférences. Les identifiants ont leur propre fenêtre (Options › Identifiants).</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == Theme.Current);
    }

    // ---------- Interface ----------

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string choice } && choice != Theme.Current)
            Theme.Set(choice);
    }
}
