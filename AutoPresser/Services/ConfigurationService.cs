using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoPresser.Models;

namespace AutoPresser.Services;

/// <summary>Loads and atomically saves all profiles in one JSON file.</summary>
public sealed class ConfigurationService(string? filePath = null)
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public string FilePath { get; } = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoPresser",
        "config.json");

    /// <summary>Warning set when an invalid configuration had to be preserved and recreated.</summary>
    public string? LastLoadWarning { get; private set; }

    public AutoPresserConfiguration LoadOrCreateDefault(Func<AutoPresserConfiguration> createDefault)
    {
        ArgumentNullException.ThrowIfNull(createDefault);

        if (!File.Exists(FilePath))
        {
            var initial = createDefault();
            Save(initial);
            return initial;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var configuration = JsonSerializer.Deserialize<AutoPresserConfiguration>(json, JsonOptions)
                ?? throw new InvalidDataException("The configuration file contains no configuration object.");

            ValidateDocument(configuration);
            return configuration;
        }
        catch (Exception exception)
        {
            var corruptPath = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(FilePath, corruptPath, overwrite: false);
            }
            catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    $"AutoPresser could not load its configuration and could not preserve the invalid file. Original error: {exception.Message}",
                    moveException);
            }

            LastLoadWarning = $"The existing configuration was invalid and has been preserved as '{corruptPath}'. A new default configuration was created. Details: {exception.Message}";
            var initial = createDefault();
            Save(initial);
            return initial;
        }
    }

    public void Save(AutoPresserConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateDocument(configuration);

        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The configuration path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{FilePath}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(configuration, JsonOptions);
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateDocument(AutoPresserConfiguration configuration)
    {
        if (configuration.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Configuration schema version {configuration.SchemaVersion} is not supported. Expected {CurrentSchemaVersion}.");
        }

        if (configuration.Profiles is null || configuration.Profiles.Count == 0)
        {
            throw new InvalidDataException("At least one profile is required.");
        }

        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in configuration.Profiles)
        {
            if (profile is null)
            {
                throw new InvalidDataException("The configuration contains a null profile.");
            }

            if (profile.Id == Guid.Empty || !ids.Add(profile.Id))
            {
                throw new InvalidDataException($"Profile ID '{profile.Id}' is empty or duplicated.");
            }

            profile.Name = profile.Name?.Trim() ?? string.Empty;
            if (profile.Name.Length == 0)
            {
                throw new InvalidDataException($"Profile '{profile.Id}' must have a name.");
            }

            if (!names.Add(profile.Name))
            {
                throw new InvalidDataException($"Profile name '{profile.Name}' is duplicated. Profile names must be unique.");
            }

            profile.Bindings ??= [];
            BindingValidator.EnsureValid(profile.Bindings);
        }

        if (!ids.Contains(configuration.ActiveProfileId))
        {
            throw new InvalidDataException("The selected profile does not exist in the configuration.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
