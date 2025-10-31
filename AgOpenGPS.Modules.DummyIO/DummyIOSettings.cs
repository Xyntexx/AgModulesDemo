namespace AgOpenGPS.Modules.DummyIO;

using AgOpenGPS.ModuleContracts;

/// <summary>
/// Settings for the Dummy IO Simulator plugin
/// </summary>
public class DummyIOSettings : IModuleSettings
{
    public string SettingsId => "DummyIO";

    // Starting position
    public double StartLatitude { get; set; } = 40.7128;  // NYC
    public double StartLongitude { get; set; } = -74.0060;
    public double StartHeading { get; set; } = 45.0;      // degrees

    // Vehicle parameters
    public double Speed { get; set; } = 2.0;               // m/s
    public double WheelBase { get; set; } = 2.5;           // meters

    // Simulation parameters
    public double UpdateRateHz { get; set; } = 10.0;
    public bool EnableNoise { get; set; } = false;
    public double NoiseAmplitude { get; set; } = 0.0001;   // degrees

    // GPS simulation
    public int SimulatedSatelliteCount { get; set; } = 12;
    public double SimulatedAltitude { get; set; } = 50.0;  // meters

    public bool Validate(out string? errorMessage)
    {
        if (StartLatitude < -90 || StartLatitude > 90)
        {
            errorMessage = "Latitude must be between -90 and 90 degrees";
            return false;
        }

        if (StartLongitude < -180 || StartLongitude > 180)
        {
            errorMessage = "Longitude must be between -180 and 180 degrees";
            return false;
        }

        if (Speed < 0 || Speed > 20)
        {
            errorMessage = "Speed must be between 0 and 20 m/s";
            return false;
        }

        if (WheelBase <= 0 || WheelBase > 10)
        {
            errorMessage = "Wheelbase must be between 0 and 10 meters";
            return false;
        }

        if (UpdateRateHz < 1 || UpdateRateHz > 100)
        {
            errorMessage = "Update rate must be between 1 and 100 Hz";
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
