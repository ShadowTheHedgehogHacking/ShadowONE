using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using HeroesONE_R.Structures;
using HeroesONE_R.Structures.Common;
using HeroesONE_R.Structures.Substructures;
using HeroesONE_R.Utilities;
using ShadowONE.Models;

namespace ShadowONE.Services
{
    public class OneFileService
    {
        private static readonly string[] ShadowOneExtensionOrder =
        [
            "SNB",
            "EFD",
            "DFF",
            "TXD",
            "UVA",
            "BIN",
            "CCL",
            "BON",
            "MTN",
            "MTP",
            "DMA",
            "PTP",
            "PTB",
            "BDT",
            "ADB",
            "GNCP",
            "XNCP",
        ];

        private Archive? _currentArchive;
        private string? _currentFilePath;
        private ONEArchiveType _archiveType;
        private readonly HashSet<string> _modifiedFiles = new();
        private bool _isDirty;

        public bool IsFileOpen => _currentArchive != null;

        public bool HasUnsavedChanges => _isDirty || _modifiedFiles.Count > 0;

        public string? ArchiveTypeName
        {
            get
            {
                if (!IsFileOpen)
                    return null;

                return _archiveType.ToString();
            }
        }

        public string? ArchiveRwVersion
        {
            get
            {
                if (!IsFileOpen)
                    return null;
                
                return _currentArchive?.RwVersion.ToString();
            }
        }

        public ObservableCollection<FileEntry> GetFileEntries()
        {
            var entries = new ObservableCollection<FileEntry>();

            if (_currentArchive == null)
            {
                return entries;
            }

            foreach (var file in _currentArchive.Files)
            {
                var decompressedData = file.DecompressThis();
                entries.Add(new FileEntry
                {
                    FileName = file.Name,
                    FileSize = decompressedData.Length,
                    Offset = 0,
                    Metadata = $"C: {FormatFileSize(file.CompressedData.Length)} | D: {FormatFileSize(decompressedData.Length)} | RW: {file.RwVersion}",
                    IsModified = _modifiedFiles.Contains(file.Name),
                    RwVersion = file.RwVersion.GetVersion(),
                    RwMajor = file.RwVersion.GetMajor(),
                    RwMinor = file.RwVersion.GetMinor(),
                    RwRevision = file.RwVersion.GetRevision(),
                    RwBuildNumber = file.RwVersion.GetBuild()
                });
            }

            return entries;
        }

        public ObservableCollection<FileEntry> OpenFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            _currentFilePath = filePath;
            var fileData = File.ReadAllBytes(filePath);
            _archiveType = ONEArchiveTester.GetArchiveType(ref fileData);
            _currentArchive = Archive.FromONEFile(ref fileData);
            _modifiedFiles.Clear();
            _isDirty = false;

            return GetFileEntries();
        }

        public byte[] ExtractFile(FileEntry entry)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == entry.FileName);
            if (file == null)
            {
                throw new FileNotFoundException($"File not found in archive: {entry.FileName}");
            }

            return file.DecompressThis().ToArray();
        }

        public void ExtractAllFiles(string outputDirectory)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            foreach (var file in _currentArchive.Files)
            {
                var outputPath = Path.Combine(outputDirectory, file.Name);
                var decompressedData = file.DecompressThis().ToArray();
                File.WriteAllBytes(outputPath, decompressedData);
            }
        }

        public List<string> ExtractFilesToTemp(IEnumerable<FileEntry> entries)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "ShadowONE_DragDrop");
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // ignored - best effort cleanup of previous staging files
                }
            }
            Directory.CreateDirectory(tempDir);

            var paths = new List<string>();
            foreach (var entry in entries)
            {
                var safeName = Path.GetFileName(entry.FileName);
                if (string.IsNullOrEmpty(safeName))
                {
                    safeName = entry.FileName;
                }

                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    safeName = safeName.Replace(c, '_');
                }

                var outputPath = Path.Combine(tempDir, safeName);
                var decompressedData = ExtractFile(entry);
                File.WriteAllBytes(outputPath, decompressedData);
                paths.Add(outputPath);
            }

            return paths;
        }

        public string ExtractFileToTempForLaunch(FileEntry entry)
        {
            var safeName = Path.GetFileName(entry.FileName);
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = entry.FileName;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), safeName);
            var data = ExtractFile(entry);
            File.WriteAllBytes(tempPath, data);
            return tempPath;
        }

        public int GetFileCount()
        {
            return _currentArchive?.Files.Count ?? 0;
        }

        public int GetFileIndex(string fileName)
        {
            if (_currentArchive == null)
            {
                return -1;
            }

            for (int i = 0; i < _currentArchive.Files.Count; i++)
            {
                if (_currentArchive.Files[i].Name == fileName)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool ReplaceFileByName(string filePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var name = Path.GetFileName(filePath);
            var file = _currentArchive.Files.FirstOrDefault(
                f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                return false;
            }

            var data = File.ReadAllBytes(filePath);
            file.CompressedData = Prs.CompressData(data);
            _modifiedFiles.Add(file.Name);
            _isDirty = true;
            return true;
        }

        public void InsertFile(int index, string filePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var newFile = new ArchiveFile(filePath, _currentArchive.RwVersion);
            index = Math.Clamp(index, 0, _currentArchive.Files.Count);
            _currentArchive.Files.Insert(index, newFile);
            _isDirty = true;
        }

        public void MoveFileToIndex(string sourceFileName, int targetArchiveIndex)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var sourceIndex = GetFileIndex(sourceFileName);
            if (sourceIndex < 0)
            {
                return;
            }

            var originalOrder = _currentArchive.Files.Select(f => f.Name).ToList();

            var file = _currentArchive.Files[sourceIndex];
            _currentArchive.Files.RemoveAt(sourceIndex);

            if (targetArchiveIndex > sourceIndex)
            {
                targetArchiveIndex--;
            }

            targetArchiveIndex = Math.Clamp(targetArchiveIndex, 0, _currentArchive.Files.Count);
            _currentArchive.Files.Insert(targetArchiveIndex, file);

            for (int i = 0; i < _currentArchive.Files.Count; i++)
            {
                if (i >= originalOrder.Count || _currentArchive.Files[i].Name != originalOrder[i])
                {
                    _modifiedFiles.Add(_currentArchive.Files[i].Name);
                }
            }

            _isDirty = true;
        }

        public void ReplaceFile(FileEntry entry, string replacementFilePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            if (!File.Exists(replacementFilePath))
            {
                throw new FileNotFoundException($"Replacement file not found: {replacementFilePath}");
            }

            var fileData = File.ReadAllBytes(replacementFilePath);
            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == entry.FileName);

            if (file != null)
            {
                file.Name = entry.FileName;
                file.CompressedData = Prs.CompressData(fileData);
                _modifiedFiles.Add(entry.FileName);
                _isDirty = true;
            }
        }

        public void DeleteFile(FileEntry entry)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == entry.FileName);
            if (file != null)
            {
                _currentArchive.Files.Remove(file);
                _isDirty = true;
            }
        }

        public void AddFile(string filePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var newFile = new ArchiveFile(filePath, _currentArchive.RwVersion);
            _currentArchive.Files.Add(newFile);
            _isDirty = true;
        }

        public void UpdateRwVersion(string fileName, uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == fileName);
            if (file != null)
            {
                file.RwVersion.SetVersion(version);
                file.RwVersion.SetMajor(major);
                file.RwVersion.SetMinor(minor);
                file.RwVersion.SetRevision(revision);
                file.RwVersion.SetBuild(buildNumber);
                _modifiedFiles.Add(fileName);
                _isDirty = true;
            }
        }

        public void RenameFile(string oldFileName, string newFileName)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == oldFileName);
            if (file != null)
            {
                file.Name = newFileName;
                _modifiedFiles.Remove(oldFileName);
                _modifiedFiles.Add(newFileName);
                _isDirty = true;
            }
        }

        public void SaveChanges()
        {
            if (_currentArchive == null || _currentFilePath == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            List<byte> fileData;
            if (_archiveType == ONEArchiveType.Heroes)
            {
                fileData = _currentArchive.BuildHeroesONEArchive();
            }
            else
            {
                var isShadow60 = _archiveType == ONEArchiveType.Shadow060;
                fileData = _currentArchive.BuildShadowONEArchive(isShadow60);
            }

            File.WriteAllBytes(_currentFilePath, fileData.ToArray());
            _modifiedFiles.Clear();
            _isDirty = false;
        }

        public void SaveChangesAs(string newFilePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            List<byte> fileData;
            if (_archiveType == ONEArchiveType.Heroes)
            {
                fileData = _currentArchive.BuildHeroesONEArchive();
            }
            else
            {
                var isShadow60 = _archiveType == ONEArchiveType.Shadow060;
                fileData = _currentArchive.BuildShadowONEArchive(isShadow60);
            }

            File.WriteAllBytes(newFilePath, fileData.ToArray());
            _currentFilePath = newFilePath;
            _modifiedFiles.Clear();
            _isDirty = false;
        }

        public void SetArchiveRwVersion(uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            _currentArchive.RwVersion.SetVersion(version);
            _currentArchive.RwVersion.SetMajor(major);
            _currentArchive.RwVersion.SetMinor(minor);
            _currentArchive.RwVersion.SetRevision(revision);
            _currentArchive.RwVersion.SetBuild(buildNumber);
            _isDirty = true;
        }

        public (uint Version, uint Major, uint Minor, uint Revision, ushort BuildNumber) GetArchiveRwVersion()
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            return (
                _currentArchive.RwVersion.GetVersion(),
                _currentArchive.RwVersion.GetMajor(),
                _currentArchive.RwVersion.GetMinor(),
                _currentArchive.RwVersion.GetRevision(),
                _currentArchive.RwVersion.GetBuild()
            );
        }

        public (uint Version, uint Major, uint Minor, uint Revision, ushort BuildNumber) GetFirstFileRwVersion()
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            if (_currentArchive.Files.Count > 0)
            {
                var firstFile = _currentArchive.Files[0];
                return (
                    firstFile.RwVersion.GetVersion(),
                    firstFile.RwVersion.GetMajor(),
                    firstFile.RwVersion.GetMinor(),
                    firstFile.RwVersion.GetRevision(),
                    firstFile.RwVersion.GetBuild()
                );
            }

            return GetArchiveRwVersion();
        }

        public void SetAllFileRwVersion(uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            foreach (var file in _currentArchive.Files)
            {
                file.RwVersion.SetVersion(version);
                file.RwVersion.SetMajor(major);
                file.RwVersion.SetMinor(minor);
                file.RwVersion.SetRevision(revision);
                file.RwVersion.SetBuild(buildNumber);
                _modifiedFiles.Add(file.Name);
            }

            _isDirty = true;
        }

        public List<string>? SortByExtensions()
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var unsupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var file in _currentArchive.Files)
            {
                var ext = Path.GetExtension(file.Name).TrimStart('.');
                var isSupported = false;
                foreach (var supportedExt in ShadowOneExtensionOrder)
                {
                    if (ext.Equals(supportedExt, StringComparison.OrdinalIgnoreCase))
                    {
                        isSupported = true;
                        break;
                    }
                }
                if (!isSupported && !string.IsNullOrEmpty(ext))
                {
                    unsupportedExtensions.Add(ext.ToUpperInvariant());
                }
            }

            if (unsupportedExtensions.Count > 0)
            {
                return unsupportedExtensions.ToList();
            }

            var originalOrder = _currentArchive.Files.Select(f => f.Name).ToList();
            var sortedFiles = new List<ArchiveFile>();
            
            foreach (var extension in ShadowOneExtensionOrder)
            {
                foreach (var file in _currentArchive.Files)
                {
                    if (file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        sortedFiles.Add(file);
                    }
                }
            }
            
            _currentArchive.Files.Clear();
            foreach (var file in sortedFiles)
            {
                _currentArchive.Files.Add(file);
            }

            for (int i = 0; i < sortedFiles.Count; i++)
            {
                if (i >= originalOrder.Count || sortedFiles[i].Name != originalOrder[i])
                {
                    _modifiedFiles.Add(sortedFiles[i].Name);
                }
            }

            _isDirty = true;
            return null;
        }

        public bool MoveFileUp(FileEntry entry)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var index = -1;
            for (int i = 0; i < _currentArchive.Files.Count; i++)
            {
                if (_currentArchive.Files[i].Name == entry.FileName)
                {
                    index = i;
                    break;
                }
            }

            if (index <= 0)
            {
                return false;
            }

            var file = _currentArchive.Files[index];
            var otherFile = _currentArchive.Files[index - 1];
            
            _currentArchive.Files[index] = otherFile;
            _currentArchive.Files[index - 1] = file;

            _modifiedFiles.Add(file.Name);
            _modifiedFiles.Add(otherFile.Name);
            _isDirty = true;

            return true;
        }

        public bool MoveFileDown(FileEntry entry)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var index = -1;
            for (int i = 0; i < _currentArchive.Files.Count; i++)
            {
                if (_currentArchive.Files[i].Name == entry.FileName)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 || index >= _currentArchive.Files.Count - 1)
            {
                return false;
            }

            var file = _currentArchive.Files[index];
            var otherFile = _currentArchive.Files[index + 1];
            
            _currentArchive.Files[index] = otherFile;
            _currentArchive.Files[index + 1] = file;

            _modifiedFiles.Add(file.Name);
            _modifiedFiles.Add(otherFile.Name);
            _isDirty = true;

            return true;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB"];
            double len = bytes;
            var order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
