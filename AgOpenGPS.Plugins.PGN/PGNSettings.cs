namespace AgOpenGPS.Plugins.PGN;

using AgOpenGPS.PluginContracts;

/// <summary>
/// Settings for the PGN Translator plugin
/// </summary>
public class PGNSettings : IPluginSettings
{
    public string SettingsId => "PGN";

    // Protocol selection
    public string Protocol { get; set; } = "NMEA";  // NMEA, J1939, Custom

    // NMEA-specific settings
    public bool ParseGGA { get; set; } = true;
    public bool ParseRMC { get; set; } = true;
    public bool ParseVTG { get; set; } = false;
    public bool ParseHDT { get; set; } = false;

    // Data filtering
    public double MinimumFixQuality { get; set; } = 1.0;
    public int MinimumSatellites { get; set; } = 4;

    // Validation
    public bool ValidateChecksum { get; set; } = true;
    public bool DiscardInvalidMessages { get; set; } = true;

    // Output encoding
    public string SteerCommandFormat { get; set; } = "Binary";  // Binary, ASCII, JSON
    public bool RequireAcknowledgment { get; set; } = false;

    public bool Validate(out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(Protocol))
        {
            errorMessage = "Protocol cannot be empty";
            return false;
        }

        if (MinimumFixQuality < 0 || MinimumFixQuality > 5)
        {
            errorMessage = "Minimum fix quality must be between 0 and 5";
            return false;
        }

        if (MinimumSatellites < 0 || MinimumSatellites > 50)
        {
            errorMessage = "Minimum satellites must be between 0 and 50";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public void Apply()
    {
        // Settings will be applied by the plugin when UpdateSettings is called
    }
}
