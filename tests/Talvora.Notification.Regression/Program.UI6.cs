using Talvora.Tray;
using Talvora.Tray.ViewModels;

internal static partial class Program
{
    private static async Task AssertUi6SettingsAsync()
    {
        var model = new SettingsViewModel();
        var missing = new ControlCenterSetupState(false, true, false, 2,
            ["talvora-dev", "talvora-admin"], "Missing", "Technical");
        model.UpdateSetup(false, missing);
        if (model.Steps[0].StateText != "Sırada" ||
            model.Steps[1].StateText != "Bekliyor" ||
            model.Steps[2].StateText != "Bekliyor")
            throw new InvalidOperationException("UI6: core must be the first active step.");

        model.UpdateSetup(true, missing);
        if (model.Steps[0].StateText != "Tamam" ||
            model.Steps[1].StateText != "Sırada" ||
            model.Steps[2].StateText != "Bekliyor")
            throw new InvalidOperationException("UI6: missing tunnels must be the active step.");

        var readyTunnels = missing with
        {
            MissingTunnelRegistrationIds = [],
            RuntimeFoundationMissing = false
        };
        model.UpdateSetup(true, readyTunnels);
        if (model.Steps[1].StateText != "Tamam" ||
            model.Steps[2].StateText != "Sırada")
            throw new InvalidOperationException("UI6: pending admin key must be step 3.");

        model.UpdateSetup(true, readyTunnels with { NeedsAdminCredential = false });
        if (!model.AllComplete || model.Steps.Any(step => step.StateText != "Tamam"))
            throw new InvalidOperationException("UI6: all steps must collapse to completion.");

        var settingsPath = Path.Combine(Path.GetTempPath(), "Talvora-UI6-" + Guid.NewGuid().ToString("N"), "preferences.json");
        try
        {
            var saved = new SettingsPreferences(SettingsMotionPreference.Reduced, SettingsRailPreference.Collapsed);
            await SettingsPreferencesStore.SaveAsync(settingsPath, saved);
            if (SettingsPreferencesStore.Load(settingsPath) != saved)
                throw new InvalidOperationException("UI6: preferences did not survive reload.");
        }
        finally
        {
            var directory = Path.GetDirectoryName(settingsPath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("UI6_SETTINGS_GREEN");
    }
}
