using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Grimoire.Ui;

/// <summary>
/// Represents an editor cursor location within a project file.
/// </summary>
/// <param name="RelativePath">The relative path representing the file that owns the cursor location.</param>
/// <param name="LineNumber">The line number indicating the one-based cursor position.</param>
internal sealed record EditorLocation(string RelativePath, int LineNumber)
{
    /// <summary>
    /// Normalizes path and line values and returns an <see cref="EditorLocation"/> representing canonical editor navigation state.
    /// </summary>
    /// <returns>An <see cref="EditorLocation"/> representing normalized path and line values.</returns>
    public EditorLocation Normalize()
    {
        string path = RelativePath.Replace('\\', '/').TrimStart('/');
        return new(RelativePath: path, LineNumber: Math.Max(1, LineNumber));
    }
}

/// <summary>
/// Represents UI state for a single open editor tab.
/// </summary>
internal sealed class EditorTabState : INotifyPropertyChanged
{
    /// <summary>
    /// A <see cref="string"/> representing the mutable tab content text.
    /// </summary>
    private string _content;

    /// <summary>
    /// An <see cref="int"/> indicating the one-based active line number in the tab.
    /// </summary>
    private int _lineNumber;

    /// <summary>
    /// Initializes tab state for a project file.
    /// </summary>
    /// <param name="relativePath">The relative path representing the file opened by this tab.</param>
    /// <param name="kind">The kind string representing the file category.</param>
    /// <param name="content">The content string representing initial editor text.</param>
    /// <param name="lineNumber">The line number indicating the initial caret location.</param>
    public EditorTabState(string relativePath, string kind, string content, int lineNumber)
    {
        RelativePath = NormalizePath(relativePath);
        Kind = kind;
        DisplayName = Path.GetFileName(RelativePath);
        _content = content;
        _lineNumber = Math.Max(1, lineNumber);
    }

    /// <summary>
    /// An <see cref="PropertyChangedEventHandler"/> representing subscribers notified when tab properties change.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets a <see cref="string"/> representing the normalized relative file path for this tab.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets a <see cref="string"/> representing the file-kind identifier used for icon and behavior decisions.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets a <see cref="string"/> representing the display name shown for this tab.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a <see cref="string"/> representing the dirty-state marker displayed in tab UI chrome.
    /// </summary>
    public string DirtyMarker => IsDirty ? "*" : string.Empty;

    /// <summary>
    /// Gets a <see cref="Color"/> representing the background color used by the tab in current active state.
    /// </summary>
    public Color TabBackground => IsActive ? Color.FromArgb("#253138") : Color.FromArgb("#151B1E");

    /// <summary>
    /// Gets a <see cref="Color"/> representing the accent color used by the tab in current active state.
    /// </summary>
    public Color TabAccent => IsActive ? Color.FromArgb("#D9B76E") : Color.FromArgb("#46565D");

    /// <summary>
    /// Gets a <see cref="string"/> representing the icon glyph name associated with this tab kind.
    /// </summary>
    public string IconGlyph => Kind switch
    {
        "template" => "dashboard_customize",
        "authors" => "groups",
        "license" => "workspace_premium",
        "readme" => "menu_book",
        "sources" => "format_quote",
        "title" => "title",
        "snippet-markdown" => "edit_note",
        "content-markdown" => "article",
        "json" => "data_object",
        "settings" => "settings",
        "asset" => "image",
        _ => "description",
    };

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the editable document content for this tab.
    /// </summary>
    public string Content
    {
        get => _content;
        set
        {
            if (string.Equals(_content, value, StringComparison.Ordinal))
            {
                return;
            }

            _content = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets an <see cref="int"/> indicating the one-based caret line currently selected for this tab.
    /// </summary>
    public int LineNumber
    {
        get => _lineNumber;
        set
        {
            int lineNumber = Math.Max(1, value);
            if (_lineNumber == lineNumber)
            {
                return;
            }

            _lineNumber = lineNumber;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether this tab is currently the active editor tab.
    /// </summary>
    public bool IsActive
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabBackground));
            OnPropertyChanged(nameof(TabAccent));
        }
    }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether this tab has unsaved edits.
    /// </summary>
    public bool IsDirty
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirtyMarker));
        }
    }

    /// <summary>
    /// Normalizes a relative path and returns a <see cref="string"/> representing canonical project-path form.
    /// </summary>
    /// <param name="relativePath">The relative path representing a project file.</param>
    /// <returns>A <see cref="string"/> representing the normalized relative path.</returns>
    internal static string NormalizePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Raises a property-changed notification and returns <see langword="void"/>.
    /// </summary>
    /// <param name="propertyName">The property name representing the changed member.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents aggregate workspace state for open tabs and editor navigation history.
/// </summary>
internal sealed class EditorWorkspaceState
{
    /// <summary>
    /// A <see cref="Stack{T}"/> representing backward navigation history.
    /// </summary>
    private readonly Stack<EditorLocation> _backStack = new();

    /// <summary>
    /// A <see cref="Stack{T}"/> representing forward navigation history.
    /// </summary>
    private readonly Stack<EditorLocation> _forwardStack = new();

    /// <summary>
    /// An <see cref="EditorLocation"/> representing the most recently recorded navigation point.
    /// </summary>
    private EditorLocation? _lastRecordedLocation;

    /// <summary>
    /// Gets a <see cref="ObservableCollection{T}"/> representing the currently open editor tabs.
    /// </summary>
    public ObservableCollection<EditorTabState> OpenTabs { get; } = [];

    /// <summary>
    /// Gets a <see cref="EditorTabState"/> representing the currently active editor tab.
    /// </summary>
    public EditorTabState? ActiveTab { get; private set; }

    /// <summary>
    /// Gets a <see cref="bool"/> indicating whether backward navigation is currently available.
    /// </summary>
    public bool CanNavigateBack => _backStack.Count > 0;

    /// <summary>
    /// Gets a <see cref="bool"/> indicating whether forward navigation is currently available.
    /// </summary>
    public bool CanNavigateForward => _forwardStack.Count > 0;

    /// <summary>
    /// Opens or activates a tab and returns an <see cref="EditorTabState"/> representing the resulting active tab.
    /// </summary>
    /// <param name="relativePath">The relative path representing the file to open or activate.</param>
    /// <param name="kind">The kind string representing the file category.</param>
    /// <param name="content">The content string representing tab text when creating a new tab.</param>
    /// <param name="lineNumber">The line number indicating the requested caret position.</param>
    /// <returns>An <see cref="EditorTabState"/> representing the active tab after the operation.</returns>
    public EditorTabState OpenOrActivate(string relativePath, string kind, string content, int lineNumber)
    {
        string normalizedPath = EditorTabState.NormalizePath(relativePath);
        EditorTabState? tab = FindTab(normalizedPath);
        if (tab is null)
        {
            tab = new(normalizedPath, kind, content, lineNumber);
            OpenTabs.Add(tab);
        }

        tab.LineNumber = lineNumber;
        SetActiveTab(tab);
        return tab;
    }

    /// <summary>
    /// Finds an open tab by relative path and returns an <see cref="EditorTabState"/> representing the matching tab when found.
    /// </summary>
    /// <param name="relativePath">The relative path representing the file to search for.</param>
    /// <returns>An <see cref="EditorTabState"/> representing the matching open tab, or <see langword="null"/> when none exists.</returns>
    public EditorTabState? FindTab(string relativePath)
    {
        string normalizedPath = EditorTabState.NormalizePath(relativePath);
        foreach (EditorTabState tab in OpenTabs)
        {
            if (string.Equals(tab.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    /// <summary>
    /// Updates active-tab content, caret line, and dirty state and returns <see langword="void"/>.
    /// </summary>
    /// <param name="content">The content string representing the latest editor text.</param>
    /// <param name="lineNumber">The line number indicating the latest caret position.</param>
    /// <param name="isDirty">The value indicating whether the active tab has unsaved edits.</param>
    public void UpdateActiveContent(string content, int lineNumber, bool isDirty)
    {
        if (ActiveTab is null)
        {
            return;
        }

        ActiveTab.Content = content;
        ActiveTab.LineNumber = lineNumber;
        ActiveTab.IsDirty = isDirty;
    }

    /// <summary>
    /// Marks the active tab as clean with supplied content and line state and returns <see langword="void"/>.
    /// </summary>
    /// <param name="content">The content string representing persisted editor text.</param>
    /// <param name="lineNumber">The line number indicating the persisted caret position.</param>
    public void MarkActiveClean(string content, int lineNumber)
    {
        if (ActiveTab is null)
        {
            return;
        }

        ActiveTab.Content = content;
        ActiveTab.LineNumber = lineNumber;
        ActiveTab.IsDirty = false;
    }

    /// <summary>
    /// Closes a tab by relative path and returns an <see cref="EditorTabState"/> representing the new active tab.
    /// </summary>
    /// <param name="relativePath">The relative path representing the tab to close.</param>
    /// <returns>An <see cref="EditorTabState"/> representing the resulting active tab, or <see langword="null"/> when no tab remains active.</returns>
    public EditorTabState? Close(string relativePath)
    {
        string normalizedPath = EditorTabState.NormalizePath(relativePath);
        EditorTabState? tab = FindTab(normalizedPath);
        if (tab is null)
        {
            return ActiveTab;
        }

        int removedIndex = OpenTabs.IndexOf(tab);
        bool removedActive = ReferenceEquals(tab, ActiveTab);
        OpenTabs.Remove(tab);
        if (!removedActive)
        {
            return ActiveTab;
        }

        EditorTabState? next = OpenTabs.Count == 0
            ? null
            : OpenTabs[Math.Clamp(removedIndex, 0, OpenTabs.Count - 1)];
        SetActiveTab(next);
        return next;
    }

    /// <summary>
    /// Clears all workspace tab and navigation state and returns <see langword="void"/>.
    /// </summary>
    public void Clear()
    {
        OpenTabs.Clear();
        ActiveTab = null;
        _backStack.Clear();
        _forwardStack.Clear();
        _lastRecordedLocation = null;
    }

    /// <summary>
    /// Records a navigation location for back/forward tracking and returns <see langword="void"/>.
    /// </summary>
    /// <param name="location">The location representing the editor position to record.</param>
    public void RecordLocation(EditorLocation location)
    {
        EditorLocation normalized = location.Normalize();
        if (_lastRecordedLocation is null)
        {
            _lastRecordedLocation = normalized;
            return;
        }

        if (_lastRecordedLocation == normalized)
        {
            return;
        }

        _backStack.Push(_lastRecordedLocation);
        _forwardStack.Clear();
        _lastRecordedLocation = normalized;
    }

    /// <summary>
    /// Navigates backward in history and returns an <see cref="EditorLocation"/> representing the destination location.
    /// </summary>
    /// <param name="currentLocation">The current location representing the source point for forward-stack recording.</param>
    /// <returns>An <see cref="EditorLocation"/> representing the backward destination, or <see langword="null"/> when no history exists.</returns>
    public EditorLocation? NavigateBack(EditorLocation currentLocation)
    {
        if (_backStack.Count == 0)
        {
            return null;
        }

        _forwardStack.Push(currentLocation.Normalize());
        EditorLocation destination = _backStack.Pop();
        _lastRecordedLocation = destination;
        return destination;
    }

    /// <summary>
    /// Navigates forward in history and returns an <see cref="EditorLocation"/> representing the destination location.
    /// </summary>
    /// <param name="currentLocation">The current location representing the source point for backward-stack recording.</param>
    /// <returns>An <see cref="EditorLocation"/> representing the forward destination, or <see langword="null"/> when no history exists.</returns>
    public EditorLocation? NavigateForward(EditorLocation currentLocation)
    {
        if (_forwardStack.Count == 0)
        {
            return null;
        }

        _backStack.Push(currentLocation.Normalize());
        EditorLocation destination = _forwardStack.Pop();
        _lastRecordedLocation = destination;
        return destination;
    }

    /// <summary>
    /// Marks the supplied tab active and updates all tab active flags and returns <see langword="void"/>.
    /// </summary>
    /// <param name="activeTab">The tab representing the desired active editor selection.</param>
    private void SetActiveTab(EditorTabState? activeTab)
    {
        foreach (EditorTabState tab in OpenTabs)
        {
            tab.IsActive = ReferenceEquals(tab, activeTab);
        }

        ActiveTab = activeTab;
    }
}
