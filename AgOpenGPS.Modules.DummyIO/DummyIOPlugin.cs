namespace AgOpenGPS.Modules.DummyIO;

using System.Text;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;
using System.Globalization;

/// <summary>
/// Dummy IO plugin with simple vehicle simulation
/// Simulates a vehicle that responds to steer commands and generates GPS data
/// </summary>
public class DummyIOPlugin : IAgModule
{
    public string Name => "Dummy IO Simulator";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private CancellationToken _shutdownToken;
    private Task? _simulationTask;

    // Vehicle state
    private double _latitude = 40.7128;   // Starting at NYC coordinates
    private double _longitude = -74.0060;
    private double _heading = 45.0;       // degrees
    private double _speed = 2.0;          // m/s (~7 km/h, typical tractor speed)
    private double _steerAngle = 0.0;     // Current steer angle from controller

    // Simulation constants
    private const double UpdateRateHz = 10.0;  // 10 Hz simulation
    private const double DeltaTime = 1.0 / UpdateRateHz;
    private const double WheelBase = 2.5;      // meters (typical tractor wheelbase)
    private const double DegreesToMeters = 111320.0; // Approximate at equator

    public Task InitializeAsync(IModuleContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;
        _shutdownToken = context.AppShutdownToken;

        // Subscribe to outbound steer commands from PGN
        _messageBus.Subscribe<RawDataToSendMessage>(OnReceiveSteerCommand);

        _logger.LogInformation("Dummy IO Simulator initialized");
        _logger.LogInformation($"Starting position: {_latitude:F6}, {_longitude:F6}");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("Starting vehicle simulation...");
        _simulationTask = Task.Run(SimulationLoop);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("Stopping vehicle simulation");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    /// <summary>
    /// Main simulation loop - updates vehicle state and publishes GPS data
    /// </summary>
    private async Task SimulationLoop()
    {
        var updateInterval = TimeSpan.FromSeconds(DeltaTime);

        while (!_shutdownToken.IsCancellationRequested)
        {
            // Update vehicle physics
            UpdateVehicleState();

            // Generate and publish GPS sentence
            PublishGPSData();

            await Task.Delay(updateInterval, _shutdownToken);
        }
    }

    /// <summary>
    /// Simple bicycle model physics for vehicle movement
    /// </summary>
    private void UpdateVehicleState()
    {
        // Convert heading to radians
        double headingRad = _heading * Math.PI / 180.0;

        // Update heading based on steer angle (bicycle model)
        // Turn rate = (speed * tan(steerAngle)) / wheelbase
        double steerAngleRad = _steerAngle * Math.PI / 180.0;
        double turnRate = (_speed * Math.Tan(steerAngleRad)) / WheelBase;
        _heading += turnRate * DeltaTime * 180.0 / Math.PI;

        // Normalize heading to 0-360
        while (_heading >= 360.0) _heading -= 360.0;
        while (_heading < 0.0) _heading += 360.0;

        // Update position based on heading and speed
        double distanceTraveled = _speed * DeltaTime;

        // Convert distance to lat/lon delta
        double latDelta = (distanceTraveled * Math.Cos(headingRad)) / DegreesToMeters;
        double lonDelta = (distanceTraveled * Math.Sin(headingRad)) /
                          (DegreesToMeters * Math.Cos(_latitude * Math.PI / 180.0));

        _latitude += latDelta;
        _longitude += lonDelta;
    }

    /// <summary>
    /// Generate NMEA GGA and RMC sentences and publish as raw data
    /// </summary>
    private void PublishGPSData()
    {
        if (_messageBus == null) return;

        string time = DateTime.UtcNow.ToString("HHmmss.ff");
        string date = DateTime.UtcNow.ToString("ddMMyy");

        // Convert latitude to NMEA format (DDMM.MMMM)
        int latDeg = (int)Math.Abs(_latitude);
        double latMin = (Math.Abs(_latitude) - latDeg) * 60.0;
        string latStr = FormattableString.Invariant($"{latDeg:D2}{latMin:00.0000}");
        string latDir = _latitude >= 0 ? "N" : "S";

        // Convert longitude to NMEA format (DDDMM.MMMM)
        int lonDeg = (int)Math.Abs(_longitude);
        double lonMin = (Math.Abs(_longitude) - lonDeg) * 60.0;
        string lonStr = FormattableString.Invariant($"{lonDeg:D3}{lonMin:00.0000}");
        string lonDir = _longitude >= 0 ? "E" : "W";

        // Generate NMEA GGA sentence (position and fix quality)
        // $GPGGA,time,lat,N/S,lon,E/W,quality,sats,hdop,alt,M,geoid,M,age,station*checksum
        string gga = $"$GPGGA,{time},{latStr},{latDir},{lonStr},{lonDir},4,12,0.9,50.0,M,0.0,M,,";
        gga += $"*{CalculateChecksum(gga):X2}\r\n";

        // Generate NMEA RMC sentence (includes speed and heading)
        // $GPRMC,time,status,lat,N/S,lon,E/W,speed,heading,date,magvar,E/W,mode*checksum
        double speedKnots = _speed * 1.94384; // Convert m/s to knots
        string speedStr = speedKnots.ToString("F2", CultureInfo.InvariantCulture);
        string headingStr = _heading.ToString("F2", CultureInfo.InvariantCulture);
        string rmc = $"$GPRMC,{time},A,{latStr},{latDir},{lonStr},{lonDir},{speedStr},{headingStr},{date},,";
        rmc += $"*{CalculateChecksum(rmc):X2}\r\n";

        // Publish GGA
        var ggaMessage = new RawDataReceivedMessage
        {
            Data = Encoding.ASCII.GetBytes(gga),
            Channel = IOChannel.Serial,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _messageBus.Publish(in ggaMessage);

        // Publish RMC
        var rmcMessage = new RawDataReceivedMessage
        {
            Data = Encoding.ASCII.GetBytes(rmc),
            Channel = IOChannel.Serial,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _messageBus.Publish(in rmcMessage);

        // Log every 1 second (every 10th message at 10Hz)
        if ((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 100) % 10 == 0)
        {
            _logger?.LogInformation($"Vehicle: Lat={_latitude:F6}, Lon={_longitude:F6}, Heading={_heading:F1}°, Speed={_speed:F2}m/s, SteerAngle={_steerAngle:F2}°");
        }
    }

    /// <summary>
    /// Calculate NMEA checksum
    /// </summary>
    private byte CalculateChecksum(string sentence)
    {
        byte checksum = 0;
        for (int i = 1; i < sentence.Length; i++)
        {
            checksum ^= (byte)sentence[i];
        }
        return checksum;
    }

    /// <summary>
    /// Receive steer commands from PGN plugin and apply to vehicle
    /// </summary>
    private void OnReceiveSteerCommand(RawDataToSendMessage msg)
    {
        if (msg.TargetChannel != IOChannel.Serial || msg.Data.Length < 7) return;

        try
        {
            // Decode our simple protocol (from PGN plugin)
            // Format: [0xFE][0x01][Angle][Speed][Engaged][Checksum][0xFF]
            if (msg.Data[0] == 0xFE && msg.Data[1] == 0x01 && msg.Data[6] == 0xFF)
            {
                // Decode angle (0-255 mapped to -45 to +45)
                _steerAngle = (msg.Data[2] * 90.0 / 255.0) - 45.0;
                bool engaged = msg.Data[4] == 1;

                _logger?.LogInformation($"DummyIO received steer command: Angle={_steerAngle:F2}° (Engaged={engaged})");
            }
            else
            {
                _logger?.LogWarning($"DummyIO received invalid steer command: Header={msg.Data[0]:X2}, ID={msg.Data[1]:X2}, Footer={msg.Data[6]:X2}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error decoding steer command: {ex.Message}");
        }
    }
}
