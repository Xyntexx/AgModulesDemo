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
    public string[] Dependencies => new[] { "Serial IO" };

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

            // Simple NMEA GPS parsing (GGA sentence)
            if (data.StartsWith("$GPGGA") || data.StartsWith("$GNGGA"))
            {
                var parts = data.Split(',');
                if (parts.Length > 9)
                {
                    var lat = ParseCoordinate(parts[2], parts[3]);
                    var lon = ParseCoordinate(parts[4], parts[5]);
                    var quality = int.Parse(parts[6]);
                    var sats = int.Parse(parts[7]);
                    var alt = double.Parse(parts[9], System.Globalization.CultureInfo.InvariantCulture);

                    var gpsMsg = new GpsPositionMessage
                    {
                        Latitude = lat,
                        Longitude = lon,
                        Altitude = alt,
                        FixQuality = (GpsFixQuality)quality,
                        SatelliteCount = sats,
                        TimestampMs = raw.TimestampMs
                    };

                    _messageBus.Publish(in gpsMsg);
                    _logger?.LogDebug($"GPS: {lat:F6}, {lon:F6}, Fix: {quality}");
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
            // Format: [Header][Angle][Speed][Checksum]
            var data = new byte[6];
            data[0] = 0xFE; // Header
            data[1] = 0x01; // Steer command ID

            // Encode angle (-45 to +45 as 0-255)
            data[2] = (byte)((cmd.SteerAngleDegrees + 45) * 255 / 90);
            data[3] = cmd.SpeedPWM;

            // Simple checksum
            data[4] = (byte)((data[2] + data[3]) & 0xFF);
            data[5] = 0xFF; // Footer

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
