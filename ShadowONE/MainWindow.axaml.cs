using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ShadowONE.Models;
using ShadowONE.Services;
using ShadowONE.ViewModels;

namespace ShadowONE
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly OneFileService _oneFileService;
        private string? _currentFilePath;
        private string? _lastDialogFolder;
        private bool _suppressCloseCheck;

        private static readonly DataFormat<string> InternalDragFormat =
            DataFormat.CreateInProcessFormat<string>("ShadowONE-Internal-Reorder");

        public MainWindow()
        {
            InitializeComponent();
            WindowsTitleBarHelper.SetDarkTitleBar(this);
            _viewModel = new MainWindowViewModel();
            _oneFileService = new OneFileService();
            DataContext = _viewModel;
            
            this.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

            FilesListBox.AddHandler(InputElement.PointerPressedEvent,
                FilesListBox_PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            this.AddHandler(DragDrop.DropEvent, Window_Drop);

            LoadIcon();
            UpdateWindowTitle();
        }

        private void LoadIcon()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var iconPath = Path.Combine(exeDir, "Assets", "logo.ico");
                    if (File.Exists(iconPath))
                    {
                        using var stream = File.OpenRead(iconPath);
                        Icon = new WindowIcon(stream);
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            if (!_suppressCloseCheck && _oneFileService.IsFileOpen && _oneFileService.HasUnsavedChanges)
            {
                e.Cancel = true;
                _suppressCloseCheck = true;
                var confirm = await ShowConfirmDialog("Unsaved Changes",
                    $"There are unsaved changes.{Environment.NewLine}{Environment.NewLine}Close anyway?");
                if (confirm)
                {
                    Close();
                }
                else
                {
                    _suppressCloseCheck = false;
                }
                return;
            }

            base.OnClosing(e);
        }

        public async void OpenOneFile(string filePath)
        {
            try
            {
                _currentFilePath = filePath;
                _lastDialogFolder = null;
                var entries = _oneFileService.OpenFile(filePath);
                _viewModel.LoadFiles(entries);
                _viewModel.CurrentFilePath = filePath;
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                await ShowError($"Error opening file: {ex.Message}");
            }
        }

        private void UpdateWindowTitle()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                Title = $"ShadowONE {VersionInfo.Version}";
                return;
            }

            var archiveType = _oneFileService.ArchiveTypeName ?? "Unknown";
            var fileCount = _viewModel.FilteredFiles.Count;
            var prefix = _oneFileService.HasUnsavedChanges ? "* " : "";
            Title = $"{prefix}{Path.GetFileName(_currentFilePath)} | {archiveType} | {_oneFileService.ArchiveRwVersion} | Files: {fileCount}";
        }

        private async Task<IStorageFolder?> TryGetStorageFolderFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                return null;

            return await StorageProvider.TryGetFolderFromPathAsync(dir);
        }

        private async Task<IStorageFolder?> GetSuggestedStartLocation()
        {
            if (!string.IsNullOrEmpty(_lastDialogFolder))
                return await StorageProvider.TryGetFolderFromPathAsync(_lastDialogFolder);

            return await TryGetStorageFolderFromPath(_currentFilePath);
        }

        private async void OpenFile_Click(object? sender, RoutedEventArgs e)
        {
            var startFolder = await GetSuggestedStartLocation();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open ONE File",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = [new FilePickerFileType("ONE Files") { Patterns = ["*.one", "*.ONE"] }]
            });

            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                _lastDialogFolder = Path.GetDirectoryName(path);
                OpenOneFile(path);
            }
        }

        private async void SaveFile_Click(object? sender, RoutedEventArgs e)
        {
            if (_oneFileService.IsFileOpen && !string.IsNullOrEmpty(_currentFilePath))
            {
                try
                {
                    _oneFileService.SaveChanges();
                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    UpdateWindowTitle();
                }
                catch (Exception ex)
                {
                    await ShowError($"Error saving file: {ex.Message}");
                }
            }
        }

        private async void SaveAsFile_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save ONE File As",
                DefaultExtension = "one",
                SuggestedStartLocation = startFolder,
                FileTypeChoices = [new FilePickerFileType("ONE Files") { Patterns = ["*.one"] }]
            });

            if (file != null)
            {
                try
                {
                    var newPath = file.Path.LocalPath;
                    _lastDialogFolder = Path.GetDirectoryName(newPath);
                    _oneFileService.SaveChangesAs(newPath);
                    _currentFilePath = newPath;
                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    _viewModel.CurrentFilePath = _currentFilePath;
                    UpdateWindowTitle();
                }
                catch (Exception ex)
                {
                    await ShowError($"Error saving file: {ex.Message}");
                }
            }
        }

        private async void ExtractAll_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Extract Directory",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (folder.Count > 0)
            {
                try
                {
                    _lastDialogFolder = folder[0].Path.LocalPath;
                    _oneFileService.ExtractAllFiles(folder[0].Path.LocalPath);
                }
                catch (Exception ex)
                {
                    await ShowError($"Error extracting files: {ex.Message}");
                }
            }
        }

        private async void AddFiles_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Files to Archive",
                AllowMultiple = true,
                SuggestedStartLocation = startFolder
            });

            if (files.Count > 0)
            {
                try
                {
                    _lastDialogFolder = Path.GetDirectoryName(files[0].Path.LocalPath);
                    foreach (var file in files)
                    {
                        _oneFileService.AddFile(file.Path.LocalPath);
                    }

                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    UpdateWindowTitle();
                }
                catch (Exception ex)
                {
                    await ShowError($"Error adding files: {ex.Message}");
                }
            }
        }

        private async void Rename_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            var newName = await ShowInputDialog("Rename File", "Enter new file name:", selectedFile.FileName);
            if (!string.IsNullOrEmpty(newName) && newName != selectedFile.FileName)
            {
                try
                {
                    _oneFileService.RenameFile(selectedFile.FileName, newName);
                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    UpdateWindowTitle();
                    
                    var newIndex = -1;
                    for (int i = 0; i < _viewModel.FilteredFiles.Count; i++)
                    {
                        if (_viewModel.FilteredFiles[i].FileName == newName)
                        {
                            newIndex = i;
                            break;
                        }
                    }
                    
                    if (newIndex >= 0)
                    {
                        FilesListBox.SelectedIndex = newIndex;
                        FilesListBox.ScrollIntoView(newIndex);
                        FilesListBox.ContainerFromIndex(newIndex)?.Focus();
                    }
                }
                catch (Exception ex)
                {
                    await ShowError($"Error renaming file: {ex.Message}");
                }
            }
        }

        private async void Replace_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Replacement File",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (files.Count > 0)
            {
                var fileName = selectedFile.FileName;
                _lastDialogFolder = Path.GetDirectoryName(files[0].Path.LocalPath);
                try
                {
                    _oneFileService.ReplaceFile(selectedFile, files[0].Path.LocalPath);
                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    UpdateWindowTitle();
                    
                    var newIndex = -1;
                    for (int i = 0; i < _viewModel.FilteredFiles.Count; i++)
                    {
                        if (_viewModel.FilteredFiles[i].FileName == fileName)
                        {
                            newIndex = i;
                            break;
                        }
                    }
                    
                    if (newIndex >= 0)
                    {
                        FilesListBox.SelectedIndex = newIndex;
                        FilesListBox.ScrollIntoView(newIndex);
                        FilesListBox.ContainerFromIndex(newIndex)?.Focus();
                    }
                }
                catch (Exception ex)
                {
                    await ShowError($"Error replacing file: {ex.Message}");
                }
            }
        }

        private async void Extract_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Extracted File",
                SuggestedStartLocation = startFolder,
                SuggestedFileName = selectedFile.FileName,
                DefaultExtension = Path.GetExtension(selectedFile.FileName)
            });

            if (file != null)
            {
                try
                {
                    _lastDialogFolder = Path.GetDirectoryName(file.Path.LocalPath);
                    var data = _oneFileService.ExtractFile(selectedFile);
                    await File.WriteAllBytesAsync(file.Path.LocalPath, data);
                }
                catch (Exception ex)
                {
                    await ShowError($"Error extracting file: {ex.Message}");
                }
            }
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            try
            {
                _oneFileService.DeleteFile(selectedFile);
                var entries = _oneFileService.GetFileEntries();
                _viewModel.LoadFiles(entries);
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                await ShowError($"Error deleting file: {ex.Message}");
            }
        }

        private void MoveUp_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                return;
            }

            if (_oneFileService.MoveFileUp(selectedFile))
            {
                RefreshFileListAndSelect(selectedFile.FileName);
                UpdateWindowTitle();
            }
        }

        private void MoveDown_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                return;
            }

            if (_oneFileService.MoveFileDown(selectedFile))
            {
                RefreshFileListAndSelect(selectedFile.FileName);
                UpdateWindowTitle();
            }
        }

        private void RefreshFileListAndSelect(string fileName)
        {
            var entries = _oneFileService.GetFileEntries();
            _viewModel.LoadFiles(entries);
            
            var newIndex = -1;
            for (int i = 0; i < _viewModel.FilteredFiles.Count; i++)
            {
                if (_viewModel.FilteredFiles[i].FileName == fileName)
                {
                    newIndex = i;
                    break;
                }
            }
            
            if (newIndex >= 0)
            {
                FilesListBox.SelectedIndex = newIndex;
                FilesListBox.ScrollIntoView(newIndex);
                FilesListBox.ContainerFromIndex(newIndex)?.Focus();
            }
        }

        private async void FilesListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                return;
            }

            if (!e.GetCurrentPoint(FilesListBox).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var point = e.GetPosition(FilesListBox);
            var hit = FilesListBox.InputHitTest(point) as Visual;
            var listItem = hit?.FindAncestorOfType<ListBoxItem>();
            if (listItem?.DataContext is not FileEntry entry)
            {
                return;
            }

            var sourceIndex = _oneFileService.GetFileIndex(entry.FileName);
            if (sourceIndex < 0)
            {
                return;
            }

            List<string> tempPaths;
            try
            {
                tempPaths = _oneFileService.ExtractFilesToTemp(new[] { entry });
            }
            catch (Exception ex)
            {
                await ShowError($"Error preparing files for drag: {ex.Message}");
                return;
            }

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(InternalDragFormat, entry.FileName));

            foreach (var path in tempPaths)
            {
                var storageFile = await StorageProvider.TryGetFileFromPathAsync(path);
                if (storageFile != null)
                {
                    dataTransfer.Add(DataTransferItem.CreateFile(storageFile));
                }
            }

            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(InternalDragFormat) && _oneFileService.IsFileOpen)
            {
                e.DragEffects = DragDropEffects.Move;
            }
            else if (e.DataTransfer.Contains(DataFormat.File))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            e.Handled = true;

            if (e.DataTransfer.Contains(InternalDragFormat) && _oneFileService.IsFileOpen)
            {
                var sourceFileName = e.DataTransfer.TryGetValue(InternalDragFormat);
                if (string.IsNullOrEmpty(sourceFileName))
                {
                    return;
                }

                var targetArchiveIndex = GetDropTargetArchiveIndex(e);
                if (targetArchiveIndex < 0)
                {
                    targetArchiveIndex = _oneFileService.GetFileCount();
                }

                var sourceIndex = _oneFileService.GetFileIndex(sourceFileName);
                if (sourceIndex >= 0 && targetArchiveIndex != sourceIndex)
                {
                    _oneFileService.MoveFileToIndex(sourceFileName, targetArchiveIndex);
                    var entries = _oneFileService.GetFileEntries();
                    _viewModel.LoadFiles(entries);
                    UpdateWindowTitle();
                }

                return;
            }

            if (!e.DataTransfer.Contains(DataFormat.File))
            {
                return;
            }

            var files = e.DataTransfer.TryGetFiles();
            if (files == null || files.Length == 0)
            {
                return;
            }

            var paths = files.Select(f => f.Path.LocalPath).ToList();

            if (paths.Count == 1 && Path.GetExtension(paths[0]).Equals(".one", StringComparison.OrdinalIgnoreCase))
            {
                OpenOneFile(paths[0]);
                return;
            }

            if (!_oneFileService.IsFileOpen)
            {
                return;
            }

            try
            {
                var nonMatching = new List<string>();
                foreach (var path in paths)
                {
                    if (!_oneFileService.ReplaceFileByName(path))
                    {
                        nonMatching.Add(path);
                    }
                }

                if (nonMatching.Count > 0)
                {
                    var selectedName = (FilesListBox.SelectedItem as FileEntry)?.FileName;
                    var baseIndex = !string.IsNullOrEmpty(selectedName)
                        ? _oneFileService.GetFileIndex(selectedName!) + 1
                        : _oneFileService.GetFileCount();

                    for (int i = 0; i < nonMatching.Count; i++)
                    {
                        _oneFileService.InsertFile(baseIndex + i, nonMatching[i]);
                    }
                }

                var entries = _oneFileService.GetFileEntries();
                _viewModel.LoadFiles(entries);
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                await ShowError($"Error adding files: {ex.Message}");
            }
        }

        private int GetDropTargetArchiveIndex(DragEventArgs e)
        {
            var point = e.GetPosition(FilesListBox);
            var hit = FilesListBox.InputHitTest(point) as Visual;
            var listItem = hit?.FindAncestorOfType<ListBoxItem>();
            if (listItem == null)
            {
                return -1;
            }

            var filteredIndex = FilesListBox.IndexFromContainer(listItem);
            if (filteredIndex < 0 || filteredIndex >= _viewModel.FilteredFiles.Count)
            {
                return -1;
            }

            var targetEntry = _viewModel.FilteredFiles[filteredIndex];
            return _oneFileService.GetFileIndex(targetEntry.FileName);
        }

        private async void FilesListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                return;
            }

            try
            {
                var tempPath = _oneFileService.ExtractFileToTempForLaunch(selectedFile);
                if (GetTopLevel(this)?.Launcher is { } launcher)
                {
                    var storageFile = await StorageProvider.TryGetFileFromPathAsync(tempPath);
                    if (storageFile != null)
                    {
                        await launcher.LaunchFileAsync(storageFile);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowError($"Error opening file: {ex.Message}");
            }
        }

        private async void EditRWVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            var dialog = new RwVersionEditorWindow(selectedFile, (version, major, minor, revision, buildNumber) =>
            {
                _oneFileService.UpdateRwVersion(selectedFile.FileName, version, major, minor, revision, buildNumber);
                var entries = _oneFileService.GetFileEntries();
                _viewModel.LoadFiles(entries);
                UpdateWindowTitle();
            });

            await dialog.ShowDialog(this);
        }

        private async void SetArchiveRwVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var currentVersion = _oneFileService.GetArchiveRwVersion();
            var dummyEntry = new FileEntry
            {
                FileName = "(Archive)",
                RwVersion = currentVersion.Version,
                RwMajor = currentVersion.Major,
                RwMinor = currentVersion.Minor,
                RwRevision = currentVersion.Revision,
                RwBuildNumber = currentVersion.BuildNumber
            };

            var dialog = new RwVersionEditorWindow(dummyEntry, (version, major, minor, revision, buildNumber) =>
            {
                _oneFileService.SetArchiveRwVersion(version, major, minor, revision, buildNumber);
                UpdateWindowTitle();
            });

            await dialog.ShowDialog(this);
        }

        private async void SetAllFileRwVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var currentVersion = _oneFileService.GetFirstFileRwVersion();
            var dummyEntry = new FileEntry
            {
                FileName = "(All Files)",
                RwVersion = currentVersion.Version,
                RwMajor = currentVersion.Major,
                RwMinor = currentVersion.Minor,
                RwRevision = currentVersion.Revision,
                RwBuildNumber = currentVersion.BuildNumber
            };

            var dialog = new RwVersionEditorWindow(dummyEntry, (version, major, minor, revision, buildNumber) =>
            {
                _oneFileService.SetAllFileRwVersion(version, major, minor, revision, buildNumber);
                var entries = _oneFileService.GetFileEntries();
                _viewModel.LoadFiles(entries);
                UpdateWindowTitle();
            });

            await dialog.ShowDialog(this);
        }

        private async void SortByExtensions_Click(object? sender, RoutedEventArgs e)
        {
            if (!_oneFileService.IsFileOpen)
            {
                await ShowError("No file is currently open.");
                return;
            }

            var unsupportedExtensions = _oneFileService.SortByExtensions();
            
            if (unsupportedExtensions != null && unsupportedExtensions.Count > 0)
            {
                var extensionList = string.Join(", ", unsupportedExtensions);
                var dialog = new Window
                {
                    Title = "Error",
                    Width = 450,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };
                WindowsTitleBarHelper.SetDarkTitleBar(dialog);

                var panel = new StackPanel { Margin = new Thickness(20), Spacing = 15 };

                var textBlock = new TextBlock
                {
                    Text = $"Sort cancelled!{Environment.NewLine}Unsupported extensions found:{Environment.NewLine}{extensionList}{Environment.NewLine}{Environment.NewLine}Please report this to the developers!",
                    TextWrapping = TextWrapping.Wrap,
                };
                panel.Children.Add(textBlock);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10
                };

                var reportButton = new Button
                {
                    Content = "Report",
                    Width = 80
                };
                reportButton.Click += async (s, args) =>
                {
                    var fileName = Path.GetFileName(_viewModel.CurrentFilePath ?? "Unknown");
                    var archiveType = _oneFileService.ArchiveTypeName ?? "Unknown";
                    var titleText = $"Unhandled Types - {extensionList}";
                    var bodyText = $"File: {fileName}\nArchive Type: {archiveType}\n\nUnsupported extensions found:\n{extensionList}";
                    var title = Uri.EscapeDataString(titleText);
                    var body = Uri.EscapeDataString(bodyText);
                    var url = $"https://github.com/ShadowTheHedgehogHacking/ShadowONE/issues/new?title={title}&body={body}";
                    if (GetTopLevel(this)?.Launcher is { } launcher)
                    {
                        await launcher.LaunchUriAsync(new Uri(url));
                    }
                };
                buttonPanel.Children.Add(reportButton);

                var okButton = new Button
                {
                    Content = "OK",
                    Width = 80
                };
                okButton.Click += (s, args) => dialog.Close();
                buttonPanel.Children.Add(okButton);

                panel.Children.Add(buttonPanel);
                dialog.Content = panel;

                await dialog.ShowDialog(this);
                return;
            }

            var entries = _oneFileService.GetFileEntries();
            _viewModel.LoadFiles(entries);
            UpdateWindowTitle();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (FilesListBox.SelectedItem is FileEntry && e.KeyModifiers == KeyModifiers.Shift)
            {
                if (e.Key == Key.W)
                {
                    MoveUp_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.S)
                {
                    MoveDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
            {
                OpenFile_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
            {
                SaveAsFile_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S)
            {
                SaveFile_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.X)
            {
                ExtractAll_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (FilesListBox.SelectedItem is FileEntry)
            {
                if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.E)
                {
                    Extract_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.F2)
                {
                    Rename_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    Delete_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R)
                {
                    Replace_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        private async void About_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "About",
                Width = 360,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

            var titleBlock = new TextBlock
            {
                Text = $"ShadowONE - ONE File Editor {VersionInfo.Version}",
                FontWeight = FontWeight.Bold,
                FontSize = 16
            };
            panel.Children.Add(titleBlock);

            var descBlock = new TextBlock
            {
                Text = $"A ONE archive editor primarily for{Environment.NewLine}Shadow the Hedgehog and Sonic Heroes{Environment.NewLine}",
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descBlock);
            
            var descBlock2 = new TextBlock
            {
                Text = $"Thank you to contributors for HeroesONE and HeroesONE-Reloaded for prior research{Environment.NewLine}{Environment.NewLine}Libraries used: AvaloniaUI, HeroesONE-R, prs-rs",
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descBlock2);

            var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            var linkLabel = new TextBlock { Text = "Source code & updates:" };
            linkPanel.Children.Add(linkLabel);

            var linkBlock = new TextBlock
            {
                Text = "ShadowONE GitHub",
                Foreground = Brushes.Yellow,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextDecorations = TextDecorations.Underline
            };
            linkBlock.PointerPressed += async (s, args) =>
            {
                if (GetTopLevel(this)?.Launcher is { } launcher)
                {
                    await launcher.LaunchUriAsync(new Uri("https://github.com/ShadowTheHedgehogHacking/ShadowONE"));
                }
            };
            linkPanel.Children.Add(linkBlock);
            panel.Children.Add(linkPanel);
            dialog.Content = panel;

            await dialog.ShowDialog(this);
        }

        private async Task ShowError(string message)
        {
            var dialog = new Window
            {
                Title = "Error",
                Width = 600,
                Height = 200
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var textBlock = new TextBlock { Text = message, Margin = new Thickness(20) };
            var panel = new StackPanel();
            panel.Children.Add(textBlock);
            dialog.Content = panel;

            await dialog.ShowDialog(this);
        }

        private async Task<bool> ShowConfirmDialog(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 240,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var panel = new StackPanel { Margin = new Thickness(15), Spacing = 10 };

            var textBlock = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(textBlock);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            var yesButton = new Button { Content = "Yes", Width = 80 };
            var noButton = new Button { Content = "No", Width = 80 };

            var result = false;
            yesButton.Click += (s, args) =>
            {
                result = true;
                dialog.Close();
            };
            noButton.Click += (s, args) =>
            {
                result = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(yesButton);
            buttonPanel.Children.Add(noButton);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            yesButton.AttachedToVisualTree += (s, e) => yesButton.Focus();

            await dialog.ShowDialog(this);
            return result;
        }

        private async Task<string?> ShowInputDialog(string title, string prompt, string defaultValue)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

            var promptBlock = new TextBlock { Text = prompt };
            panel.Children.Add(promptBlock);

            string? result = null;

            var textBox = new TextBox 
            { 
                Text = defaultValue,
                SelectionStart = 0,
                SelectionEnd = defaultValue.Length
            };
            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = textBox.Text;
                    dialog.Close();
                    e.Handled = true;
                }
            };
            panel.Children.Add(textBox);

            var saveButton = new Button 
            { 
                Content = "Save", 
                Width = 70,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 0)
            };
            saveButton.Click += (s, e) =>
            {
                result = textBox.Text;
                dialog.Close();
            };
            panel.Children.Add(saveButton);

            dialog.Content = panel;

            textBox.AttachedToVisualTree += (s, e) => textBox.Focus();

            await dialog.ShowDialog(this);
            return result;
        }
    }
}
