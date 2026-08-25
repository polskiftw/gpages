using System.Text.Json;

namespace GDupe.App;

public sealed record AppSettings(string? MonitoredFolder)
{
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(null)
                : new(null);
        }
        catch
        {
            return new(null);
        }
    }

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
