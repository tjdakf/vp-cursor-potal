using System.Text.Json;
using System.Text.Json.Serialization;
using H2CursorRouter.Core.Validation;

namespace H2CursorRouter.Core.Configuration;

public sealed class ConfigFileService
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly AppConfigurationValidator _validator = new();

    public async Task<AppConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ConfigDocument>(stream, Options, cancellationToken)
            ?? throw new InvalidOperationException($"Configuration file '{path}' is empty or invalid.");

        var configuration = document.ToRuntime();
        var validation = _validator.Validate(configuration);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Configuration file '{path}' is invalid: {string.Join("; ", validation.Errors)}");
        }

        return configuration;
    }

    public async Task SaveAsync(AppConfiguration configuration, string path, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(configuration);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Cannot save invalid configuration: {string.Join("; ", validation.Errors)}");
        }

        // Replace only after the entire new document has been written. No backup copy is created.
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                var document = ConfigDocument.FromRuntime(configuration);
                await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
