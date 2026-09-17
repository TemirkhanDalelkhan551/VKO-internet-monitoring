using Microsoft.Extensions.Logging;
using VkoMonitoring.Agent.Infrastructure.Diagnostics;

namespace VkoMonitoring.Agent.Logging;

public sealed class RollingFileLoggerProvider(RollingTextFileWriter writer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, writer);

    public void Dispose()
    {
    }

    private sealed class RollingFileLogger(
        string categoryName,
        RollingTextFileWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var message = formatter(state, exception);
            var eventSuffix = eventId.Id == 0 ? string.Empty : $" [{eventId.Id}]";
            var exceptionSuffix = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
            writer.WriteLine($"{timestamp} {logLevel,-11} {categoryName}{eventSuffix}: {message}{exceptionSuffix}");
        }
    }
}
