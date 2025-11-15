namespace AgOpenGPS.Core;

using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;

/// <summary>
/// Provides safe execution wrappers for module operations with comprehensive exception handling
/// </summary>
public static class SafeModuleExecutor
{
    /// <summary>
    /// Execute a module operation with full exception protection
    /// </summary>
    public static async Task<OperationResult> ExecuteSafelyAsync(
        Func<Task> operation,
        string operationName,
        string moduleName,
        ILogger logger)
    {
        try
        {
            await operation();
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            logger.LogDebug($"{operationName} cancelled for module {moduleName}");
            return OperationResult.Cancelled();
        }
        catch (OutOfMemoryException ex)
        {
            // Critical - log and try to continue
            logger.LogCritical(ex, $"OUT OF MEMORY in {moduleName} during {operationName}");
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            return OperationResult.Failure($"Out of memory: {ex.Message}", isFatal: true);
        }
        catch (StackOverflowException ex)
        {
            // Cannot actually catch this, but included for documentation
            logger.LogCritical(ex, $"STACK OVERFLOW in {moduleName} during {operationName} - PROCESS WILL CRASH");
            return OperationResult.Failure("Stack overflow - process terminating", isFatal: true);
        }
        catch (AccessViolationException ex)
        {
            // Native memory corruption
            logger.LogCritical(ex, $"ACCESS VIOLATION in {moduleName} during {operationName}");
            return OperationResult.Failure($"Access violation (native crash): {ex.Message}", isFatal: true);
        }
        catch (TypeInitializationException ex)
        {
            // Static constructor failed
            logger.LogError(ex, $"Type initialization failed in {moduleName} during {operationName}");
            return OperationResult.Failure($"Type initialization error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (TypeLoadException ex)
        {
            // Assembly loading issue
            logger.LogError(ex, $"Type load failed in {moduleName} during {operationName}");
            return OperationResult.Failure($"Type load error: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            // File/IO errors
            logger.LogError(ex, $"IO error in {moduleName} during {operationName}");
            return OperationResult.Failure($"IO error: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            // Permission issues
            logger.LogError(ex, $"Access denied in {moduleName} during {operationName}");
            return OperationResult.Failure($"Access denied: {ex.Message}");
        }
        catch (AggregateException ex)
        {
            // Multiple exceptions from tasks
            logger.LogError(ex, $"Multiple errors in {moduleName} during {operationName}");
            var messages = string.Join("; ", ex.InnerExceptions.Select(e => e.Message));
            return OperationResult.Failure($"Multiple errors: {messages}");
        }
        catch (Exception ex) when (IsFatalException(ex))
        {
            // Fatal exceptions that we can catch but indicate serious problems
            logger.LogCritical(ex, $"FATAL EXCEPTION in {moduleName} during {operationName}");
            return OperationResult.Failure($"Fatal error: {ex.Message}", isFatal: true);
        }
        catch (Exception ex)
        {
            // All other managed exceptions
            logger.LogError(ex, $"Error in {moduleName} during {operationName}");
            return OperationResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a synchronous module operation with full exception protection
    /// </summary>
    public static OperationResult ExecuteSafely(
        Action operation,
        string operationName,
        string moduleName,
        ILogger logger)
    {
        try
        {
            operation();
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug($"{operationName} cancelled for module {moduleName}");
            return OperationResult.Cancelled();
        }
        catch (OutOfMemoryException ex)
        {
            logger.LogCritical(ex, $"OUT OF MEMORY in {moduleName} during {operationName}");
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            return OperationResult.Failure($"Out of memory: {ex.Message}", isFatal: true);
        }
        catch (AccessViolationException ex)
        {
            logger.LogCritical(ex, $"ACCESS VIOLATION in {moduleName} during {operationName}");
            return OperationResult.Failure($"Access violation (native crash): {ex.Message}", isFatal: true);
        }
        catch (Exception ex) when (IsFatalException(ex))
        {
            logger.LogCritical(ex, $"FATAL EXCEPTION in {moduleName} during {operationName}");
            return OperationResult.Failure($"Fatal error: {ex.Message}", isFatal: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error in {moduleName} during {operationName}");
            return OperationResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a module operation with timeout protection
    /// </summary>
    public static async Task<OperationResult> ExecuteWithTimeoutAsync(
        Func<Task> operation,
        TimeSpan timeout,
        string operationName,
        string moduleName,
        ILogger logger)
    {
        using var cts = new CancellationTokenSource();
        var operationTask = ExecuteSafelyAsync(operation, operationName, moduleName, logger);
        var timeoutTask = Task.Delay(timeout, cts.Token);

        var completedTask = await Task.WhenAny(operationTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            logger.LogWarning($"{operationName} timed out after {timeout.TotalSeconds}s for module {moduleName}");
            return OperationResult.Failure($"Operation timed out after {timeout.TotalSeconds}s");
        }

        cts.Cancel(); // Cancel the timeout task
        return await operationTask;
    }

    /// <summary>
    /// Determines if an exception is fatal and indicates a severe problem
    /// </summary>
    private static bool IsFatalException(Exception ex)
    {
        return ex is OutOfMemoryException
            || ex is StackOverflowException
            || ex is AccessViolationException
            || ex is AppDomainUnloadedException
            || ex is BadImageFormatException
            || ex is CannotUnloadAppDomainException
            || ex is InvalidProgramException
            || ex is System.Runtime.InteropServices.SEHException;
    }
}

/// <summary>
/// Result of a safe module operation
/// </summary>
public class OperationResult
{
    public bool IsSuccess { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsFatal { get; set; }
    public string? ErrorMessage { get; set; }

    public static OperationResult Success() => new() { IsSuccess = true };

    public static OperationResult Cancelled() => new() { IsCancelled = true };

    public static OperationResult Failure(string message, bool isFatal = false) =>
        new() { IsSuccess = false, ErrorMessage = message, IsFatal = isFatal };
}
