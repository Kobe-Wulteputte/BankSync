using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace BS.Data;

/// <summary>
/// Persists <see cref="BankSession"/> records as JSON. Pure persistence — performs no API calls.
/// Reads the historical one-GUID-per-line format too, yielding incomplete records that the
/// caller fills in from the API and saves back.
/// </summary>
public class SessionKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public SessionKeyStore(IConfiguration configuration)
    {
        _filePath = configuration["FilePaths:SessionKeys"] ?? "session-keys.json";
    }

    public List<BankSession> GetSessions()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var text = File.ReadAllText(_filePath).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        if (text[0] == '[')
        {
            return JsonSerializer.Deserialize<List<BankSession>>(text, JsonOptions) ?? [];
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => Guid.TryParse(line, out _))
            .Select(line => new BankSession { SessionId = Guid.Parse(line) })
            .ToList();
    }

    public void SaveSessions(List<BankSession> sessions)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(sessions, JsonOptions));
    }

    /// <summary>Stores a session, replacing any existing session for the same bank.</summary>
    public void AddOrReplace(BankSession session)
    {
        var sessions = GetSessions()
            .Where(existing => !string.Equals(existing.Bank, session.Bank, StringComparison.OrdinalIgnoreCase))
            .ToList();

        sessions.Add(session);
        SaveSessions(sessions);
    }
}
