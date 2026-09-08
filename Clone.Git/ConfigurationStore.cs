using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clone.Git;

public sealed record CloneConfiguration(string? Root = null, CloneLayout? Layout = null, string? GitPath = null, int? TimeoutSeconds = null);

[JsonSerializable(typeof(CloneConfiguration))]
[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal partial class ConfigurationJson : JsonSerializerContext;

public static class ConfigurationStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "clone");

    public static CloneConfiguration Load(string? directory = null)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Configuration storage supports macOS only.");
        directory ??= DirectoryPath;
        // No configuration is an ordinary first-run state. Existing paths are opened without following symlinks.
        if (!Directory.Exists(directory) && !File.Exists(directory) && new DirectoryInfo(directory).LinkTarget is null) return new CloneConfiguration();
        using var storage = MacDirectory.OpenRoot(directory, false);
        var text = storage.ReadText("config.json");
        if (text is null) return new CloneConfiguration();
        try
        {
            var configuration = JsonSerializer.Deserialize(text, ConfigurationJson.Default.CloneConfiguration) ?? throw new JsonException();
            Validate(configuration);
            return configuration;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new IOException("Invalid configuration. Inspect config.json or use an explicit clean configuration directory.");
        }
    }

    public static void Save(CloneConfiguration configuration, string? directory = null)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Configuration storage supports macOS only.");
        Validate(configuration);
        using var storage = MacDirectory.OpenRoot(directory ?? DirectoryPath, true);
        storage.WriteText("config.json", JsonSerializer.Serialize(configuration, ConfigurationJson.Default.CloneConfiguration) + "\n");
    }

    public static void Validate(CloneConfiguration configuration)
    {
        if (configuration.Root is { } root && (!Path.IsPathFullyQualified(root) || Path.GetFullPath(root) == "/" || UnsafePath(root))) throw new ArgumentException("Configuration root must be an absolute directory path.");
        if (configuration.GitPath is { } git && (!Path.IsPathFullyQualified(git) || UnsafePath(git))) throw new ArgumentException("Configured Git path must be absolute.");
        if (configuration.Layout is { } layout && !Enum.IsDefined(layout)) throw new ArgumentException("Unknown configured layout.");
        if (configuration.TimeoutSeconds is < 1 or > 86400) throw new ArgumentException("Timeout must be between 1 and 86400 seconds.");
    }

    private static bool UnsafePath(string path) => path.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);

    public static void InspectRoot(string root)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS is required.");
        using var directory = MacDirectory.OpenRoot(root, false);
    }
}
