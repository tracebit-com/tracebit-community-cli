namespace Tracebit.Cli.Config;

public static class ConfigDirectory
{
    public const string ConfigDirVariable = "TRACEBIT_CONFIG_DIR";

    /// <summary>
    /// The directory holding <c>credentials.json</c> and <c>state.json</c>. Overridable with
    /// <c>TRACEBIT_CONFIG_DIR</c> so the CLI can be sandboxed, because on macOS and Windows the default location ignores
    /// <c>HOME</c>.
    /// </summary>
    public static string Path
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(ConfigDirVariable);
            if (!string.IsNullOrEmpty(overridePath))
                return System.IO.Path.GetFullPath(overridePath);

            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Program.AppName);
        }
    }
}
