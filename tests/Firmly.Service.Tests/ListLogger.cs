using Microsoft.Extensions.Logging;

namespace Firmly.Service.Tests;

/// <summary>Keeps formatted log messages at Warning and above.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            Warnings.Add(formatter(state, exception));
    }
}
