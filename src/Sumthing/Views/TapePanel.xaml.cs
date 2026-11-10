using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>The paper roll: every step of every Standard calculation, with a note column, like an adding machine.</summary>
public partial class TapePanel : UserControl
{
    public ObservableCollection<TapeEntry> Entries { get; } = new();

    public TapePanel()
    {
        InitializeComponent();
        TapeList.ItemsSource = Entries;
        Entries.CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();
    }

    public void ApplyLook() { }

    public void Load(IEnumerable<TapeEntry> entries)
    {
        Entries.Clear();
        foreach (var e in entries) Entries.Add(e);
        ScrollToEnd();
    }

    /// <summary>Adds a line; null means "the last line changed" (operator swapped).</summary>
    public void Add(TapeEntry e)
    {
        if (e == null) { TapeList.Items.Refresh(); return; }
        if (e.IsDivider && (Entries.Count == 0 || Entries[^1].IsDivider)) return;
        Entries.Add(e);
        while (Entries.Count > 2000) Entries.RemoveAt(0);
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (Entries.Count > 0) TapeList.ScrollIntoView(Entries[^1]);
    }

    private void UpdateEmpty()
    {
        EmptyText.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public string AsText()
    {
        var sb = new StringBuilder();
        foreach (var e in Entries)
        {
            if (e.IsDivider) { sb.AppendLine(new string('-', 36)); continue; }
            if (e.IsTotal) sb.AppendLine(new string(' ', 14) + new string('=', 18));
            string note = (e.Note ?? "").Trim();
            if (note.Length > 14) note = note[..13] + "…";
            sb.AppendLine($"{note,-14}{e.Number,18} {e.Symbol}".TrimEnd());
        }
        return sb.ToString();
    }

    public void CopyAll()
    {
        try { Clipboard.SetText(AsText()); } catch { }
    }

    public void Clear() => Entries.Clear();

    public bool SaveTo(string path)
    {
        try
        {
            File.WriteAllText(path, $"Sumthing tape - {DateTime.Now:f}\r\n\r\n" + AsText().Replace("\n", "\r\n").Replace("\r\r", "\r"), new UTF8Encoding(true));
            return true;
        }
        catch { return false; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAll();
    private void Clear_Click(object sender, RoutedEventArgs e) => Clear();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save tape",
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"Sumthing tape {DateTime.Now:yyyy-MM-dd}.txt",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true && !SaveTo(dlg.FileName))
            MessageBox.Show(Window.GetWindow(this), "The tape couldn't be saved there.", "Sumthing", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var pd = new PrintDialog();
        if (pd.ShowDialog() != true) return;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            PagePadding = new Thickness(60),
            ColumnWidth = double.PositiveInfinity,
        };
        doc.Blocks.Add(new Paragraph(new Run($"Sumthing tape - {DateTime.Now:f}")) { FontWeight = FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run(AsText())));
        pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Sumthing tape");
    }
}
