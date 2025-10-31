namespace AgOpenGPS.Modules.Kinematics;

using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Kinematics plugin - calculates heading from position changes
/// Enriches GPS data with calculated heading based on movement
/// </summary>
public class KinematicsPlugin : IAgModule
{
    public string Name => "Kinematics";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.DataProcessing;
    public string[] Dependencies => new[] { "PGN Translator" };

    private IMessageBus? _messageBus;
    private ILogger? _logger;

    // Previous position for heading calculation
    private double _prevLat;
    private double _prevLon;
    private bool _hasHistory;
    private double _calculatedHeading;

    public Task InitializeAsync(IModuleContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to GPS position with HIGH priority to enrich it before autosteer
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition, priority: 90);

        _logger.LogInformation("Kinematics plugin initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("Kinematics plugin started");
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        if (_messageBus == null) return;

        // Calculate heading from position change
        if (_hasHistory)
        {
            double dLat = msg.Latitude - _prevLat;
            double dLon = msg.Longitude - _prevLon;

            // Calculate heading (bearing)
            double heading = Math.Atan2(dLon, dLat) * 180.0 / Math.PI;
            if (heading < 0) heading += 360.0;

            // Only update if we've moved significantly (>0.1m)
            double distance = Math.Sqrt(dLat * dLat + dLon * dLon) * 111320.0; // rough meters
            if (distance > 0.1)
            {
                _calculatedHeading = heading;
            }
        }

        // Store current position for next iteration
        _prevLat = msg.Latitude;
        _prevLon = msg.Longitude;
        _hasHistory = true;

        // Note: We don't republish the GPS message to avoid infinite loops
        // Instead, other plugins can read the Heading field that's already in the message
        // The DummyIO simulator already calculates heading, so we just validate/log it here

        if (_hasHistory && _calculatedHeading > 0)
        {
            _logger?.LogTrace($"Calculated heading: {_calculatedHeading:F1}° (Message heading: {msg.Heading:F1}°)");
        }
    }
}
