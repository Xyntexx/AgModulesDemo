using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private string _satelliteCount = "0";

    [ObservableProperty]
    private string _fixQuality = "No Fix";

    [ObservableProperty]
    private bool _autosteerEngaged = false;

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
        SatelliteCount = msg.SatelliteCount.ToString();
        FixQuality = msg.FixQuality.ToString();
    }

    private void OnSteerCommand(SteerCommandMessage msg)
    {
        SteerAngle = $"{msg.SteerAngleDegrees:F2}°";
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
