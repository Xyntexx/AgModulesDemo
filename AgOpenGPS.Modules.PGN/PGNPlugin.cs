namespace AgOpenGPS.Modules.PGN;

using System.Text;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Bidirectional PGN translator
/// Parses incoming PGN messages and encodes outgoing commands
/// </summary>
public class PGNPlugin : IAgModule
{
    public string Name => "PGN Translator";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.DataProcessing;
    public string[] Dependencies => Array.Empty<string>();

    private IMessageBus? _messageBus;
    private ILogger? _logger;

    public Task InitializeAsync(IModuleContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to raw data from IO
        _messageBus.Subscribe<RawDataReceivedMessage>(ParseIncomingData, priority: 100);

        // Subscribe to control commands for encoding
        _messageBus.Subscribe<SteerCommandMessage>(EncodeSteerCommand);
        _messageBus.Subscribe<SectionControlMessage>(EncodeSectionControl);

        _logger.LogInformation("PGN Translator initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    // INBOUND: Parse received data
    private void ParseIncomingData(RawDataReceivedMessage raw)
    {
        if (_messageBus == null) return;

        string data = "";
        try
        {
            data = Encoding.ASCII.GetString(raw.Data);

            // Parse NMEA GGA sentence (position and fix quality)
            // NOTE: We only log GGA for diagnostics, but don't publish GPS position
            // because GGA doesn't contain heading/speed. We wait for RMC which has complete data.
            if (data.StartsWith("$GPGGA") || data.StartsWith("$GNGGA"))
            {
                var parts = data.Split(',');
                if (parts.Length > 9)
                {
                    var lat = ParseCoordinate(parts[2], parts[3]);
                    var lon = ParseCoordinate(parts[4], parts[5]);
                    var quality = int.Parse(parts[6]);

                    _logger?.LogTrace($"GGA: {lat:F6}, {lon:F6}, Fix: {quality}");
                }
            }
            // Parse NMEA RMC sentence (includes speed and heading)
            else if (data.StartsWith("$GPRMC") || data.StartsWith("$GNRMC"))
            {
                var parts = data.Split(',');
                if (parts.Length > 8 && parts[2] == "A") // A = Active/Valid
                {
                    var lat = ParseCoordinate(parts[3], parts[4]);
                    var lon = ParseCoordinate(parts[5], parts[6]);
                    var speedKnots = double.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture);
                    var heading = double.Parse(parts[8], System.Globalization.CultureInfo.InvariantCulture);

                    // Convert knots to m/s
                    var speedMs = speedKnots * 0.514444;

                    var gpsMsg = new GpsPositionMessage
                    {
                        Latitude = lat,
                        Longitude = lon,
                        Speed = speedMs,
                        Heading = heading,
                        FixQuality = GpsFixQuality.RTK_Fixed, // Assume good fix if RMC is valid
                        TimestampMs = raw.TimestampMs
                    };

                    _messageBus.Publish(in gpsMsg);
                    _logger?.LogDebug($"GPS: {lat:F6}, {lon:F6}, Heading: {heading:F1}°, Speed: {speedMs:F2}m/s");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error parsing PGN: {ex.Message}. Raw data: {data}");
        }
    }

    // OUTBOUND: Encode steer command to PGN
    private void EncodeSteerCommand(SteerCommandMessage cmd)
    {
        if (_messageBus == null) return;

        try
        {
            // Example: Simple binary protocol
            // Format: [Header][Angle][Speed][Engaged][Checksum][Footer]
            var data = new byte[7];
            data[0] = 0xFE; // Header
            data[1] = 0x01; // Steer command ID

            // Encode angle (-45 to +45 as 0-255)
            data[2] = (byte)((cmd.SteerAngleDegrees + 45) * 255 / 90);
            data[3] = cmd.SpeedPWM;
            data[4] = (byte)(cmd.IsEngaged ? 1 : 0); // Engage status

            // Simple checksum
            data[5] = (byte)((data[2] + data[3] + data[4]) & 0xFF);
            data[6] = 0xFF; // Footer

            var sendMsg = new RawDataToSendMessage
            {
                Data = data,
                TargetChannel = IOChannel.Serial,
                RequireAcknowledgment = false
            };

            _messageBus.Publish(in sendMsg);
            _logger?.LogDebug($"Sent steer command: {cmd.SteerAngleDegrees:F1}°");
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error encoding steer command: {ex.Message}");
        }
    }

    // OUTBOUND: Encode section control
    private void EncodeSectionControl(SectionControlMessage cmd)
    {
        if (_messageBus == null) return;

        try
        {
            var data = new byte[5];
            data[0] = 0xFE; // Header
            data[1] = 0x02; // Section control ID
            data[2] = (byte)(cmd.SectionBitmap & 0xFF);
            data[3] = (byte)((cmd.SectionBitmap >> 8) & 0xFF);
            data[4] = 0xFF; // Footer

            var sendMsg = new RawDataToSendMessage
            {
                Data = data,
                TargetChannel = IOChannel.Serial,
                RequireAcknowledgment = false
            };

            _messageBus.Publish(in sendMsg);
            _logger?.LogDebug($"Sent section control: 0x{cmd.SectionBitmap:X4}");
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error encoding section control: {ex.Message}");
        }
    }

    private double ParseCoordinate(string value, string direction)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        // NMEA format: DDMM.MMMM (latitude) or DDDMM.MMMM (longitude)
        // Latitude: 2 digits for degrees, rest for minutes
        // Longitude: 3 digits for degrees, rest for minutes

        int degreeDigits = (direction == "N" || direction == "S") ? 2 : 3;

        if (value.Length < degreeDigits + 3) return 0; // Minimum: DD.M or DDD.M

        var degrees = double.Parse(value.Substring(0, degreeDigits), System.Globalization.CultureInfo.InvariantCulture);
        var minutes = double.Parse(value.Substring(degreeDigits), System.Globalization.CultureInfo.InvariantCulture);
        var coord = degrees + minutes / 60.0;

        if (direction == "S" || direction == "W")
            coord = -coord;

        return coord;
    }
}
