using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
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
        private const double DragStartThreshold = 30;
        private FileEntry? _dragCandidate;
        private Point _dragStartPoint;
        private PointerPressedEventArgs? _dragPressedEvent;

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
            FilesListBox.AddHandler(InputElement.PointerMovedEvent,
                FilesListBox_PointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            FilesListBox.AddHandler(InputElement.PointerReleasedEvent,
                FilesListBox_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            FilesListBox.AddHandler(InputElement.PointerCaptureLostEvent,
                FilesListBox_PointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
            this.AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            this.AddHandler(DragDrop.DropEvent, Window_Drop);

            LoadIcon();
            UpdateWindowTitle();
            FilePickerStartsAtOpenedFileMenuItem.IsChecked = AppSettings.FilePickerStartsAtOpenedFile;
            CheckForUpdatesOnStartupMenuItem.IsChecked = AppSettings.CheckForUpdatesOnStartup;
            CheckForUpdatesNowMenuItem.IsEnabled = UpdateChecker.IsSupported;
        }

        private void LoadIcon()
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://ShadowONE/Assets/logo.ico"));
                Icon = new WindowIcon(stream);
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

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await CheckFileAssociationAsync();
            await CheckForUpdatesOnStartupAsync();
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            if (!UpdateChecker.IsSupported || !AppSettings.CheckForUpdatesOnStartup || !UpdateChecker.IsCheckDue())
            {
                return;
            }

            var (result, remoteVersion) = await UpdateChecker.CheckAsync();
            if (result == UpdateCheckResult.UpdateAvailable)
            {
                await ShowUpdateDialog(result, remoteVersion);
            }
        }

        private async Task ShowUpdateDialog(UpdateCheckResult result, Version? remoteVersion)
        {
            var message = result switch
            {
                UpdateCheckResult.UpdateAvailable => $"A new version of ShadowONE is available ({remoteVersion}).{Environment.NewLine}You are running {UpdateChecker.LocalVersion}.",
                UpdateCheckResult.UpToDate => $"ShadowONE is up to date ({UpdateChecker.LocalVersion}).",
                _ => "Failed to check for updates."
            };

            var dialog = new Window
            {
                Title = "Check for Updates",
                Width = 360,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var panel = new StackPanel { Margin = new Thickness(15), Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            var closeButton = new Button { Content = "Close", MinWidth = 100 };
            closeButton.Click += (_, _) => dialog.Close();
            var focusButton = closeButton;

            if (result == UpdateCheckResult.UpdateAvailable)
            {
                var downloadButton = new Button { Content = "Open Download Page", MinWidth = 100 };
                downloadButton.Click += async (_, _) =>
                {
                    if (GetTopLevel(this)?.Launcher is { } launcher)
                    {
                        await launcher.LaunchUriAsync(new Uri(UpdateChecker.VisitUrl));
                    }
                    dialog.Close();
                };
                buttonPanel.Children.Add(downloadButton);
                focusButton = downloadButton;
            }

            buttonPanel.Children.Add(closeButton);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            focusButton.AttachedToVisualTree += (_, _) => focusButton.Focus();

            await dialog.ShowDialog(this);
        }

        private async Task CheckFileAssociationAsync()
        {
            // skip check if opening a .one directly
            if (Environment.GetCommandLineArgs().Skip(1).Any(arg => arg.EndsWith(".one", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var mode = AppSettings.FileAssociation;
            if (mode == FileAssociationMode.Never || !FileAssociationService.IsRegistrationNeeded())
            {
                return;
            }

            if (mode == FileAssociationMode.Yes)
            {
                FileAssociationService.Register();
                return;
            }

            await Task.Delay(300); // Wayland moment (even if using XWayland, need this to not pop in wrong spot)
            await ApplyFileAssociationPreferenceAsync(await ShowFileAssociationDialog());
        }

        private async Task ApplyFileAssociationPreferenceAsync(FileAssociationMode choice)
        {
            switch (choice)
            {
                case FileAssociationMode.Yes:
                    AppSettings.FileAssociation = FileAssociationMode.Yes;
                    await Task.Run(FileAssociationService.Register);
                    break;
                case FileAssociationMode.Never:
                    AppSettings.FileAssociation = FileAssociationMode.Never;
                    break;
                case FileAssociationMode.Ask:
                    AppSettings.FileAssociation = FileAssociationMode.Ask;
                    break;
            }
        }

        private async Task<FileAssociationMode> ShowFileAssociationDialog()
        {
            var dialog = new Window
            {
                Title = "File Association",
                Width = 380,
                Height = 100,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            WindowsTitleBarHelper.SetDarkTitleBar(dialog);

            var panel = new StackPanel { Margin = new Thickness(15), Spacing = 15 };
            panel.Children.Add(new TextBlock
            {
                Text = "Automatically open .one files with ShadowONE?",
                TextWrapping = TextWrapping.Wrap
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            var result = FileAssociationMode.Ask;
            Button AddButton(string text, FileAssociationMode choice)
            {
                var button = new Button { Content = text, MinWidth = 100 };
                button.Click += (_, _) =>
                {
                    result = choice;
                    dialog.Close();
                };
                buttonPanel.Children.Add(button);
                return button;
            }

            var yesButton = AddButton("Yes", FileAssociationMode.Yes);
            AddButton("Ask me later", FileAssociationMode.Ask);
            AddButton("Never ask me", FileAssociationMode.Never);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            yesButton.AttachedToVisualTree += (_, _) => yesButton.Focus();

            await dialog.ShowDialog(this);
            return result;
        }

        protected override void OnClosed(EventArgs e)
        {
            _oneFileService.CleanupTemp();
            base.OnClosed(e);
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
            if (!AppSettings.FilePickerStartsAtOpenedFile && !string.IsNullOrEmpty(_lastDialogFolder))
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
                    RefreshFileList();
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
                    _viewModel.CurrentFilePath = _currentFilePath;
                    RefreshFileList();
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
                    if (await RejectOverlongNames(files.Select(f => f.Path.LocalPath)))
                    {
                        return;
                    }

                    foreach (var file in files)
                    {
                        _oneFileService.InsertFile(_oneFileService.GetFileCount(), file.Path.LocalPath);
                    }

                    RefreshFileList();
                }
                catch (Exception ex)
                {
                    await ShowError($"Error adding files: {ex.Message}");
                }
            }
        }

        private async void Rename_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedFiles().Count > 1)
            {
                await ShowError("Select a single file to rename.");
                return;
            }

            if (FilesListBox.SelectedItem is not FileEntry selectedFile)
            {
                await ShowError("No file selected.");
                return;
            }

            var newName = await ShowInputDialog("Rename File", $"Enter new file name (max {_oneFileService.MaxFileNameLength} characters):", selectedFile.FileName, _oneFileService.MaxFileNameLength);
            if (!string.IsNullOrEmpty(newName) && newName != selectedFile.FileName)
            {
                try
                {
                    _oneFileService.RenameFile(selectedFile.FileName, newName);
                    RefreshFileList(newName);
                }
                catch (Exception ex)
                {
                    await ShowError($"Error renaming file: {ex.Message}");
                }
            }
        }

        private async void Replace_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                await ShowError("No file selected.");
                return;
            }

            var startFolder = await GetSuggestedStartLocation();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = selectedFiles.Count == 1 ? "Select Replacement File" : $"Select Replacement File for {selectedFiles.Count} Files",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (files.Count > 0)
            {
                _lastDialogFolder = Path.GetDirectoryName(files[0].Path.LocalPath);
                try
                {
                    _oneFileService.ReplaceFiles(selectedFiles, files[0].Path.LocalPath);
                    RefreshFileList(selectedFiles.Select(f => f.FileName));
                }
                catch (Exception ex)
                {
                    await ShowError($"Error replacing file: {ex.Message}");
                }
            }
        }

        private async void Extract_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                await ShowError("No file selected.");
                return;
            }

            if (selectedFiles.Count > 1)
            {
                await ExtractSelectedToFolder(selectedFiles);
                return;
            }

            var selectedFile = selectedFiles[0];
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

        private async Task ExtractSelectedToFolder(List<FileEntry> selectedFiles)
        {
            var startFolder = await GetSuggestedStartLocation();
            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select Extract Directory for {selectedFiles.Count} Files",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            if (folder.Count > 0)
            {
                try
                {
                    _lastDialogFolder = folder[0].Path.LocalPath;
                    _oneFileService.ExtractFiles(selectedFiles, folder[0].Path.LocalPath);
                }
                catch (Exception ex)
                {
                    await ShowError($"Error extracting files: {ex.Message}");
                }
            }
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                await ShowError("No file selected.");
                return;
            }

            try
            {
                _oneFileService.DeleteFiles(selectedFiles);
                RefreshFileList();
            }
            catch (Exception ex)
            {
                await ShowError($"Error deleting file: {ex.Message}");
            }
        }

        private void MoveUp_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            if (_oneFileService.MoveFilesUp(selectedFiles))
            {
                RefreshFileList(selectedFiles.Select(f => f.FileName));
            }
        }

        private void MoveDown_Click(object? sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            if (_oneFileService.MoveFilesDown(selectedFiles))
            {
                RefreshFileList(selectedFiles.Select(f => f.FileName));
            }
        }

        private List<FileEntry> GetSelectedFiles()
        {
            if (FilesListBox.SelectedItems == null)
            {
                return [];
            }

            return FilesListBox.SelectedItems
                .OfType<FileEntry>()
                .OrderBy(f => _oneFileService.GetFileIndex(f.FileName))
                .ToList();
        }

        private void FilesContextMenu_Opening(object? sender, CancelEventArgs e)
        {
            var count = GetSelectedFiles().Count;
            if (count == 0)
            {
                e.Cancel = true;
                return;
            }

            var suffix = count > 1 ? $" {count} Files" : "";
            ContextRenameMenuItem.IsEnabled = count == 1;
            ContextExtractMenuItem.Header = $"_Extract{suffix} (Ctrl+E)";
            ContextReplaceMenuItem.Header = $"_Replace{suffix} (Ctrl+R)";
            ContextDeleteMenuItem.Header = $"_Delete{suffix} (Del)";
            ContextEditRwVersionMenuItem.Header = $"Edit _RW Version{suffix}";
        }

        private void RefreshFileList(string? selectFileName = null)
        {
            RefreshFileList(selectFileName == null ? [] : [selectFileName]);
        }

        private void RefreshFileList(IEnumerable<string> selectFileNames)
        {
            _viewModel.LoadFiles(_oneFileService.GetFileEntries());
            UpdateWindowTitle();

            var names = selectFileNames.ToHashSet();
            if (names.Count == 0)
            {
                return;
            }

            var firstIndex = -1;
            FilesListBox.Selection.BeginBatchUpdate();
            for (var i = 0; i < _viewModel.FilteredFiles.Count; i++)
            {
                if (names.Contains(_viewModel.FilteredFiles[i].FileName))
                {
                    FilesListBox.Selection.Select(i);
                    if (firstIndex < 0)
                    {
                        firstIndex = i;
                    }
                }
            }
            FilesListBox.Selection.EndBatchUpdate();

            if (firstIndex >= 0)
            {
                FilesListBox.ScrollIntoView(firstIndex);
                FilesListBox.ContainerFromIndex(firstIndex)?.Focus();
            }
        }

        private void FilesListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _dragCandidate = null;

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

            if (_oneFileService.GetFileIndex(entry.FileName) < 0)
            {
                return;
            }

            _dragCandidate = entry;
            _dragStartPoint = point;
            _dragPressedEvent = e;
        }

        private void FilesListBox_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _dragCandidate = null;
        }

        private void FilesListBox_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _dragCandidate = null;
        }

        private async void FilesListBox_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragCandidate is not { } entry || _dragPressedEvent is not { } pressedEvent)
            {
                return;
            }

            if (!e.GetCurrentPoint(FilesListBox).Properties.IsLeftButtonPressed)
            {
                _dragCandidate = null;
                return;
            }

            var delta = e.GetPosition(FilesListBox) - _dragStartPoint;
            if (Math.Abs(delta.X) < DragStartThreshold && Math.Abs(delta.Y) < DragStartThreshold)
            {
                return;
            }

            _dragCandidate = null;

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

            await DragDrop.DoDragDropAsync(pressedEvent, dataTransfer, DragDropEffects.Copy | DragDropEffects.Move);
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
                    RefreshFileList();
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

            if (await RejectOverlongNames(paths))
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

                RefreshFileList();
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
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count == 0)
            {
                await ShowError("No file selected.");
                return;
            }

            var first = selectedFiles[0];
            var editEntry = selectedFiles.Count == 1 ? first : new FileEntry
            {
                FileName = $"({selectedFiles.Count} Files)",
                RwVersion = first.RwVersion,
                RwMajor = first.RwMajor,
                RwMinor = first.RwMinor,
                RwRevision = first.RwRevision,
                RwBuildNumber = first.RwBuildNumber
            };

            var dialog = new RwVersionEditorWindow(editEntry, (version, major, minor, revision, buildNumber) =>
            {
                foreach (var file in selectedFiles)
                {
                    _oneFileService.UpdateRwVersion(file.FileName, version, major, minor, revision, buildNumber);
                }
                RefreshFileList(selectedFiles.Select(f => f.FileName));
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
                RefreshFileList();
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

            RefreshFileList();
        }

        private bool IsTextInputFocused()
        {
            return GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (IsTextInputFocused())
            {
                return;
            }

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
                else if (e.Key == Key.Delete && !IsTextInputFocused())
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

        private void FilePickerStartsAtOpenedFile_Click(object? sender, RoutedEventArgs e)
        {
            AppSettings.FilePickerStartsAtOpenedFile = FilePickerStartsAtOpenedFileMenuItem.IsChecked;
        }

        private async void FileAssociation_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyFileAssociationPreferenceAsync(await ShowFileAssociationDialog());
        }

        private void CheckForUpdatesOnStartup_Click(object? sender, RoutedEventArgs e)
        {
            AppSettings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartupMenuItem.IsChecked;
        }

        private async void CheckForUpdatesNow_Click(object? sender, RoutedEventArgs e)
        {
            CheckForUpdatesNowMenuItem.IsEnabled = false;
            var (result, remoteVersion) = await UpdateChecker.CheckAsync();
            CheckForUpdatesNowMenuItem.IsEnabled = true;
            await ShowUpdateDialog(result, remoteVersion);
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

        private async Task<bool> RejectOverlongNames(IEnumerable<string> paths)
        {
            var overlong = _oneFileService.GetOverlongFileNames(paths);
            if (overlong.Count == 0)
            {
                return false;
            }

            await ShowError($"File name chars have a max of {_oneFileService.MaxFileNameLength} characters.");
            return true;
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

        private async Task<string?> ShowInputDialog(string title, string prompt, string defaultValue, int maxLength = 0)
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
                MaxLength = maxLength,
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
