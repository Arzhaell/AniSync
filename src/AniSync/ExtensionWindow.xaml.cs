using System.Diagnostics;
using System.Windows;
using AniSync.Core;
using AniSync.Ui;

namespace AniSync;

public partial class ExtensionWindow : Window
{
    public ExtensionWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);

        try
        {
            FolderBox.Text = ExtensionInstaller.Extract();
        }
        catch (Exception ex)
        {
            Log.Error("Extraction de l'extension", ex);
            FolderBox.Text = L.T($"Erreur : {ex.Message}", $"Error: {ex.Message}");
        }
    }

    void CopyFolder_Click(object sender, RoutedEventArgs e) => Copy(ExtensionInstaller.Folder, L.T("Chemin du dossier copié.", "Folder path copied."));
    void CopyOpera_Click(object sender, RoutedEventArgs e) => Copy("opera://extensions", L.T("Adresse copiée : colle-la dans la barre d'adresse d'Opera.", "Address copied: paste it in Opera's address bar."));
    void CopyEdge_Click(object sender, RoutedEventArgs e) => Copy("edge://extensions", L.T("Adresse copiée : colle-la dans la barre d'adresse d'Edge.", "Address copied: paste it in Edge's address bar."));

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ExtensionInstaller.Folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Ouverture du dossier de l'extension", ex); }
    }

    void Copy(string text, string message)
    {
        try
        {
            Clipboard.SetText(text);
            CopiedText.Text = message;
            CopiedText.Visibility = Visibility.Visible;
        }
        catch
        {
            // presse-papiers occupé
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
