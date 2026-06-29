using Microsoft.Extensions.Logging;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Represents unit tests for workspace tab, navigation, and UI log-feed integration behavior.
/// </summary>
public sealed class EditorWorkspaceStateTests
{
    /// <summary>
    /// Verifies that back/forward navigation tracks line-aware editor locations and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void TracksLineAwareBackAndForwardLocations()
    {
        EditorWorkspaceState state = new();

        state.RecordLocation(new EditorLocation("content/001.md", 1));
        state.RecordLocation(new EditorLocation("content/001.md", 12));
        state.RecordLocation(new EditorLocation("snippets/goblin.json", 3));

        Assert.True(state.CanNavigateBack);
        EditorLocation? firstBack = state.NavigateBack(new EditorLocation("snippets/goblin.json", 3));
        Assert.Equal(new EditorLocation("content/001.md", 12), firstBack);

        EditorLocation? secondBack = state.NavigateBack(firstBack!);
        Assert.Equal(new EditorLocation("content/001.md", 1), secondBack);

        Assert.True(state.CanNavigateForward);
        EditorLocation? forward = state.NavigateForward(secondBack!);
        Assert.Equal(new EditorLocation("content/001.md", 12), forward);
    }

    /// <summary>
    /// Verifies that dirty tab content and caret position are preserved when tab focus changes and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void TabsPreserveDirtyContentWhenSwitching()
    {
        EditorWorkspaceState state = new();
        state.OpenOrActivate("content/001.md", "content-markdown", "# One", 1);
        state.UpdateActiveContent("# One\nEdited", 2, isDirty: true);
        state.OpenOrActivate("snippets/goblin.json", "json", "{\"name\":\"Goblin\"}", 1);

        EditorTabState? first = state.FindTab("content/001.md");
        Assert.NotNull(first);
        Assert.True(first!.IsDirty);
        Assert.Equal("# One\nEdited", first.Content);
        Assert.Equal(2, first.LineNumber);
    }

    /// <summary>
    /// Verifies that the UI log provider forwards formatted logger messages into the feed and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void UiLogFeedProviderPublishesFormattedLogMessages()
    {
        UiLogFeed feed = new();
        using UiLogFeedLoggerProvider provider = new(feed);
        ILogger logger = provider.CreateLogger("test");

        logger.Log(LogLevel.Information, new EventId(1, "Build"), "Building HTML", null, static (state, _) => state);

        Assert.NotNull(feed.LatestEntry);
        Assert.Equal(LogLevel.Information, feed.LatestEntry!.Level);
        Assert.Equal("Building HTML", feed.LatestEntry.Message);
    }
}
