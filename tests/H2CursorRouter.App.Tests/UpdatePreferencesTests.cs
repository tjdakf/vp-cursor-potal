using System.IO;
using H2CursorRouter.App.Services;
using Xunit;

namespace H2CursorRouter.App.Tests;

public sealed class UpdatePreferencesTests
{
    [Fact]
    public void PreferencePersistsSeparatelyFromDeviceConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vpc-pref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            File.WriteAllText(config, "existing device settings");
            var preferences = new UpdatePreferences(Path.Combine(directory, "update-settings.json"));
            Assert.False(preferences.Load());
            preferences.Save(true);
            Assert.True(preferences.Load());
            preferences.Save(false);
            Assert.False(preferences.Load());
            Assert.Equal("existing device settings", File.ReadAllText(config));
        }
        finally { Directory.Delete(directory, true); }
    }
}
