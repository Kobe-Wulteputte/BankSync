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
            try
            {
                return JsonSerializer.Deserialize<List<BankSession>>(text, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                QuarantineCorruptFile();
                return [];
            }
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

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sessions, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// Preserves an unreadable session file by renaming it aside so the user's data isn't
    /// silently lost. Failure to rename (e.g. the file is locked, or a protected folder denies
    /// the write) must not crash the app either.
    /// </summary>
    private void QuarantineCorruptFile()
    {
        var quarantinePath = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            File.Move(_filePath, quarantinePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort only — an inaccessible file must not prevent the app from starting.
        }
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
