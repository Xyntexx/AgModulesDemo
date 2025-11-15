using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;

namespace AgOpenGPS.GUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private ApplicationCore? _core;
    private IMessageBus? _messageBus;

    [ObservableProperty]
    private string _latitude = "0.0";

    [ObservableProperty]
    private string _longitude = "0.0";

    [ObservableProperty]
    private string _heading = "0.0";

    [ObservableProperty]
    private string _speed = "0.0";

    [ObservableProperty]
    private string _steerAngle = "0.0";

    [ObservableProperty]
    private string _wasAngle = "0.0";

    [ObservableProperty]
    private bool _autosteerEngaged = false;

    [ObservableProperty]
    private string _autosteerButtonText = "ENGAGE AUTOSTEER";

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = new();

    public void Initialize(ApplicationCore core, IMessageBus messageBus)
    {
        _core = core;
        _messageBus = messageBus;

        // Subscribe to GPS updates
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        // Subscribe to steer commands
        _messageBus.Subscribe<SteerCommandMessage>(OnSteerCommand);

        AddLog("GUI initialized and connected to message bus");
    }

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        Latitude = $"{msg.Latitude:F6}°";
        Longitude = $"{msg.Longitude:F6}°";
        Heading = $"{msg.Heading:F1}°";
        Speed = $"{msg.Speed:F2} m/s";
    }

    private void OnSteerCommand(SteerCommandMessage msg)
    {
        SteerAngle = $"{msg.SteerAngleDegrees:F2}°";
        WasAngle = $"{msg.SteerAngleDegrees:F2}°"; // TODO: Replace with actual WAS sensor data when available

        // Update engaged status from steer command
        if (AutosteerEngaged != msg.IsEngaged)
        {
            AutosteerEngaged = msg.IsEngaged;
            AutosteerButtonText = AutosteerEngaged ? "DISENGAGE AUTOSTEER" : "ENGAGE AUTOSTEER";
        }
    }

    [RelayCommand]
    private void ToggleAutosteer()
    {
        if (_messageBus == null)
        {
            AddLog("ERROR: Message bus not available");
            return;
        }

        // Toggle state
        AutosteerEngaged = !AutosteerEngaged;
        AutosteerButtonText = AutosteerEngaged ? "DISENGAGE AUTOSTEER" : "ENGAGE AUTOSTEER";

        // Send engage message
        var msg = new AutosteerEngageMessage
        {
            IsEngaged = AutosteerEngaged,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _messageBus.Publish(in msg);

        AddLog($"Autosteer {(AutosteerEngaged ? "ENGAGED" : "DISENGAGED")}");
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        LogMessages.Insert(0, $"[{timestamp}] {message}");

        // Keep only last 100 messages
        while (LogMessages.Count > 100)
        {
            LogMessages.RemoveAt(LogMessages.Count - 1);
        }
    }
}
