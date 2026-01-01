using Microsoft.Extensions.Configuration;

namespace BS.Data;

public class SessionKeyStore
{
    private readonly IConfiguration _configuration;
    private readonly string _filePath;

    public SessionKeyStore(IConfiguration configuration)
    {
        _configuration = configuration;
        _filePath = _configuration["FilePaths:SessionKeys"] ?? "session-keys.txt";
    }

    public List<string> GetIds()
    {
        if (!File.Exists(_filePath))
        {
            return new List<string>();
        }

        var lines = File.ReadAllLines(_filePath);
        return lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }

    public void AddId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be null or empty", nameof(id));
        }

        var ids = GetIds();
        if (!ids.Contains(id))
        {
            ids.Add(id);
            SaveIds(ids);
        }
    }

    public void RemoveId(string id)
    {
        var ids = GetIds();
        if (ids.Remove(id))
        {
            SaveIds(ids);
        }
    }

    public void SaveIds(List<string> ids)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(_filePath, ids);
    }

    public void ClearIds()
    {
        SaveIds(new List<string>());
    }
}