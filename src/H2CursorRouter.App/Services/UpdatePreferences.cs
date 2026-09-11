using System.IO;
using System.Text.Json;

namespace H2CursorRouter.App.Services;

public sealed class UpdatePreferences(string path)
{
    public bool Load()
    {
        try { return JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.CheckOnStartup ?? false; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public void Save(bool enabled)
    {
        var temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(enabled)));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record Preference(bool CheckOnStartup);
}
