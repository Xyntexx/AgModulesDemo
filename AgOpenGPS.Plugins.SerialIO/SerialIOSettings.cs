namespace AgOpenGPS.Plugins.SerialIO;

using AgOpenGPS.PluginContracts;

/// <summary>
/// Settings for the Serial IO plugin
/// </summary>
public class SerialIOSettings : IPluginSettings
{
    public string SettingsId => "SerialIO";

    // Serial port configuration
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";  // None, Odd, Even, Mark, Space
    public string StopBits { get; set; } = "One"; // None, One, Two, OnePointFive

    // Buffer settings
    public int ReadBufferSize { get; set; } = 4096;
    public int WriteBufferSize { get; set; } = 2048;

    // Timeout settings
    public int ReadTimeoutMs { get; set; } = 500;
    public int WriteTimeoutMs { get; set; } = 500;

    // Flow control
    public bool RtsEnable { get; set; } = false;
    public bool DtrEnable { get; set; } = false;
    public string Handshake { get; set; } = "None"; // None, XOnXOff, RequestToSend, RequestToSendXOnXOff

    public bool Validate(out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            errorMessage = "Port name cannot be empty";
            return false;
        }

        if (BaudRate <= 0 || BaudRate > 921600)
        {
            errorMessage = "Baud rate must be between 1 and 921600";
            return false;
        }

        if (DataBits < 5 || DataBits > 8)
        {
            errorMessage = "Data bits must be between 5 and 8";
            return false;
        }

        if (ReadTimeoutMs < 0 || WriteTimeoutMs < 0)
        {
            errorMessage = "Timeout values cannot be negative";
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
