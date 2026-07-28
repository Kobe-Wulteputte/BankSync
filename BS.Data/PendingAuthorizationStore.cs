using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace BS.Data;

/// <summary>
/// Persists outstanding authorization links as JSON, shared between the console (which creates
/// them) and the callback API (which consumes them). Pure persistence — performs no API calls.
/// Deliberately less defensive than <see cref="SessionKeyStore"/>: losing this file costs one
/// duplicate email, not a browser re-authorization, so a corrupt file is simply treated as empty.
/// </summary>
public class PendingAuthorizationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public PendingAuthorizationStore(IConfiguration configuration)
    {
        // Defaults alongside the session file rather than to a relative path: the console and the
        // callback API run from different working directories and must agree on one file, or a
        // state written by one is unknown to the other and every callback is rejected.
        _filePath = configuration["FilePaths:PendingAuthorizations"]
                    ?? DeriveFrom(configuration["FilePaths:SessionKeys"]);
    }

    private static string DeriveFrom(string? sessionKeysPath)
    {
        const string fileName = "pending-authorizations.json";

        if (string.IsNullOrWhiteSpace(sessionKeysPath))
        {
            return Path.Combine(AppContext.BaseDirectory, fileName);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(sessionKeysPath));

        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    public List<PendingAuthorization> GetAll()
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

        try
        {
            return JsonSerializer.Deserialize<List<PendingAuthorization>>(text, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void SaveAll(List<PendingAuthorization> pending)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(pending, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    /// <summary>Stores a pending authorization, replacing any outstanding one for the same bank.</summary>
    public void AddOrReplace(PendingAuthorization authorization)
    {
        var pending = GetAll()
            .Where(existing => !string.Equals(existing.Bank, authorization.Bank, StringComparison.OrdinalIgnoreCase))
            .ToList();

        pending.Add(authorization);
        SaveAll(pending);
    }

    public void RemoveByState(string state)
    {
        var pending = GetAll();
        if (pending.RemoveAll(existing => existing.State == state) > 0)
        {
            SaveAll(pending);
        }
    }

    public void RemoveForBank(string bank)
    {
        var pending = GetAll();
        if (pending.RemoveAll(existing => string.Equals(existing.Bank, bank, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveAll(pending);
        }
    }
}
