namespace AgOpenGPS.Plugins.SerialIO;

using System.Collections.Concurrent;
using System.IO.Ports;
using AgOpenGPS.PluginContracts;
using AgOpenGPS.PluginContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Bidirectional Serial/UDP communication plugin
/// Receives data from hardware and sends control commands
/// </summary>
public class SerialIOPlugin : IAgPlugin
{
    public string Name => "Serial IO";
    public Version Version => new Version(1, 0, 0);
    public PluginCategory Category => PluginCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    private SerialPort? _serialPort;
    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private CancellationToken _shutdownToken;
    private Task? _sendTask;

    public Task InitializeAsync(IPluginContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;
        _shutdownToken = context.AppShutdownToken;

        // Subscribe to outbound messages
        _messageBus.Subscribe<RawDataToSendMessage>(OnSendRequest);

        // Setup serial port
        var config = context.Configuration.GetSection("Plugins:SerialIO");
        var port = config.GetValue<string>("Port") ?? "COM3";
        var baudRate = config.GetValue<int>("BaudRate", 115200);

        _serialPort = new SerialPort(port, baudRate)
        {
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None
        };

        _logger.LogInformation($"Serial IO configured on {port} @ {baudRate}");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        if (_serialPort == null) return Task.CompletedTask;

        try
        {
            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
            _logger?.LogInformation("Serial port opened");

            // Start send loop
            _sendTask = Task.Run(SendLoop);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to open serial port: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _serialPort?.Close();
        _logger?.LogInformation("Serial port closed");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _serialPort?.Dispose();
        return Task.CompletedTask;
    }

    public PluginHealth GetHealth()
    {
        return _serialPort?.IsOpen == true ? PluginHealth.Healthy : PluginHealth.Unhealthy;
    }

    // INBOUND: Data received from hardware
    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null || _messageBus == null) return;

        try
        {
            int bytesToRead = _serialPort.BytesToRead;
            byte[] buffer = new byte[bytesToRead];
            _serialPort.Read(buffer, 0, bytesToRead);

            // Publish to message bus
            var message = new RawDataReceivedMessage
            {
                Data = buffer,
                Channel = IOChannel.Serial,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _messageBus.Publish(in message);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error receiving data: {ex.Message}");
        }
    }

    // OUTBOUND: Send request from other plugins
    private void OnSendRequest(RawDataToSendMessage msg)
    {
        if (msg.TargetChannel == IOChannel.Serial)
        {
            _sendQueue.Enqueue(msg.Data);
        }
    }

    // Send loop to avoid blocking
    private async Task SendLoop()
    {
        while (!_shutdownToken.IsCancellationRequested)
        {
            if (_sendQueue.TryDequeue(out var data))
            {
                try
                {
                    _serialPort?.Write(data, 0, data.Length);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error sending data: {ex.Message}");
                }
            }
            else
            {
                await Task.Delay(1, _shutdownToken);
            }
        }
    }
}
