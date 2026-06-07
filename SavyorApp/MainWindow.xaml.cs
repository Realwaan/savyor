using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SavyorApp.Models;
using SavyorApp.Services;

namespace SavyorApp
{
    public partial class MainWindow : Window
    {
        private static readonly string AppBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string VaultDir = Path.Combine(AppBaseDir, "documents");
        private static readonly string FilesDir = Path.Combine(VaultDir, "files");
        private static readonly string CatalogPath = Path.Combine(VaultDir, "documents.json");

        private List<DocumentInfo> _documents = new List<DocumentInfo>();
        private string _currentFilter = "All";
        private DocumentInfo? _selectedDoc = null;
        private string _tempSelectedFilePath = "";
        private bool _isCatalogCorrupted = false;
        private string _activeViewingDocId = "";

        public MainWindow()
        {
            InitializeComponent();
            EnsureVaultDirectories();
            LoadCatalog();
            RefreshDisplay();
            
            // Set app version label
            LocalVerLabel.Text = $"App: v{GetAppVersion()}";
        }

        private string GetAppVersion()
        {
            try
            {
                string verPath = Path.Combine(AppBaseDir, "version.json");
                if (File.Exists(verPath))
                {
                    string json = File.ReadAllText(verPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("version", out var prop))
                    {
                        return prop.GetString() ?? "1.0.0";
                    }
                }
            }
            catch { }
            return "1.0.0";
        }

        private void EnsureVaultDirectories()
        {
            if (!Directory.Exists(VaultDir)) Directory.CreateDirectory(VaultDir);
            if (!Directory.Exists(FilesDir)) Directory.CreateDirectory(FilesDir);
        }

        private void LoadCatalog()
        {
            try
            {
                if (File.Exists(CatalogPath))
                {
                    string json = File.ReadAllText(CatalogPath);
                    var catalog = JsonSerializer.Deserialize<CatalogData>(json);
                    if (catalog != null && catalog.Documents != null)
                    {
                        _documents = catalog.Documents;
                    }
                }
            }
            catch (Exception ex)
            {
                _isCatalogCorrupted = true;
                MessageBox.Show($"Failed to load documents catalog: {ex.Message}\n\nSaving catalog is disabled to prevent overwriting/losing data.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveCatalog()
        {
            if (_isCatalogCorrupted)
            {
                MessageBox.Show("Saving database catalog is disabled because the document vault failed to load correctly. Please verify the integrity of the data files.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var catalog = new CatalogData { Documents = _documents };
                string json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
                
                string tempPath = CatalogPath + ".tmp";
                string backupPath = CatalogPath + ".bak";
                
                File.WriteAllText(tempPath, json);
                
                if (File.Exists(CatalogPath))
                {
                    File.Copy(CatalogPath, backupPath, true);
                }
                
                File.Move(tempPath, CatalogPath, overwrite: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save documents catalog: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshDisplay()
        {
            // Calculate stats
            TotalFilesText.Text = $"Total Files: {_documents.Count}";
            long totalBytes = _documents.Sum(d => d.SizeBytes);
            VaultSizeText.Text = $"Vault Size: {FormatBytes(totalBytes)}";

            // Filter lists
            var filtered = _documents.AsEnumerable();

            // Apply search filter
            string search = SearchBox.Text.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(d => 
                    d.Title.ToLowerInvariant().Contains(search) || 
                    d.Description.ToLowerInvariant().Contains(search) ||
                    d.Tags.Any(t => t.ToLowerInvariant().Contains(search))
                );
            }

            // Apply category filter
            if (_currentFilter != "All")
            {
                filtered = filtered.Where(d => MapCategory(d.Extension) == _currentFilter);
            }

            // Bind to UI list
            DocumentsList.ItemsSource = filtered.ToList();
            SectionTitleText.Text = _currentFilter == "All" ? "All Documents" : $"{_currentFilter.ToUpperInvariant()} Documents";
        }

        private string MapCategory(string ext)
        {
            ext = ext.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;

            if (ext == ".pdf") return "pdf";
            if (ext == ".docx" || ext == ".doc") return "docx";
            if (ext == ".pptx" || ext == ".ppt") return "pptx";
            if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" }.Contains(ext)) return "images";
            if (new[] { ".txt", ".md", ".json", ".csv", ".cs", ".xml" }.Contains(ext)) return "text";
            return "other";
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{(double)bytes / (1024 * 1024):F1} MB";
            if (bytes >= 1024) return $"{(double)bytes / 1024:F0} KB";
            return $"{bytes} B";
        }

        // ── Titlebar Controls ───────────────────────────────────────────
        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // ── Search & Filter triggers ───────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDisplay();
        }

        private void FilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _currentFilter = tag;
                RefreshDisplay();
            }
        }

        // ── Dialog Logic ───────────────────────────────────────────────

        private void ImportFile_Click(object sender, RoutedEventArgs e)
        {
            // Clear fields
            _tempSelectedFilePath = "";
            SelectedFilePathText.Text = "No file selected...";
            DocTitleInput.Text = "";
            DocDescriptionInput.Text = "";
            DocTagsInput.Text = "";

            ImportDialogOverlay.Visibility = Visibility.Visible;
        }

        private void CloseImportDialog_Click(object sender, RoutedEventArgs e)
        {
            ImportDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "All Files (*.*)|*.*|Documents (*.pdf;*.docx;*.pptx;*.txt;*.md)|*.pdf;*.docx;*.pptx;*.txt;*.md|Images (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif";
            
            if (openFileDialog.ShowDialog() == true)
            {
                _tempSelectedFilePath = openFileDialog.FileName;
                SelectedFilePathText.Text = Path.GetFileName(_tempSelectedFilePath);
                
                // Prefill title if empty
                if (string.IsNullOrWhiteSpace(DocTitleInput.Text))
                {
                    DocTitleInput.Text = Path.GetFileNameWithoutExtension(_tempSelectedFilePath);
                }
            }
        }

        private async void SaveImportedDocument_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_tempSelectedFilePath) || !File.Exists(_tempSelectedFilePath))
            {
                MessageBox.Show("Please select a valid file to import.", "File Not Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(DocTitleInput.Text))
            {
                MessageBox.Show("Please provide a title for the document.", "Title Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SaveImportedDocumentButton.IsEnabled = false;
                SaveImportedDocumentButton.Content = "Importing...";

                string id = Guid.NewGuid().ToString("N");
                string ext = Path.GetExtension(_tempSelectedFilePath);
                string originalName = Path.GetFileNameWithoutExtension(_tempSelectedFilePath);

                // Sanitize file name
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    originalName = originalName.Replace(c, '_');
                }

                if (originalName.Length > 50)
                {
                    originalName = originalName.Substring(0, 50);
                }

                string destinationName = $"{id}_{originalName}{ext}";
                string destinationPath = Path.Combine(FilesDir, destinationName);

                // Copy file to vault asynchronously
                string sourcePath = _tempSelectedFilePath;
                await Task.Run(() => File.Copy(sourcePath, destinationPath, true));

                // Extract size info
                long sizeBytes = new FileInfo(destinationPath).Length;

                // Parse tags
                var tags = DocTagsInput.Text
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                // Add extension as a default category tag
                string cleanExt = ext.Replace(".", "").ToUpperInvariant();
                if (!tags.Contains(cleanExt) && !string.IsNullOrEmpty(cleanExt))
                {
                    tags.Add(cleanExt);
                }

                var docInfo = new DocumentInfo
                {
                    Id = id,
                    Title = DocTitleInput.Text,
                    FileName = destinationName,
                    Extension = ext,
                    SizeBytes = sizeBytes,
                    DateAdded = DateTime.Now,
                    Description = DocDescriptionInput.Text,
                    Tags = tags
                };

                _documents.Add(docInfo);
                SaveCatalog();

                ImportDialogOverlay.Visibility = Visibility.Collapsed;
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import document: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveImportedDocumentButton.IsEnabled = true;
                SaveImportedDocumentButton.Content = "Import Document";
            }
        }

        // ── Viewer Logic ───────────────────────────────────────────────

        private void DocumentCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border card && card.DataContext is DocumentInfo doc)
            {
                OpenDocumentViewer(doc);
            }
        }

        private async void OpenDocumentViewer(DocumentInfo doc)
        {
            _selectedDoc = doc;
            _activeViewingDocId = doc.Id;
            string filePath = Path.Combine(FilesDir, doc.FileName);

            if (!File.Exists(filePath))
            {
                MessageBox.Show($"File could not be found in vault storage:\n{filePath}", "File Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ViewerDocTitle.Text = doc.Title;
            ViewerDocPath.Text = filePath;

            // Update metadata sidebar
            ViewerDocSize.Text = FormatBytes(doc.SizeBytes);
            ViewerDocDate.Text = doc.DateAdded.ToString("MM/dd/yyyy hh:mm tt");
            ViewerDocFormat.Text = doc.Extension.Replace(".", "").ToUpperInvariant() + " File";
            ViewerDocDescription.Text = string.IsNullOrWhiteSpace(doc.Description) ? "No description provided." : doc.Description;

            // Clear and rebuild tags panel
            ViewerTagsPanel.Children.Clear();
            foreach (var tag in doc.Tags)
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b")),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 6, 6)
                };

                var tagText = new TextBlock
                {
                    Text = tag,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8")),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold
                };

                tagBorder.Child = tagText;
                ViewerTagsPanel.Children.Add(tagBorder);
            }

            // Reset previews
            ImagePreviewer.Visibility = Visibility.Collapsed;
            TextPreviewer.Visibility = Visibility.Collapsed;
            WebPreviewer.Visibility = Visibility.Collapsed;
            PdfPreviewer.Visibility = Visibility.Collapsed;
            BinaryPreviewer.Visibility = Visibility.Collapsed;

            string category = MapCategory(doc.Extension);

            try
            {
                if (category == "images")
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.EndInit();
                    ImageViewerControl.Source = bitmap;
                    ImagePreviewer.Visibility = Visibility.Visible;
                }
                else if (category == "text")
                {
                    TextViewerControl.Text = "Loading content...";
                    TextPreviewer.Visibility = Visibility.Visible;
                    string text = await File.ReadAllTextAsync(filePath);
                    if (_activeViewingDocId == doc.Id)
                    {
                        TextViewerControl.Text = text;
                    }
                }
                else if (category == "docx")
                {
                    TextViewerControl.Text = "Parsing document content...";
                    TextPreviewer.Visibility = Visibility.Visible;
                    string text = await Task.Run(() => DocxReader.ReadText(filePath));
                    if (_activeViewingDocId == doc.Id)
                    {
                        TextViewerControl.Text = text;
                    }
                }
                else if (category == "pptx")
                {
                    TextViewerControl.Text = "Parsing slide presentation...";
                    TextPreviewer.Visibility = Visibility.Visible;
                    string text = await Task.Run(() => PptxReader.ReadText(filePath));
                    if (_activeViewingDocId == doc.Id)
                    {
                        TextViewerControl.Text = text;
                    }
                }
                else if (category == "pdf")
                {
                    // Show a dedicated PDF message with a direct open button
                    PdfPreviewer.Visibility = Visibility.Visible;
                }
                else
                {
                    BinaryPreviewer.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                TextViewerControl.Text = $"Error loading preview: {ex.Message}";
                TextPreviewer.Visibility = Visibility.Visible;
            }

            ViewerOverlay.Visibility = Visibility.Visible;
        }

        private void CloseViewer_Click(object sender, RoutedEventArgs e)
        {
            _activeViewingDocId = "";
            // Stop PDF playback or page loads by navigating away
            try
            {
                WebBrowserControl.Navigate("about:blank");
            }
            catch { }

            ViewerOverlay.Visibility = Visibility.Collapsed;
            _selectedDoc = null;
        }

        private void OpenExternally_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDoc == null) return;
            string filePath = Path.Combine(FilesDir, _selectedDoc.FileName);

            if (File.Exists(filePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch file on system: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EditDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDoc == null) return;

            // Fill input text fields
            EditTitleInput.Text = _selectedDoc.Title;
            EditDescriptionInput.Text = _selectedDoc.Description;
            EditTagsInput.Text = string.Join(", ", _selectedDoc.Tags.Where(t => t != _selectedDoc.Extension.Replace(".", "").ToUpperInvariant()));

            EditDialogOverlay.Visibility = Visibility.Visible;
        }

        private void CloseEditDialog_Click(object sender, RoutedEventArgs e)
        {
            EditDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveEditedDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDoc == null) return;

            if (string.IsNullOrWhiteSpace(EditTitleInput.Text))
            {
                MessageBox.Show("Please provide a title for the document.", "Title Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Parse tags
                var tags = EditTagsInput.Text
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                // Add extension as a default category tag
                string cleanExt = _selectedDoc.Extension.Replace(".", "").ToUpperInvariant();
                if (!tags.Contains(cleanExt) && !string.IsNullOrEmpty(cleanExt))
                {
                    tags.Add(cleanExt);
                }

                // Update properties
                _selectedDoc.Title = EditTitleInput.Text;
                _selectedDoc.Description = EditDescriptionInput.Text;
                _selectedDoc.Tags = tags;

                SaveCatalog();

                // Refresh UI labels in the viewer
                ViewerDocTitle.Text = _selectedDoc.Title;

                EditDialogOverlay.Visibility = Visibility.Collapsed;
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update document details: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDoc == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to permanently delete \"{_selectedDoc.Title}\" from the vault?\n\nThis will delete the file on disk as well.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string filePath = Path.Combine(FilesDir, _selectedDoc.FileName);

                    // Stop PDF playback or page loads by navigating away (since it locks the file)
                    try
                    {
                        WebBrowserControl.Navigate("about:blank");
                    }
                    catch { }

                    // Delete from filesystem
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    // Remove from list and save
                    _documents.Remove(_selectedDoc);
                    SaveCatalog();

                    // Hide viewer and refresh display
                    _activeViewingDocId = "";
                    ViewerOverlay.Visibility = Visibility.Collapsed;
                    _selectedDoc = null;
                    RefreshDisplay();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete document: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}