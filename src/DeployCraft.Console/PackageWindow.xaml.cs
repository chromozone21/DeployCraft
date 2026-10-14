using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployCraft.Server.Data;
using Microsoft.Win32;

namespace DeployCraft.Console;

/// <summary>Éditeur de paquet : propriétés et étapes à gauche, détail de l'élément sélectionné à droite.</summary>
public partial class PackageWindow : Window
{
    private readonly TreeViewItem _propertiesNode;
    private readonly TreeViewItem _stepsNode;
    private string _savedSnapshot;
    private bool _loadingStep;

    /// <summary>Paquet tel qu'enregistré en dernier (valide si <see cref="Saved"/>).</summary>
    public Package Package { get; private set; }
    public bool Saved { get; private set; }

    public PackageWindow(Package package)
    {
        InitializeComponent();
        Package = Clone(package);
        _savedSnapshot = Snapshot();

        NameBox.Text = Package.Name;
        DescriptionBox.Text = Package.Description;
        foreach (var (mode, text) in new[]
                 {
                     (RunAsMode.DeployUser, "Compte de déploiement"), (RunAsMode.LocalSystem, "Système local"),
                     (RunAsMode.LoggedOnUser, "Utilisateur connecté"),
                 })
            RunAsBox.Items.Add(new ComboBoxItem { Content = text, Tag = mode, IsSelected = Package.RunAs == mode });
        foreach (var (mode, text) in new[] { (CopyMode.Push, "Push : le serveur copie les fichiers"), (CopyMode.Pull, "Pull : le poste lit le partage") })
            CopyModeBox.Items.Add(new ComboBoxItem { Content = text, Tag = mode, IsSelected = Package.CopyMode == mode });

        _propertiesNode = new TreeViewItem { Header = Ui.Header(Ui.IconProperties, "Propriétés"), Tag = "properties" };
        _stepsNode = new TreeViewItem { Header = Ui.Header(Ui.IconSteps, "Étapes"), IsExpanded = true };
        Tree.Items.Add(_propertiesNode);
        Tree.Items.Add(_stepsNode);
        RebuildSteps(select: null);
        _propertiesNode.IsSelected = true;

        UpdateTitle();
        SetupContextMenus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, e); e.Handled = true; }
        };
        Loaded += (_, _) => { if (Package.Id == 0) NameBox.Focus(); };
    }

    public static PackageStep NewStep(StepType type) => new()
    {
        Type = type,
        SuccessCodes = type == StepType.Install ? Defaults.SuccessCodes : "0",
    };

    private PackageStep? SelectedStep => (Tree.SelectedItem as TreeViewItem)?.Tag as PackageStep;

    // ---------- Arbre ----------

    private void RebuildSteps(PackageStep? select)
    {
        _stepsNode.Items.Clear();
        for (var i = 0; i < Package.Steps.Count; i++)
        {
            var step = Package.Steps[i];
            var node = new TreeViewItem { Header = StepHeader(step, i), Tag = step };
            _stepsNode.Items.Add(node);
            if (step == select) node.IsSelected = true;
        }
        UpdateButtons();
    }

    private static StackPanel StepHeader(PackageStep step, int index) =>
        Ui.Header(Ui.StepIcon(step.Type), string.IsNullOrWhiteSpace(step.Name)
            ? $"Étape {index + 1} ({MainWindow.StepTypeText(step.Type)})"
            : $"{index + 1}. {step.Name}");

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SelectedStep is { } step) ShowStep(step);
        else if (Tree.SelectedItem == _stepsNode) ShowStepsOverview();
        else ShowOnly(PropertiesView);
        UpdateButtons();
    }

    private void ShowOnly(FrameworkElement view)
    {
        foreach (var v in new FrameworkElement[] { PropertiesView, StepsOverview, StepView })
            v.Visibility = v == view ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStepsOverview()
    {
        StepsGrid.ItemsSource = Package.Steps.Select((s, i) => new StepRow(i + 1, s.Name, MainWindow.StepTypeText(s.Type), s.Type switch
        {
            StepType.Install => $"{s.File} {s.Arguments}".Trim(),
            _ => (s.Command ?? "").Split('\n')[0].Trim(),
        })).ToList();
        ShowOnly(StepsOverview);
    }

    private void StepsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StepsGrid.SelectedItem is StepRow row) SelectStep(Package.Steps[row.Number - 1]);
    }

    private void SelectStep(PackageStep step)
    {
        foreach (TreeViewItem node in _stepsNode.Items)
            if (node.Tag == step) { node.IsSelected = true; node.BringIntoView(); }
    }

    // ---------- Menus clic droit ----------

    private void SetupContextMenus()
    {
        Ui.SelectOnRightClick(Tree);
        Ui.SelectOnRightClick(StepsGrid);

        Ui.MenuEntry[] AddEntries() =>
        [
            new("Nouvelle étape : Installer", () => AddInstall_Click(this, new())),
            new("Nouvelle étape : Commande", () => AddCommand_Click(this, new())),
            new("Nouvelle étape : PowerShell", () => AddPowerShell_Click(this, new())),
        ];

        IEnumerable<Ui.MenuEntry> StepEntries(PackageStep step)
        {
            var i = Package.Steps.IndexOf(step);
            return
            [
                .. AddEntries(),
                Ui.Separator,
                new("Monter", () => { SelectStep(step); Move(-1); }, i > 0),
                new("Descendre", () => { SelectStep(step); Move(1); }, i < Package.Steps.Count - 1),
                new("Dupliquer", () => { SelectStep(step); Duplicate_Click(this, new()); }),
                Ui.Separator,
                new("Supprimer", () => { SelectStep(step); DeleteStep_Click(this, new()); }, Gesture: "Suppr"),
            ];
        }

        Ui.ContextMenu(Tree, () => SelectedStep is { } step ? StepEntries(step)
            : Tree.SelectedItem == _stepsNode ? AddEntries()
            : []);

        Ui.ContextMenu(StepsGrid, () => StepsGrid.SelectedItem is StepRow row
            ? [new("Modifier", () => SelectStep(Package.Steps[row.Number - 1])), Ui.Separator, .. StepEntries(Package.Steps[row.Number - 1])]
            : AddEntries());

        Tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && SelectedStep != null) { DeleteStep_Click(this, e); e.Handled = true; }
        };
    }

    private void UpdateButtons()
    {
        var step = SelectedStep;
        var index = step == null ? -1 : Package.Steps.IndexOf(step);
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < Package.Steps.Count - 1;
        DuplicateButton.IsEnabled = step != null;
        DeleteButton.IsEnabled = step != null;
    }

    // ---------- Détail d'une étape ----------

    private void ShowStep(PackageStep step)
    {
        _loadingStep = true;
        StepView.DataContext = step;
        ShowOnly(StepView);

        var install = step.Type == StepType.Install;
        foreach (UIElement el in new UIElement[] { FileLabel, FileBox, BrowseButton, FileHint, ArgsLabel, ArgsBox, SilentHint, IncludeDirBox })
            el.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        CommandLabel.Visibility = CommandBox.Visibility = install ? Visibility.Collapsed : Visibility.Visible;
        CommandLabel.Text = step.Type == StepType.PowerShell ? "Script" : "Commande";
        CommandBox.MinHeight = step.Type == StepType.PowerShell ? 220 : 26;
        CommandBox.TextWrapping = step.Type == StepType.PowerShell ? TextWrapping.NoWrap : TextWrapping.Wrap;

        ErrorModeBox.SelectedIndex = step.ErrorMode == ErrorMode.Continue ? 1 : 0;
        UpdateSilentHint();
        _loadingStep = false;
    }

    private void StepName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Tree.SelectedItem is TreeViewItem { Tag: PackageStep step } node)
            node.Header = StepHeader(step, Package.Steps.IndexOf(step));
    }

    private void InstallFields_Changed(object sender, TextChangedEventArgs e) => UpdateSilentHint();

    /// <summary>Un .exe sans paramètre silencieux attend un clic qui n'arrivera jamais : on prévient.</summary>
    private void UpdateSilentHint()
    {
        if (SelectedStep is not { Type: StepType.Install } step) return;
        var ext = Path.GetExtension(step.File ?? "").ToLowerInvariant();
        if (ext == ".exe" && string.IsNullOrWhiteSpace(step.Arguments))
        {
            SilentHint.Foreground = (System.Windows.Media.Brush)FindResource("KoBrush");
            SilentHint.Text = "⚠ Aucun paramètre silencieux : l'installation risque de bloquer jusqu'au délai max. " +
                              "Courants : /S (NSIS), /VERYSILENT /NORESTART (Inno Setup), /quiet /norestart, /s /v\"/qn\" (InstallShield).";
        }
        else
        {
            SilentHint.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            SilentHint.Text = ext switch
            {
                ".msi" or ".msp" => "MSI : /qn /norestart sont ajoutés automatiquement.",
                ".msu" => "MSU : /quiet /norestart sont ajoutés automatiquement.",
                _ => "",
            };
        }
    }

    private void ErrorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingStep && SelectedStep is { } step)
            step.ErrorMode = ErrorModeBox.SelectedIndex == 1 ? ErrorMode.Continue : ErrorMode.Stop;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var dialog = new OpenFileDialog
        {
            Title = "Fichier d'installation",
            Filter = "Installeurs|*.exe;*.msi;*.msp;*.msu;*.ps1;*.bat;*.cmd|Tous les fichiers|*.*",
        };
        if (!string.IsNullOrEmpty(step.File) && Directory.Exists(Path.GetDirectoryName(step.File)))
            dialog.InitialDirectory = Path.GetDirectoryName(step.File);
        if (dialog.ShowDialog(this) != true) return;

        step.File = dialog.FileName;
        FileBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(step.Name))
        {
            step.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            StepView.DataContext = null;
            ShowStep(step);
            StepName_TextChanged(this, null!);
        }
    }

    // ---------- Propriétés ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Package.Name = NameBox.Text;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = string.IsNullOrWhiteSpace(Package.Name) ? "Nouveau paquet" : $"{Package.Name} — Paquet";

    // ---------- Barre d'outils ----------

    private void NewStep_Click(object sender, RoutedEventArgs e)
    {
        NewStepButton.ContextMenu.PlacementTarget = NewStepButton;
        NewStepButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        NewStepButton.ContextMenu.IsOpen = true;
    }

    private void AddInstall_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Install));
    private void AddCommand_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.Command));
    private void AddPowerShell_Click(object sender, RoutedEventArgs e) => AddStep(NewStep(StepType.PowerShell));

    private void AddStep(PackageStep step)
    {
        // Insérée après l'étape sélectionnée, sinon à la fin.
        var index = SelectedStep is { } current ? Package.Steps.IndexOf(current) + 1 : Package.Steps.Count;
        Package.Steps.Insert(index, step);
        RebuildSteps(step);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        if (SelectedStep is not { } step) return;
        var i = Package.Steps.IndexOf(step);
        var j = i + delta;
        if (j < 0 || j >= Package.Steps.Count) return;
        (Package.Steps[i], Package.Steps[j]) = (Package.Steps[j], Package.Steps[i]);
        RebuildSteps(step);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var copy = JsonSerializer.Deserialize<PackageStep>(JsonSerializer.Serialize(step, Api.Json), Api.Json)!;
        copy.Name = string.IsNullOrWhiteSpace(step.Name) ? "" : step.Name + " (copie)";
        AddStep(copy);
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { } step) return;
        var i = Package.Steps.IndexOf(step);
        Package.Steps.Remove(step);
        RebuildSteps(Package.Steps.Count > 0 ? Package.Steps[Math.Min(i, Package.Steps.Count - 1)] : null);
        if (Package.Steps.Count == 0) _propertiesNode.IsSelected = true;
    }

    private void RunAsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RunAsBox.SelectedItem is ComboBoxItem { Tag: RunAsMode mode }) Package.RunAs = mode;
    }

    private void CopyModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CopyModeBox.SelectedItem is ComboBoxItem { Tag: CopyMode mode }) Package.CopyMode = mode;
    }

    // ---------- Enregistrement ----------

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        Package.Name = NameBox.Text.Trim();
        Package.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        try
        {
            var selected = SelectedStep == null ? -1 : Package.Steps.IndexOf(SelectedStep);
            Package = await Api.SavePackage(Package);
            Saved = true;
            _savedSnapshot = Snapshot();
            RebuildSteps(selected >= 0 && selected < Package.Steps.Count ? Package.Steps[selected] : null);
            if (selected < 0) _propertiesNode.IsSelected = true;
            UpdateTitle();
            return true;
        }
        catch (ApiException ex)
        {
            Ui.Error(this, ex);
            return false;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        Package.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        if (e.Cancel || Snapshot() == _savedSnapshot) return;

        var answer = MessageBox.Show(this, "Enregistrer les modifications du paquet ?", "Paquet",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No) return;

        e.Cancel = true; // on rouvre la question après l'enregistrement asynchrone
        if (answer == MessageBoxResult.Yes && await SaveAsync())
        {
            _savedSnapshot = Snapshot();
            Close();
        }
    }

    private string Snapshot() => JsonSerializer.Serialize(Package, Api.Json);

    private static Package Clone(Package p) =>
        JsonSerializer.Deserialize<Package>(JsonSerializer.Serialize(p, Api.Json), Api.Json)!;
}
