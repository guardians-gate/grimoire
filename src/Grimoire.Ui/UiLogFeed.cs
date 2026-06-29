using Microsoft.Extensions.Logging;

namespace Grimoire.Ui;

/// <summary>
/// Represents a single log entry captured for in-app UI diagnostics.
/// </summary>
/// <param name="Level">The log level indicating severity.</param>
/// <param name="Category">The logger category representing the source component.</param>
/// <param name="Message">The rendered log message representing the event details.</param>
/// <param name="Exception">The optional exception representing failure context for the log entry.</param>
public sealed record UiLogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>
/// Represents event arguments carrying a <see cref="UiLogEntry"/> raised by the UI log feed.
/// </summary>
public sealed class UiLogEntryEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new event-args instance for a published UI log entry.
    /// </summary>
    /// <param name="entry">The entry representing the published log message.</param>
    public UiLogEntryEventArgs(UiLogEntry entry)
    {
        Entry = entry;
    }

    /// <summary>
    /// Gets a <see cref="UiLogEntry"/> representing the log payload associated with the raised event.
    /// </summary>
    public UiLogEntry Entry { get; }
}

/// <summary>
/// Represents an in-memory log sink that broadcasts the most recent diagnostic entry to UI subscribers.
/// </summary>
public sealed class UiLogFeed
{
    /// <summary>
    /// An <see cref="EventHandler{TEventArgs}"/> representing subscribers that should be notified when a new UI log entry is published.
    /// </summary>
    public event EventHandler<UiLogEntryEventArgs>? EntryWritten;

    /// <summary>
    /// Gets a <see cref="UiLogEntry"/> representing the latest message published to the feed.
    /// </summary>
    public UiLogEntry? LatestEntry { get; private set; }

    /// <summary>
    /// Publishes a UI log entry to listeners and updates the latest-entry snapshot.
    /// </summary>
    /// <param name="entry">The entry representing the message to broadcast.</param>
    public void Publish(UiLogEntry entry)
    {
        LatestEntry = entry;
        EntryWritten?.Invoke(this, new UiLogEntryEventArgs(entry));
    }
}

/// <summary>
/// Represents an <see cref="ILoggerProvider"/> that routes logs into a shared <see cref="UiLogFeed"/>.
/// </summary>
internal sealed class UiLogFeedLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// A <see cref="UiLogFeed"/> representing the destination feed for forwarded logger messages.
    /// </summary>
    private readonly UiLogFeed _feed;

    /// <summary>
    /// Initializes a logger provider that writes entries into the supplied UI log feed.
    /// </summary>
    /// <param name="feed">The feed representing where logger output should be published.</param>
    public UiLogFeedLoggerProvider(UiLogFeed feed)
    {
        _feed = feed;
    }

    /// <summary>
    /// Creates a category-scoped logger and returns an <see cref="ILogger"/> representing a feed-backed logger implementation.
    /// </summary>
    /// <param name="categoryName">The category name representing the logical logger source.</param>
    /// <returns>An <see cref="ILogger"/> representing a logger that publishes into the UI feed.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return new UiLogFeedLogger(categoryName, _feed);
    }

    /// <summary>
    /// Releases provider resources.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Represents a category-specific logger that forwards formatted entries to <see cref="UiLogFeed"/>.
    /// </summary>
    private sealed class UiLogFeedLogger : ILogger
    {
        /// <summary>
        /// A <see cref="string"/> representing the logger category associated with emitted entries.
        /// </summary>
        private readonly string _categoryName;

        /// <summary>
        /// A <see cref="UiLogFeed"/> representing the destination feed for category log entries.
        /// </summary>
        private readonly UiLogFeed _feed;

        /// <summary>
        /// Initializes a logger that publishes messages under a specific category into the shared UI feed.
        /// </summary>
        /// <param name="categoryName">The category name representing the originating component.</param>
        /// <param name="feed">The feed representing where log entries should be written.</param>
        public UiLogFeedLogger(string categoryName, UiLogFeed feed)
        {
            _categoryName = categoryName;
            _feed = feed;
        }

        /// <summary>
        /// Begins a logging scope and returns an <see cref="IDisposable"/> representing a no-op scope token.
        /// </summary>
        /// <typeparam name="TState">The scope-state type representing contextual logging metadata.</typeparam>
        /// <param name="state">The state object representing scope context.</param>
        /// <returns>An <see cref="IDisposable"/> representing the created scope token.</returns>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <summary>
        /// Determines whether the provided level is enabled and returns a <see cref="bool"/> indicating whether the logger should emit entries for that severity.
        /// </summary>
        /// <param name="logLevel">The log level indicating the entry severity to evaluate.</param>
        /// <returns><see langword="true"/> indicating the level is enabled; otherwise, <see langword="false"/>.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is not LogLevel.None and >= LogLevel.Debug;
        }

        /// <summary>
        /// Formats and writes a log entry into the UI feed and returns <see langword="void"/>.
        /// </summary>
        /// <typeparam name="TState">The state type representing structured logging payload data.</typeparam>
        /// <param name="logLevel">The severity level indicating how important the entry is.</param>
        /// <param name="eventId">The event identifier indicating the logical event type.</param>
        /// <param name="state">The state payload representing structured message data.</param>
        /// <param name="exception">The optional exception representing failure context.</param>
        /// <param name="formatter">The formatter function representing how state should be rendered into message text.</param>
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

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _feed.Publish(new UiLogEntry(logLevel, _categoryName, message, exception));
        }
    }

    /// <summary>
    /// Represents a no-op logging scope used when structured scope tracking is not required.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>
        /// Gets a <see cref="NullScope"/> representing the singleton no-op scope instance.
        /// </summary>
        public static NullScope Instance { get; } = new();

        /// <summary>
        /// Ends the no-op scope and returns <see langword="void"/>.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
