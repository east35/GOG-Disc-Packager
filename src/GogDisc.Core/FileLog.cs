namespace GogDisc.Core;

public sealed class FileLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(string message)
    {
        lock (_gate)
            File.AppendAllText(_path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
