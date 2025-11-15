namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;

/// <summary>
/// Real-time implementation of ITimeProvider using system clock
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return Task.Delay(duration, cancellationToken);
    }
}
