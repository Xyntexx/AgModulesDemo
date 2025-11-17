namespace AgOpenGPS.Modules.Autosteer;

using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// Autosteer control plugin
/// Calculates steering commands based on GPS and guidance
/// </summary>
public class AutosteerPlugin : IAgModule, IConfigurableModule
{
    public string Name => "Autosteer";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Control;
    public string[] Dependencies => new[] { "PGN Translator" };

    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private ITimeProvider? _timeProvider;
    private IMessageQueue? _messageQueue;
    private IScheduledMethod? _tickHandle;
    private double _currentHeading;
    private double _targetHeading;
    private bool _engaged;

    // Simple PID controller state
    private double _lastError;
    private double _integral;

    // Settings
    private AutosteerSettings _settings = new AutosteerSettings();

    public Task InitializeAsync(IModuleContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;
        _timeProvider = context.TimeProvider;
        _messageQueue = context.CreateMessageQueue();

        // Subscribe to GPS position with queued execution (processed during Tick)
        _messageBus.SubscribeQueued<GpsPositionMessage>(OnGpsPosition, _messageQueue);

        // Subscribe to guidance line updates with queued execution
        _messageBus.SubscribeQueued<GuidanceLineMessage>(OnGuidanceUpdate, _messageQueue);

        // Subscribe to engage/disengage commands with immediate execution (UI responsiveness)
        _messageBus.Subscribe<AutosteerEngageMessage>(OnEngageCommand);

        // Schedule control loop at 10Hz
        _tickHandle = context.Scheduler?.Schedule(
            Tick,
            rateHz: 10.0,
            name: "AutosteerControl");

        _logger.LogInformation("Autosteer initialized with queued message processing");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        // Start disengaged - wait for UI command
        _engaged = false;

        // Set a default target heading for testing (90 degrees = due East)
        _targetHeading = 90.0;

        // Publish a test guidance line
        if (_messageBus != null)
        {
            var guidanceLine = new GuidanceLineMessage
            {
                StartLatitude = 40.7128,
                StartLongitude = -74.0060,
                HeadingDegrees = 90.0,  // Drive due East
                OffsetMeters = 0.0
            };
            _messageBus.Publish(in guidanceLine);
            _logger?.LogInformation($"Published test guidance line: Heading {guidanceLine.HeadingDegrees}°");
        }

        _logger?.LogInformation("Autosteer module started (disengaged)");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _engaged = false;
        _logger?.LogInformation("Autosteer disengaged");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _tickHandle?.Dispose();
        return Task.CompletedTask;
    }
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        _currentHeading = msg.Heading;
    }

    private void OnGuidanceUpdate(GuidanceLineMessage msg)
    {
        _targetHeading = msg.HeadingDegrees;
    }

    /// <summary>
    /// Tick method called by scheduler at 10Hz
    /// Processes queued messages then runs control loop
    /// </summary>
    private void Tick(long tickNumber, long monotonicMs)
    {
        // Process all queued messages (GPS, guidance updates)
        // This runs handlers in this module's thread context
        _messageQueue?.ProcessQueue();

        // Run control loop if engaged
        if (_engaged)
        {
            CalculateAndSendSteerCommand();
        }
    }

    private void OnEngageCommand(AutosteerEngageMessage msg)
    {
        _engaged = msg.IsEngaged;

        if (_engaged)
        {
            // Reset PID state when engaging
            _integral = 0;
            _lastError = 0;
            _logger?.LogInformation("Autosteer ENGAGED");
        }
        else
        {
            // Send zero steer command when disengaging
            if (_messageBus != null)
            {
                var cmd = new SteerCommandMessage
                {
                    SteerAngleDegrees = 0,
                    SpeedPWM = 0,
                    IsEngaged = false,
                    Timestamp = TimestampMetadata.CreateSimClockOnly(_timeProvider, 0)
                };
                _messageBus.Publish(in cmd);
            }
            _logger?.LogInformation("Autosteer DISENGAGED");
        }
    }

    private void CalculateAndSendSteerCommand()
    {
        if (_messageBus == null) return;

        // Calculate heading error
        double error = _targetHeading - _currentHeading;

        // Normalize error to -180 to 180
        while (error > 180) error -= 360;
        while (error < -180) error += 360;

        // PID calculation
        _integral += error;

        // Anti-windup: clamp integral
        _integral = Math.Clamp(_integral, -_settings.MaxIntegralWindup, _settings.MaxIntegralWindup);

        double derivative = error - _lastError;

        double steerAngle = (_settings.Kp * error) + (_settings.Ki * _integral) + (_settings.Kd * derivative);

        // Clamp to configured limits
        steerAngle = Math.Clamp(steerAngle, _settings.MinSteerAngleDegrees, _settings.MaxSteerAngleDegrees);

        _lastError = error;

        // Send steer command
        var cmd = new SteerCommandMessage
        {
            SteerAngleDegrees = steerAngle,
            SpeedPWM = 200, // Full speed
            IsEngaged = _engaged,
            Timestamp = TimestampMetadata.CreateSimClockOnly(_timeProvider, 0)
        };

        _messageBus.Publish(in cmd);

        _logger?.LogDebug($"Steer: Error={error:F2}°, Command={steerAngle:F2}°");
    }

    // IConfigurableModule implementation
    public IModuleSettings GetSettings()
    {
        return _settings;
    }

    public void UpdateSettings(IModuleSettings settings)
    {
        if (settings is AutosteerSettings autosteerSettings)
        {
            if (autosteerSettings.Validate(out var errorMessage))
            {
                _settings = autosteerSettings;
                _logger?.LogInformation($"Autosteer settings updated: Kp={_settings.Kp}, Ki={_settings.Ki}, Kd={_settings.Kd}");

                // Reset PID state when settings change
                _integral = 0;
                _lastError = 0;
            }
            else
            {
                _logger?.LogError($"Invalid autosteer settings: {errorMessage}");
            }
        }
    }
}
