namespace ShadowONE.Models
{
    public class FileEntry
    {
        public string FileName { get; set; } = string.Empty;
        public string Metadata { get; set; } = string.Empty;
        public bool IsModified { get; set; }
        public uint RwVersion { get; set; }
        public uint RwMajor { get; set; }
        public uint RwMinor { get; set; }
        public uint RwRevision { get; set; }
        public ushort RwBuildNumber { get; set; }
    }
}
