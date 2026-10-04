using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using HeroesONE_R.Structures;
using HeroesONE_R.Structures.Common;
using HeroesONE_R.Structures.ShadowTheHedgehog;
using HeroesONE_R.Structures.SonicHeroes.ONE_Substructures;
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

        /// <summary>
        /// Longest file name the current archive format can store; the name field includes a null terminator.
        /// </summary>
        public int MaxFileNameLength => _archiveType switch
        {
            ONEArchiveType.Shadow050 => ONE50FileEntry.FileNameLength - 1,
            ONEArchiveType.Shadow060 => ONE60FileEntry.FileNameLength - 1,
            _ => ONEFileName.FileNameLength - 1,
        };

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
                var decompressedSize = file.CompressedData.IsEmpty ? 0 : (int)Prs.GetDecompressedSize(file.CompressedData.Span);
                entries.Add(new FileEntry
                {
                    FileName = file.Name,
                    Metadata = $"C: {FormatFileSize(file.CompressedData.Length)} | D: {FormatFileSize(decompressedSize)} | RW: {file.RwVersion}",
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

        // <temp>/ShadowONE/<processId>/
        private static string GetTempRootDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "ShadowONE");
        }

        private static string GetInstanceTempDirectory()
        {
            return Path.Combine(GetTempRootDirectory(), Environment.ProcessId.ToString());
        }

        private static string GetDragTempDirectory()
        {
            return Path.Combine(GetInstanceTempDirectory(), "DragDrop");
        }

        private static string GetLaunchTempDirectory()
        {
            return Path.Combine(GetInstanceTempDirectory(), "Launch");
        }

        public void CleanupTemp()
        {
            try
            {
                var root = GetTempRootDirectory();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
                // if a temp resource is being held by a process (like if dragged into a RW viewer), ignore
            }
        }

        public List<string> ExtractFilesToTemp(IEnumerable<FileEntry> entries)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var tempDir = GetDragTempDirectory();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            var paths = new List<string>();
            foreach (var entry in entries)
            {
                var outputPath = Path.Combine(tempDir, GetSafeFileName(entry.FileName));
                var decompressedData = ExtractFile(entry);
                File.WriteAllBytes(outputPath, decompressedData);
                paths.Add(outputPath);
            }

            return paths;
        }

        public string ExtractFileToTempForLaunch(FileEntry entry)
        {
            var launchDir = GetLaunchTempDirectory();
            Directory.CreateDirectory(launchDir);
            var tempPath = Path.Combine(launchDir, GetSafeFileName(entry.FileName));
            var data = ExtractFile(entry);
            File.WriteAllBytes(tempPath, data);
            return tempPath;
        }

        private static string GetSafeFileName(string fileName)
        {
            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = fileName;
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }

            return safeName;
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

        /// <summary>
        /// Returns the names of the given files (by path) that are too long for the current archive format.
        /// </summary>
        public List<string> GetOverlongFileNames(IEnumerable<string> filePaths)
        {
            return filePaths
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => name.Length > MaxFileNameLength)
                .ToList();
        }

        private void ValidateFileNameLength(string name)
        {
            if (name.Length > MaxFileNameLength)
            {
                throw new ArgumentException(
                    $"\"{name}\" is {name.Length} characters; this archive format allows at most {MaxFileNameLength}.");
            }
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

            ValidateFileNameLength(Path.GetFileName(filePath));
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

        public void UpdateRwVersion(string fileName, uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == fileName);
            if (file != null)
            {
                ApplyRWVersion(file.RwVersion, version, major, minor, revision, buildNumber);
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

            if (string.IsNullOrWhiteSpace(newFileName))
            {
                throw new ArgumentException("File name cannot be empty.");
            }

            ValidateFileNameLength(newFileName);

            var file = _currentArchive.Files.FirstOrDefault(f => f.Name == oldFileName);
            if (file != null)
            {
                if (_currentArchive.Files.Any(f => f != file && f.Name.Equals(newFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"A file named \"{newFileName}\" already exists in this archive.");
                }

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

            File.WriteAllBytes(_currentFilePath, BuildArchiveData().ToArray());
            _modifiedFiles.Clear();
            _isDirty = false;
        }

        public void SaveChangesAs(string newFilePath)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            File.WriteAllBytes(newFilePath, BuildArchiveData().ToArray());
            _currentFilePath = newFilePath;
            _modifiedFiles.Clear();
            _isDirty = false;
        }

        private List<byte> BuildArchiveData()
        {
            if (_archiveType == ONEArchiveType.Heroes)
            {
                return _currentArchive!.BuildHeroesONEArchive();
            }

            var isShadow60 = _archiveType == ONEArchiveType.Shadow060;
            return _currentArchive!.BuildShadowONEArchive(isShadow60);
        }

        public void SetArchiveRwVersion(uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            ApplyRWVersion(_currentArchive.RwVersion, version, major, minor, revision, buildNumber);
            _isDirty = true;
        }

        public (uint Version, uint Major, uint Minor, uint Revision, ushort BuildNumber) GetArchiveRwVersion()
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            return ReadRWVersion(_currentArchive.RwVersion);
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
                return ReadRWVersion(firstFile.RwVersion);
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
                ApplyRWVersion(file.RwVersion, version, major, minor, revision, buildNumber);
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
            return SwapWithNeighbor(entry, -1);
        }

        public bool MoveFileDown(FileEntry entry)
        {
            return SwapWithNeighbor(entry, 1);
        }

        private bool SwapWithNeighbor(FileEntry entry, int direction)
        {
            if (_currentArchive == null)
            {
                throw new InvalidOperationException("No file is currently open");
            }

            var index = GetFileIndex(entry.FileName);
            var otherIndex = index + direction;
            if (index < 0 || otherIndex < 0 || otherIndex >= _currentArchive.Files.Count)
            {
                return false;
            }

            var file = _currentArchive.Files[index];
            var otherFile = _currentArchive.Files[otherIndex];

            _currentArchive.Files[index] = otherFile;
            _currentArchive.Files[otherIndex] = file;

            _modifiedFiles.Add(file.Name);
            _modifiedFiles.Add(otherFile.Name);
            _isDirty = true;

            return true;
        }

        private static void ApplyRWVersion(RWVersion rw, uint version, uint major, uint minor, uint revision, ushort buildNumber)
        {
            rw.SetVersion(version);
            rw.SetMajor(major);
            rw.SetMinor(minor);
            rw.SetRevision(revision);
            rw.SetBuild(buildNumber);
        }

        private static (uint Version, uint Major, uint Minor, uint Revision, ushort BuildNumber) ReadRWVersion(RWVersion rw)
        {
            return (rw.GetVersion(), rw.GetMajor(), rw.GetMinor(), rw.GetRevision(), rw.GetBuild());
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
