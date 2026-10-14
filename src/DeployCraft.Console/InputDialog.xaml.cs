using System.Windows;

namespace DeployCraft.Console;

/// <summary>Petite fenêtre de saisie d'un texte (renommer…).</summary>
public partial class InputDialog : Window
{
    public string Value => Input.Text.Trim();

    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    /// <summary>Affiche la fenêtre ; renvoie le texte saisi, ou null si annulé ou vide.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var dialog = new InputDialog(title, prompt, initial) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Value.Length > 0 ? dialog.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
