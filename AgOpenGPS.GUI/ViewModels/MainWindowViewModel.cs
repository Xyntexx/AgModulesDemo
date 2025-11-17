using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
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
    private Timer? _moduleRefreshTimer;

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

    [ObservableProperty]
    private ObservableCollection<ModuleStatusViewModel> _modules = new();

    [ObservableProperty]
    private string _moduleStatusText = "Loading...";

    public void Initialize(ApplicationCore core, IMessageBus messageBus)
    {
        _core = core;
        _messageBus = messageBus;

        // Subscribe to GPS updates
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        // Subscribe to steer commands
        _messageBus.Subscribe<SteerCommandMessage>(OnSteerCommand);

        AddLog("GUI initialized and connected to message bus");

        // Initial module load
        RefreshModules();

        // Setup timer to refresh modules every 2 seconds
        _moduleRefreshTimer = new Timer(_ => RefreshModules(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void RefreshModules()
    {
        if (_core == null) return;

        try
        {
            var moduleInfos = _core.GetLoadedModules();

            Dispatcher.UIThread.Post(() =>
            {
                Modules.Clear();

                foreach (var info in moduleInfos)
                {
                    var memoryInfo = _core.GetModuleMemoryInfo(info.ModuleId);

                    Modules.Add(new ModuleStatusViewModel
                    {
                        Name = info.Name,
                        Version = $"v{info.Version}",
                        State = info.State.ToString(),
                        Category = info.Category.ToString(),
                        Health = info.Health,
                        MemoryUsage = $"{memoryInfo.EstimatedMemoryMB:F1} MB",
                        LoadedTime = info.LoadedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                ModuleStatusText = $"{Modules.Count} module(s) loaded • Last updated: {DateTime.Now:HH:mm:ss}";
            });
        }
        catch (Exception ex)
        {
            AddLog($"Error refreshing modules: {ex.Message}");
        }
    }

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        // Marshal to UI thread for property updates
        Dispatcher.UIThread.Post(() =>
        {
            Latitude = $"{msg.Latitude:F6}°";
            Longitude = $"{msg.Longitude:F6}°";
            Heading = $"{msg.Heading:F1}°";
            Speed = $"{msg.Speed:F2} m/s";

            // Debug: Log heading updates to verify they're being processed
            if ((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) % 2 == 0)
            {
                AddLog($"UI: Heading={msg.Heading:F1}° (Lat={msg.Latitude:F6}, Lon={msg.Longitude:F6})");
            }
        });
    }

    private void OnSteerCommand(SteerCommandMessage msg)
    {
        // Marshal to UI thread for property updates
        Dispatcher.UIThread.Post(() =>
        {
            SteerAngle = $"{msg.SteerAngleDegrees:F2}°";
            WasAngle = $"{msg.SteerAngleDegrees:F2}°"; // TODO: Replace with actual WAS sensor data when available

            // Update engaged status from steer command
            if (AutosteerEngaged != msg.IsEngaged)
            {
                AutosteerEngaged = msg.IsEngaged;
                AutosteerButtonText = AutosteerEngaged ? "DISENGAGE AUTOSTEER" : "ENGAGE AUTOSTEER";
            }
        });
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
        };

        _messageBus.Publish(in msg);

        AddLog($"Autosteer {(AutosteerEngaged ? "ENGAGED" : "DISENGAGED")}");
    }

    private void AddLog(string message)
    {
        // Marshal to UI thread for collection updates
        Dispatcher.UIThread.Post(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogMessages.Insert(0, $"[{timestamp}] {message}");

            // Keep only last 100 messages
            while (LogMessages.Count > 100)
            {
                LogMessages.RemoveAt(LogMessages.Count - 1);
            }
        });
    }
}

public class ModuleStatusViewModel
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string State { get; set; } = "";
    public string Category { get; set; } = "";
    public ModuleHealth Health { get; set; }
    public string MemoryUsage { get; set; } = "";
    public string LoadedTime { get; set; } = "";

    public string HealthText => Health switch
    {
        ModuleHealth.Healthy => "HEALTHY",
        ModuleHealth.Degraded => "DEGRADED",
        ModuleHealth.Unhealthy => "UNHEALTHY",
        _ => "UNKNOWN"
    };

    public ISolidColorBrush HealthColor => Health switch
    {
        ModuleHealth.Healthy => new SolidColorBrush(Color.Parse("#27AE60")),
        ModuleHealth.Degraded => new SolidColorBrush(Color.Parse("#F39C12")),
        ModuleHealth.Unhealthy => new SolidColorBrush(Color.Parse("#E74C3C")),
        _ => new SolidColorBrush(Color.Parse("#95A5A6"))
    };
}
