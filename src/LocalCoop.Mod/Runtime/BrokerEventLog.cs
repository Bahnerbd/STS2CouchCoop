namespace LocalCoop.Mod.Runtime;

public sealed class BrokerEventLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public BrokerEventLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Log path must not be blank.", nameof(path));
        }

        _path = path;
    }

    public void Write(string message)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_lock)
        {
            File.AppendAllLines(_path, [$"{DateTimeOffset.Now:O} {message}"]);
        }
    }
}

