# UI Description Pattern - Plugin Describes UI via Messages

## Concept

Plugins send **UI description messages** to the UI module, which dynamically builds the interface. This decouples plugins from UI framework (Avalonia, WPF, etc.) and allows UI-less operation.

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Application Process                   │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │  UI Module (Avalonia)                            │  │
│  │  ┌────────────────────────────────────────────┐  │  │
│  │  │ UIBuilder                                  │  │  │
│  │  │ - Receives UIDescriptionMessage           │  │  │
│  │  │ - Dynamically creates controls            │  │  │
│  │  │ - Binds to data from messages             │  │  │
│  │  └────────────────────────────────────────────┘  │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│                     ▼                                   │
│  ┌──────────────────────────────────────────────────┐  │
│  │         MessageBus                               │  │
│  │  - UIDescriptionMessage (Plugin → UI)            │  │
│  │  - UIEventMessage (UI → Plugin)                  │  │
│  │  - DataUpdateMessage (Plugin → UI)               │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│                     ▼                                   │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Plugins (Business Logic)                        │  │
│  │  ┌────────────────┐  ┌──────────────────┐       │  │
│  │  │ AutosteerPlugin│  │ MapPlugin        │       │  │
│  │  │ - Publishes UI │  │ - Publishes UI   │       │  │
│  │  │   description  │  │   description    │       │  │
│  │  │ - Subscribes   │  │ - Subscribes to  │       │  │
│  │  │   to UI events │  │   UI events      │       │  │
│  │  └────────────────┘  └──────────────────┘       │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## Message Definitions

### 1. UI Description Messages

```csharp
// AgOpenGPS.PluginContracts/Messages/UIMessages.cs

/// <summary>
/// Plugin describes its UI panel declaratively
/// </summary>
public struct UIDescriptionMessage
{
    public string PluginName;           // Owner plugin
    public string PanelId;              // Unique panel ID
    public string Title;                // Panel title
    public UILocation Location;         // Where to show
    public UIElement[] Elements;        // UI elements to create
    public long TimestampMs;
}

/// <summary>
/// Where to place the UI panel
/// </summary>
public enum UILocation
{
    MainDashboard,      // Main content area
    LeftPanel,          // Left sidebar
    RightPanel,         // Right sidebar
    BottomPanel,        // Bottom status area
    SettingsDialog,     // Settings window
    FloatingWindow      // Separate window
}

/// <summary>
/// Declarative UI element description
/// </summary>
public struct UIElement
{
    public string Id;               // Unique element ID
    public UIElementType Type;      // What kind of control
    public string Label;            // Display text
    public string DataBinding;      // What data to show
    public UIElement[] Children;    // Child elements (for containers)
    public Dictionary<string, object> Properties;  // Type-specific properties
}

/// <summary>
/// Types of UI elements
/// </summary>
public enum UIElementType
{
    // Containers
    Panel,
    Grid,
    Stack,

    // Display
    Label,
    Value,
    ProgressBar,
    Chart,
    Gauge,

    // Input
    Button,
    Slider,
    TextBox,
    CheckBox,
    ComboBox,

    // Special
    Separator,
    Spacer
}
```

### 2. UI Event Messages

```csharp
/// <summary>
/// User interacted with UI element (UI → Plugin)
/// </summary>
public struct UIEventMessage
{
    public string PluginName;       // Target plugin
    public string PanelId;          // Source panel
    public string ElementId;        // Source element
    public UIEventType EventType;   // What happened
    public object? Value;           // New value (if applicable)
    public long TimestampMs;
}

public enum UIEventType
{
    ButtonClick,
    ValueChanged,
    SliderMoved,
    CheckboxToggled,
    TextEntered,
    ComboBoxSelected
}
```

### 3. Data Update Messages

```csharp
/// <summary>
/// Plugin updates data shown in UI (Plugin → UI)
/// </summary>
public struct UIDataUpdateMessage
{
    public string PluginName;
    public string PanelId;
    public string ElementId;        // Which element to update
    public object Value;            // New value
    public long TimestampMs;
}
```

---

## Example 1: Autosteer Plugin with UI Description

```csharp
// AgOpenGPS.Plugins.Autosteer/AutosteerPlugin.cs

public class AutosteerPlugin : IAgPlugin
{
    private const string PANEL_ID = "autosteer_control";
    private bool _engaged = false;
    private double _currentSteerAngle = 0.0;
    private double _lateralError = 0.0;

    public Task InitializeAsync(IPluginContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to GPS for control logic
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        // Subscribe to UI events
        _messageBus.Subscribe<UIEventMessage>(OnUIEvent);

        // Describe our UI
        PublishUIDescription();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Tell the UI module what controls we need
    /// </summary>
    private void PublishUIDescription()
    {
        var uiDescription = new UIDescriptionMessage
        {
            PluginName = Name,
            PanelId = PANEL_ID,
            Title = "Autosteer Control",
            Location = UILocation.MainDashboard,
            Elements = new[]
            {
                // Status section
                new UIElement
                {
                    Id = "status_group",
                    Type = UIElementType.Panel,
                    Label = "Status",
                    Children = new[]
                    {
                        new UIElement
                        {
                            Id = "engagement_status",
                            Type = UIElementType.Label,
                            Label = "Status:",
                            DataBinding = "engagement_status",
                            Properties = new Dictionary<string, object>
                            {
                                ["FontSize"] = 14,
                                ["FontWeight"] = "Bold"
                            }
                        },
                        new UIElement
                        {
                            Id = "steer_angle_value",
                            Type = UIElementType.Value,
                            Label = "Steer Angle:",
                            DataBinding = "steer_angle",
                            Properties = new Dictionary<string, object>
                            {
                                ["Unit"] = "°",
                                ["Format"] = "F2",
                                ["FontFamily"] = "Consolas"
                            }
                        },
                        new UIElement
                        {
                            Id = "lateral_error_value",
                            Type = UIElementType.Value,
                            Label = "Lateral Error:",
                            DataBinding = "lateral_error",
                            Properties = new Dictionary<string, object>
                            {
                                ["Unit"] = "m",
                                ["Format"] = "F3"
                            }
                        },
                        new UIElement
                        {
                            Id = "error_gauge",
                            Type = UIElementType.Gauge,
                            Label = "Cross Track Error",
                            DataBinding = "lateral_error",
                            Properties = new Dictionary<string, object>
                            {
                                ["Min"] = -2.0,
                                ["Max"] = 2.0,
                                ["GreenZone"] = new[] { -0.1, 0.1 },
                                ["YellowZone"] = new[] { -0.5, 0.5 }
                            }
                        }
                    }
                },

                new UIElement
                {
                    Id = "separator1",
                    Type = UIElementType.Separator
                },

                // Control section
                new UIElement
                {
                    Id = "control_group",
                    Type = UIElementType.Panel,
                    Label = "Control",
                    Children = new[]
                    {
                        new UIElement
                        {
                            Id = "engage_button",
                            Type = UIElementType.Button,
                            Label = "Engage Autosteer",
                            Properties = new Dictionary<string, object>
                            {
                                ["Style"] = "Primary",
                                ["Width"] = 200,
                                ["Height"] = 40
                            }
                        },
                        new UIElement
                        {
                            Id = "disengage_button",
                            Type = UIElementType.Button,
                            Label = "Disengage",
                            Properties = new Dictionary<string, object>
                            {
                                ["Style"] = "Secondary",
                                ["Width"] = 200,
                                ["Height"] = 40
                            }
                        }
                    }
                },

                new UIElement
                {
                    Id = "separator2",
                    Type = UIElementType.Separator
                },

                // Settings section
                new UIElement
                {
                    Id = "settings_group",
                    Type = UIElementType.Panel,
                    Label = "PID Tuning",
                    Children = new[]
                    {
                        new UIElement
                        {
                            Id = "kp_slider",
                            Type = UIElementType.Slider,
                            Label = "Kp (Proportional)",
                            DataBinding = "kp",
                            Properties = new Dictionary<string, object>
                            {
                                ["Min"] = 0.0,
                                ["Max"] = 10.0,
                                ["Step"] = 0.1,
                                ["DefaultValue"] = 2.0
                            }
                        },
                        new UIElement
                        {
                            Id = "ki_slider",
                            Type = UIElementType.Slider,
                            Label = "Ki (Integral)",
                            DataBinding = "ki",
                            Properties = new Dictionary<string, object>
                            {
                                ["Min"] = 0.0,
                                ["Max"] = 1.0,
                                ["Step"] = 0.01,
                                ["DefaultValue"] = 0.1
                            }
                        },
                        new UIElement
                        {
                            Id = "kd_slider",
                            Type = UIElementType.Slider,
                            Label = "Kd (Derivative)",
                            DataBinding = "kd",
                            Properties = new Dictionary<string, object>
                            {
                                ["Min"] = 0.0,
                                ["Max"] = 5.0,
                                ["Step"] = 0.1,
                                ["DefaultValue"] = 0.5
                            }
                        }
                    }
                }
            },
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _messageBus.Publish(in uiDescription);
        _logger.LogInformation("Published UI description");
    }

    /// <summary>
    /// Handle UI interactions
    /// </summary>
    private void OnUIEvent(UIEventMessage evt)
    {
        if (evt.PluginName != Name || evt.PanelId != PANEL_ID)
            return;

        switch (evt.ElementId)
        {
            case "engage_button":
                if (evt.EventType == UIEventType.ButtonClick)
                {
                    _engaged = true;
                    _logger.LogInformation("Autosteer engaged");
                    UpdateEngagementStatus();
                }
                break;

            case "disengage_button":
                if (evt.EventType == UIEventType.ButtonClick)
                {
                    _engaged = false;
                    _logger.LogInformation("Autosteer disengaged");
                    UpdateEngagementStatus();
                }
                break;

            case "kp_slider":
                if (evt.EventType == UIEventType.SliderMoved && evt.Value is double kp)
                {
                    _settings.Kp = kp;
                    _logger.LogInformation($"Kp updated to {kp:F2}");
                }
                break;

            case "ki_slider":
                if (evt.EventType == UIEventType.SliderMoved && evt.Value is double ki)
                {
                    _settings.Ki = ki;
                    _logger.LogInformation($"Ki updated to {ki:F3}");
                }
                break;

            case "kd_slider":
                if (evt.EventType == UIEventType.SliderMoved && evt.Value is double kd)
                {
                    _settings.Kd = kd;
                    _logger.LogInformation($"Kd updated to {kd:F2}");
                }
                break;
        }
    }

    /// <summary>
    /// Update UI with current data
    /// </summary>
    private void UpdateEngagementStatus()
    {
        _messageBus.Publish(new UIDataUpdateMessage
        {
            PluginName = Name,
            PanelId = PANEL_ID,
            ElementId = "engagement_status",
            Value = _engaged ? "ENGAGED" : "Disengaged",
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <summary>
    /// Called every time we calculate steering
    /// </summary>
    private void OnGpsPosition(GpsPositionMessage gps)
    {
        if (!_engaged) return;

        // Calculate steering (existing logic)
        _currentSteerAngle = CalculateSteerAngle(gps);
        _lateralError = CalculateLateralError(gps);

        // Update UI
        _messageBus.Publish(new UIDataUpdateMessage
        {
            PluginName = Name,
            PanelId = PANEL_ID,
            ElementId = "steer_angle",
            Value = _currentSteerAngle,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        _messageBus.Publish(new UIDataUpdateMessage
        {
            PluginName = Name,
            PanelId = PANEL_ID,
            ElementId = "lateral_error",
            Value = _lateralError,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    // ... existing autosteer logic ...
}
```

---

## Example 2: UI Module - Dynamic UI Builder

```csharp
// AgOpenGPS.GUI/Services/UIBuilder.cs

public class UIBuilder
{
    private readonly IMessageBus _messageBus;
    private readonly Dictionary<string, Panel> _panels = new();
    private readonly Dictionary<string, Control> _controls = new();

    public UIBuilder(IMessageBus messageBus)
    {
        _messageBus = messageBus;

        // Subscribe to UI description messages
        _messageBus.Subscribe<UIDescriptionMessage>(OnUIDescription);

        // Subscribe to data updates
        _messageBus.Subscribe<UIDataUpdateMessage>(OnDataUpdate);
    }

    /// <summary>
    /// Plugin sent UI description - build the interface
    /// </summary>
    private void OnUIDescription(UIDescriptionMessage msg)
    {
        // Create panel container
        var panel = new StackPanel
        {
            Name = msg.PanelId,
            Margin = new Thickness(10)
        };

        // Add title
        panel.Children.Add(new TextBlock
        {
            Text = msg.Title,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Build UI elements recursively
        foreach (var element in msg.Elements)
        {
            var control = BuildElement(element, msg.PluginName, msg.PanelId);
            if (control != null)
            {
                panel.Children.Add(control);
            }
        }

        // Store panel
        _panels[msg.PanelId] = panel;

        // Add to main window based on location
        AddPanelToWindow(msg.Location, panel);
    }

    /// <summary>
    /// Build a single UI element
    /// </summary>
    private Control? BuildElement(UIElement element, string pluginName, string panelId)
    {
        Control? control = element.Type switch
        {
            UIElementType.Panel => BuildPanel(element),
            UIElementType.Label => BuildLabel(element),
            UIElementType.Value => BuildValue(element),
            UIElementType.Button => BuildButton(element, pluginName, panelId),
            UIElementType.Slider => BuildSlider(element, pluginName, panelId),
            UIElementType.Gauge => BuildGauge(element),
            UIElementType.Separator => new Separator { Margin = new Thickness(0, 10, 0, 10) },
            UIElementType.Spacer => new Border { Height = 10 },
            _ => null
        };

        if (control != null)
        {
            control.Name = element.Id;
            _controls[$"{panelId}.{element.Id}"] = control;
        }

        return control;
    }

    private Control BuildPanel(UIElement element)
    {
        var panel = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 5, 0, 5)
        };

        var stack = new StackPanel();

        // Add label if provided
        if (!string.IsNullOrEmpty(element.Label))
        {
            stack.Children.Add(new TextBlock
            {
                Text = element.Label,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }

        // Add children
        foreach (var child in element.Children ?? Array.Empty<UIElement>())
        {
            var childControl = BuildElement(child, "", "");
            if (childControl != null)
            {
                stack.Children.Add(childControl);
            }
        }

        panel.Child = stack;
        return panel;
    }

    private Control BuildLabel(UIElement element)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var label = new TextBlock
        {
            Text = element.Label,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);

        var value = new TextBlock
        {
            Name = $"{element.Id}_value",
            Text = "N/A",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        ApplyProperties(value, element.Properties);
        Grid.SetColumn(value, 1);

        grid.Children.Add(label);
        grid.Children.Add(value);

        return grid;
    }

    private Control BuildValue(UIElement element)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            Margin = new Thickness(0, 3, 0, 3)
        };

        var label = new TextBlock
        {
            Text = element.Label,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);

        var value = new TextBlock
        {
            Name = $"{element.Id}_value",
            Text = "0.00",
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };

        ApplyProperties(value, element.Properties);
        Grid.SetColumn(value, 1);

        grid.Children.Add(label);
        grid.Children.Add(value);

        return grid;
    }

    private Control BuildButton(UIElement element, string pluginName, string panelId)
    {
        var button = new Button
        {
            Content = element.Label,
            Margin = new Thickness(0, 5, 0, 5)
        };

        ApplyProperties(button, element.Properties);

        // Wire up click event
        button.Click += (sender, args) =>
        {
            _messageBus.Publish(new UIEventMessage
            {
                PluginName = pluginName,
                PanelId = panelId,
                ElementId = element.Id,
                EventType = UIEventType.ButtonClick,
                Value = null,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        };

        return button;
    }

    private Control BuildSlider(UIElement element, string pluginName, string panelId)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

        // Label with current value
        var labelGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var label = new TextBlock
        {
            Text = element.Label,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);

        var valueText = new TextBlock
        {
            Name = $"{element.Id}_value",
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 1);

        labelGrid.Children.Add(label);
        labelGrid.Children.Add(valueText);
        stack.Children.Add(labelGrid);

        // Slider
        var slider = new Slider
        {
            Minimum = GetProperty<double>(element.Properties, "Min", 0),
            Maximum = GetProperty<double>(element.Properties, "Max", 100),
            TickFrequency = GetProperty<double>(element.Properties, "Step", 1),
            Value = GetProperty<double>(element.Properties, "DefaultValue", 0),
            Margin = new Thickness(0, 5, 0, 0)
        };

        // Update value display
        valueText.Text = slider.Value.ToString("F2");

        // Wire up value changed event
        slider.PropertyChanged += (sender, args) =>
        {
            if (args.Property.Name == nameof(Slider.Value))
            {
                valueText.Text = slider.Value.ToString("F2");

                _messageBus.Publish(new UIEventMessage
                {
                    PluginName = pluginName,
                    PanelId = panelId,
                    ElementId = element.Id,
                    EventType = UIEventType.SliderMoved,
                    Value = slider.Value,
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        };

        stack.Children.Add(slider);
        return stack;
    }

    private Control BuildGauge(UIElement element)
    {
        // Simple gauge implementation (could use a custom control)
        var border = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Height = 60,
            Margin = new Thickness(0, 5, 0, 5)
        };

        var canvas = new Canvas
        {
            Name = $"{element.Id}_canvas"
        };

        border.Child = canvas;
        return border;
    }

    /// <summary>
    /// Plugin sent data update - update the control
    /// </summary>
    private void OnDataUpdate(UIDataUpdateMessage msg)
    {
        var controlKey = $"{msg.PanelId}.{msg.ElementId}";

        if (!_controls.TryGetValue(controlKey, out var control))
        {
            // Try with _value suffix (for label/value pairs)
            controlKey = $"{msg.PanelId}.{msg.ElementId}_value";
            if (!_controls.TryGetValue(controlKey, out control))
                return;
        }

        // Update based on control type
        Dispatcher.UIThread.Post(() =>
        {
            if (control is TextBlock textBlock)
            {
                textBlock.Text = msg.Value?.ToString() ?? "N/A";
            }
            else if (control is Slider slider)
            {
                if (msg.Value is double d)
                    slider.Value = d;
            }
            // ... handle other control types
        });
    }

    private void ApplyProperties(Control control, Dictionary<string, object>? props)
    {
        if (props == null) return;

        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "FontSize" when value is int fs:
                    if (control is TextBlock tb1) tb1.FontSize = fs;
                    break;
                case "FontWeight" when value is string fw:
                    if (control is TextBlock tb2)
                        tb2.FontWeight = fw == "Bold" ? FontWeight.Bold : FontWeight.Normal;
                    break;
                case "FontFamily" when value is string ff:
                    control.FontFamily = new FontFamily(ff);
                    break;
                case "Width" when value is int w:
                    control.Width = w;
                    break;
                case "Height" when value is int h:
                    control.Height = h;
                    break;
            }
        }
    }

    private T GetProperty<T>(Dictionary<string, object>? props, string key, T defaultValue)
    {
        if (props == null || !props.TryGetValue(key, out var value))
            return defaultValue;

        return value is T typedValue ? typedValue : defaultValue;
    }

    private void AddPanelToWindow(UILocation location, Panel panel)
    {
        // Find main window and add panel to appropriate location
        var mainWindow = Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow as MainWindow
            : null;

        if (mainWindow == null) return;

        switch (location)
        {
            case UILocation.MainDashboard:
                mainWindow.MainDashboardArea.Children.Add(panel);
                break;
            case UILocation.LeftPanel:
                mainWindow.LeftPanelArea.Children.Add(panel);
                break;
            case UILocation.RightPanel:
                mainWindow.RightPanelArea.Children.Add(panel);
                break;
            // ... other locations
        }
    }
}
```

---

## Benefits of This Pattern

### ✅ Complete Decoupling
- Plugin has **zero dependency** on UI framework
- Plugin can run **headless** (no UI module loaded)
- UI can be **replaced** (Avalonia → WPF → Web) without changing plugins

### ✅ Network Transparency
- Messages could be sent **over gRPC** to remote UI
- Same plugin works for local UI or web dashboard

### ✅ UI-Less Testing
- Test plugins without UI loaded
- Verify UIDescriptionMessages in unit tests

### ✅ Dynamic UI
- UI builds at runtime based on loaded plugins
- No compile-time dependency

### ✅ Multiple UIs
- Desktop UI, mobile UI, web UI can all subscribe to same messages
- Each renders controls differently

---

## Example 3: Simple Map Plugin

```csharp
public class MapPlugin : IAgPlugin
{
    private const string PANEL_ID = "map_view";

    private void PublishUIDescription()
    {
        var uiDescription = new UIDescriptionMessage
        {
            PluginName = Name,
            PanelId = PANEL_ID,
            Title = "Field Map",
            Location = UILocation.MainDashboard,
            Elements = new[]
            {
                new UIElement
                {
                    Id = "map_control",
                    Type = UIElementType.Chart,  // Custom map control
                    Properties = new Dictionary<string, object>
                    {
                        ["Width"] = 800,
                        ["Height"] = 600,
                        ["BackgroundColor"] = "#F5F5F5",
                        ["ShowGrid"] = true,
                        ["AllowZoom"] = true
                    }
                },
                new UIElement
                {
                    Id = "zoom_controls",
                    Type = UIElementType.Panel,
                    Children = new[]
                    {
                        new UIElement
                        {
                            Id = "zoom_in",
                            Type = UIElementType.Button,
                            Label = "+"
                        },
                        new UIElement
                        {
                            Id = "zoom_out",
                            Type = UIElementType.Button,
                            Label = "-"
                        },
                        new UIElement
                        {
                            Id = "center_button",
                            Type = UIElementType.Button,
                            Label = "Center on Vehicle"
                        }
                    }
                }
            },
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _messageBus.Publish(in uiDescription);
    }
}
```

---

## Comparison with Other Patterns

| Aspect | Direct Plugin UI | UI Bridge (gRPC) | **UI Description** |
|--------|------------------|------------------|-------------------|
| **Coupling** | High | Low | **None** |
| **Network Support** | No | Yes | **Yes** |
| **UI Framework** | Fixed (Avalonia) | Any | **Any** |
| **Complexity** | Low | High | **Medium** |
| **Performance** | Fast | Medium | **Fast** |
| **Testability** | Hard | Medium | **Easy** |

---

## When to Use This Pattern

### ✅ Use When:
- Building a **platform** (multiple UIs: desktop, mobile, web)
- Need **UI framework independence** (might switch from Avalonia to Blazor later)
- Plugins might run **headless** (no UI at all)
- Want **network-transparent** UI (remote dashboard)
- Building **commercial product** with multiple UI clients

### ❌ Don't Use When:
- Simple single-UI application (current monolithic is fine)
- UI is fixed and won't change
- Performance is absolutely critical (direct binding is faster)
- Team is small and UI complexity isn't worth it

---

## Conclusion

The **UI Description Pattern** provides:

1. **Zero coupling** between plugins and UI framework
2. **Message-based** communication (fits existing architecture)
3. **Multiple UI support** (desktop, mobile, web from same plugins)
4. **Testable** (verify UI descriptions without actual UI)
5. **Network transparent** (works over gRPC like Nexus)

This is a **hybrid approach** between your current monolithic UI and Nexus's full multi-process architecture. You get the benefits of decoupling without the complexity of multiple processes!
