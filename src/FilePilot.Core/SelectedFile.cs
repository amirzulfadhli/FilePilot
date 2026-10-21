namespace FilePilot.Core;

public sealed record SelectedFile(string FullPath, string Name, string Extension,
    long Length, DateTime LastWriteTime);
