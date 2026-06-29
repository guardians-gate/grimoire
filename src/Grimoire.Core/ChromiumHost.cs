using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents a Chromium executable resolver that downloads and caches browser binaries for HTML rendering workflows.
/// </summary>
public sealed partial class ChromiumHost
{
    /// <summary>
    /// A <see cref="SemaphoreSlim"/> representing cross-call synchronization for browser download initialization.
    /// </summary>
    private static readonly SemaphoreSlim Lock = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// A <see cref="ChromiumHost"/> representing the default resolver instance used by static helper APIs.
    /// </summary>
    private static readonly ChromiumHost Default = new();

    /// <summary>
    /// A <see cref="string"/> representing the cached Chromium executable path for the current process.
    /// </summary>
    private static string? _cachedPath;

    /// <summary>
    /// A <see cref="ILogger"/> representing diagnostics for Chromium download and cache usage events.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a Chromium resolver instance with an optional logger.
    /// </summary>
    /// <param name="logger">The logger representing where resolver diagnostics should be emitted.</param>
    public ChromiumHost(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Ensures a Chromium executable is available and returns a <see cref="Task{TResult}"/> representing the resolved executable path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when resolution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path to the Chromium executable.</returns>
    public static Task<string> EnsureBrowserExecutableAsync(CancellationToken cancellationToken)
    {
        return Default.EnsureBrowserExecutableCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures a Chromium executable is available with an explicit logger and returns a <see cref="Task{TResult}"/> representing the resolved executable path.
    /// </summary>
    /// <param name="logger">The logger representing where resolver diagnostics should be emitted.</param>
    /// <param name="cancellationToken">The cancellation token indicating when resolution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path to the Chromium executable.</returns>
    public static Task<string> EnsureBrowserExecutableAsync(ILogger? logger, CancellationToken cancellationToken)
    {
        return new ChromiumHost(logger).EnsureBrowserExecutableCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the Chromium executable path, downloading the browser when missing, and returns a <see cref="Task{TResult}"/> representing the executable location.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when resolution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path to the Chromium executable.</returns>
    private async Task<string> EnsureBrowserExecutableCoreAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedPath) && File.Exists(_cachedPath))
        {
            UsingCachedExecutable(_cachedPath);
            return _cachedPath;
        }

        await Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_cachedPath) && File.Exists(_cachedPath))
            {
                UsingCachedExecutable(_cachedPath);
                return _cachedPath;
            }

            string cacheDirectory = Path.Combine(Path.GetTempPath(), "grimoire-browser-cache");
            BrowserFetcher fetcher = new(new BrowserFetcherOptions
            {
                Path = cacheDirectory,
                Browser = SupportedBrowser.Chrome,
            });

            DownloadingBrowser(cacheDirectory);
            InstalledBrowser browser = await fetcher.DownloadAsync().ConfigureAwait(false);
            _cachedPath = browser.GetExecutablePath();
            DownloadedBrowser(_cachedPath);
            return _cachedPath;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Logs that an already-cached Chromium executable path is being reused.
    /// </summary>
    /// <param name="path">The executable path representing the cached Chromium binary.</param>
    [LoggerMessage(EventId = 2090, Level = LogLevel.Debug, Message = "Using cached Chromium executable at {path}.")]
    private partial void UsingCachedExecutable(string path);

    /// <summary>
    /// Logs that Chromium download has started for a cache directory.
    /// </summary>
    /// <param name="cacheDirectory">The cache directory representing where browser binaries are downloaded.</param>
    [LoggerMessage(EventId = 2091, Level = LogLevel.Debug, Message = "Downloading Chromium browser to cache directory {cacheDirectory}.")]
    private partial void DownloadingBrowser(string cacheDirectory);

    /// <summary>
    /// Logs that Chromium download has completed and reports the resolved executable path.
    /// </summary>
    /// <param name="path">The executable path representing the downloaded Chromium binary.</param>
    [LoggerMessage(EventId = 2092, Level = LogLevel.Debug, Message = "Chromium executable resolved to {path}.")]
    private partial void DownloadedBrowser(string path);
}
