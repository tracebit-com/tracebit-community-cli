using System.Text.Json;

namespace Tracebit.Cli.Config;

public class Credentials
{
    public static FileInfo File()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Program.AppName,
            "credentials.json");
        return new FileInfo(path);
    }

    // NOTE: this will remain null if no token can be loaded.
    public string? Token { get; set; }

    public static async Task<Credentials> LoadCredsFromFile(CancellationToken cancellationToken)
    {
        Credentials emptyCredentials = new();

        var credentialsFile = File();
        if (!credentialsFile.Exists || credentialsFile.Length == 0)
        {
            return emptyCredentials;
        }

        await using var credentialsStream = credentialsFile.Open(FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            return await JsonSerializer.DeserializeAsync(credentialsStream,
                ConfigJsonSerializerContext.Default.Credentials, cancellationToken) ?? emptyCredentials;
        }
        catch (JsonException e)
        {
            throw new Exception($"Your Tracebit credentials file might be corrupted. Please delete it from '{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}' and try again.", e);
        }
    }
}
