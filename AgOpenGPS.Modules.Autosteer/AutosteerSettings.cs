namespace AgOpenGPS.Modules.Autosteer;

using AgOpenGPS.ModuleContracts;

/// <summary>
/// Settings for the Autosteer plugin
/// </summary>
public class AutosteerSettings : IModuleSettings
{
    public string SettingsId => "Autosteer";

    // PID Controller parameters
    public double Kp { get; set; } = 2.0;
    public double Ki { get; set; } = 0.1;
    public double Kd { get; set; } = 0.5;

    // Physical constraints
    public double MaxSteerAngleDegrees { get; set; } = 45.0;
    public double MinSteerAngleDegrees { get; set; } = -45.0;

    // Control parameters
    public double MaxIntegralWindup { get; set; } = 100.0;
    public bool EnableAutoEngage { get; set; } = false;
    public double MinSpeedForEngage { get; set; } = 0.5; // m/s

    // Performance tuning
    public double UpdateRateHz { get; set; } = 10.0;
    public double DeadzoneDegrees { get; set; } = 0.5;

    public bool Validate(out string? errorMessage)
    {
        if (Kp < 0 || Kp > 10)
        {
            errorMessage = "Kp must be between 0 and 10";
            return false;
        }

        if (Ki < 0 || Ki > 5)
        {
            errorMessage = "Ki must be between 0 and 5";
            return false;
        }

        if (Kd < 0 || Kd > 5)
        {
            errorMessage = "Kd must be between 0 and 5";
            return false;
        }

        if (MaxSteerAngleDegrees < 10 || MaxSteerAngleDegrees > 90)
        {
            errorMessage = "Max steer angle must be between 10 and 90 degrees";
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
