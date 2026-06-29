using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
#if WINDOWS
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
#endif
using Grimoire.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Grimoire.Ui;


/// <summary>
/// Implements pane layout, docking, splitter resizing, and status display helpers for <see cref="MainPage"/>.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Attempts to map preview log text to a normalized progress fraction.
    /// </summary>
    /// <param name="message">The raw preview status message.</param>
    /// <returns>A progress value from <c>0</c> to <c>1</c>, or <see langword="null"/> when unknown.</returns>
    private static double? TryMapPreviewProgress(string message)
    {
        string normalized = message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        Match fraction = ProgressFractionRegex.Match(normalized);
        double phaseProgress = 0;
        if (fraction.Success &&
            int.TryParse(fraction.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            int.TryParse(fraction.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) &&
            total > 0)
        {
            phaseProgress = Math.Clamp((double)index / total, 0, 1);
        }

        if (normalized.StartsWith("Starting preview render ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.04;
        }

        if (normalized.StartsWith("Preview cache miss ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.08;
        }

        if (normalized.StartsWith("Preview cache hit ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.18;
        }

        if (normalized.StartsWith("Preview reference scan ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.10 + (phaseProgress * 0.12);
        }

        if (normalized.StartsWith("Preview reference target scan ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.22 + (phaseProgress * 0.12);
        }

        if (normalized.StartsWith("Preview found ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Preview indexing ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.34;
        }

        if (normalized.StartsWith("Preview index ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.34 + (phaseProgress * 0.16);
        }

        if (normalized.StartsWith("Preview cache built ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.52;
        }

        if (normalized.StartsWith("Rendering preview body ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.58;
        }

        if (normalized.StartsWith("Processing inline tokens ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.62;
        }

        if (normalized.StartsWith("Including material ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.66;
        }

        if (normalized.StartsWith("Preview auto-linking mention ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Preview autolink ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.68 + (phaseProgress * 0.20);
        }

        if (normalized.StartsWith("Preview auto-linking complete", StringComparison.OrdinalIgnoreCase))
        {
            return 0.90;
        }

        if (normalized.StartsWith("Rewriting preview links ", StringComparison.OrdinalIgnoreCase))
        {
            return 0.95;
        }

        if (normalized.StartsWith("Preview file cache hit ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Completed preview render ", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return null;
    }

    /// <summary>
    /// Converts verbose status text into compact display text.
    /// </summary>
    /// <param name="message">The raw status message.</param>
    /// <returns>The shortened status text.</returns>
    private static string ShortenStatusText(string message)
    {
        return FormatProgressMessage(message);
    }

    /// <summary>
    /// Shows the requested bottom panel and updates layout state.
    /// </summary>
    /// <param name="panel">The panel title to show.</param>
    private void ShowBottomPanel(string panel)
    {
        BottomDock.IsVisible = true;
        BottomRail.IsVisible = false;
        LorePanel.IsVisible = string.Equals(panel, "Lore search", StringComparison.OrdinalIgnoreCase);
        ReferencePanel.IsVisible = string.Equals(panel, "Reference scan", StringComparison.OrdinalIgnoreCase);
        LogEditor.IsVisible = string.Equals(panel, "Log", StringComparison.OrdinalIgnoreCase);
        BottomPaneTitleLabel.Text = panel;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Shows the lore-search bottom tab.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnBottomLoreTabClicked(object? sender, EventArgs e)
    {
        ShowBottomPanel("Lore search");
    }

    /// <summary>
    /// Shows the reference-scan bottom tab.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnBottomReferenceTabClicked(object? sender, EventArgs e)
    {
        ShowBottomPanel("Reference scan");
    }

    /// <summary>
    /// Shows the log bottom tab.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnBottomLogTabClicked(object? sender, EventArgs e)
    {
        ShowBottomPanel("Log");
    }

    /// <summary>
    /// Shows the log panel from toolbar actions.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnShowLogClicked(object? sender, EventArgs e)
    {
        ShowBottomPanel("Log");
    }

    /// <summary>
    /// Expands and shows the project pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnShowProjectPaneClicked(object? sender, EventArgs e)
    {
        ProjectDock.IsVisible = true;
        ProjectRail.IsVisible = false;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Expands and shows the tools pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnShowToolsPaneClicked(object? sender, EventArgs e)
    {
        ToolsDock.IsVisible = true;
        ToolsRail.IsVisible = false;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Expands and shows the bottom pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnShowBottomPaneClicked(object? sender, EventArgs e)
    {
        BottomDock.IsVisible = true;
        BottomRail.IsVisible = false;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Collapses the project pane into its rail.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnHideProjectPaneClicked(object? sender, EventArgs e)
    {
        ProjectDock.IsVisible = false;
        ProjectRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Collapses the tools pane into its rail.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnHideToolsPaneClicked(object? sender, EventArgs e)
    {
        ToolsDock.IsVisible = false;
        ToolsRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Collapses the bottom pane into its rail.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnHideBottomPaneClicked(object? sender, EventArgs e)
    {
        BottomDock.IsVisible = false;
        BottomRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the project pane to the left side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockProjectLeftClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ProjectDock, 0);
        Grid.SetRow(ProjectDock, 0);
        Grid.SetRowSpan(ProjectDock, 3);
        ProjectDock.IsVisible = true;
        ProjectRail.IsVisible = false;
        if (ToolsDock.IsVisible && Grid.GetColumn(ToolsDock) == 0)
        {
            ToolsDock.IsVisible = false;
            ToolsRail.IsVisible = true;
        }

        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the project pane to the right side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockProjectRightClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ProjectDock, 4);
        Grid.SetRow(ProjectDock, 0);
        Grid.SetRowSpan(ProjectDock, 3);
        ProjectDock.IsVisible = true;
        ProjectRail.IsVisible = false;
        ToolsDock.IsVisible = false;
        ToolsRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the project pane to the bottom region.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockProjectBottomClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ProjectDock, 2);
        Grid.SetRow(ProjectDock, 2);
        Grid.SetRowSpan(ProjectDock, 1);
        ProjectDock.IsVisible = true;
        ProjectRail.IsVisible = false;
        BottomDock.IsVisible = false;
        BottomRail.IsVisible = true;
        if (ToolsDock.IsVisible && Grid.GetRow(ToolsDock) == 2)
        {
            ToolsDock.IsVisible = false;
            ToolsRail.IsVisible = true;
        }

        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the tools pane to the right side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockToolsRightClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ToolsDock, 4);
        Grid.SetRow(ToolsDock, 0);
        Grid.SetRowSpan(ToolsDock, 3);
        ToolsDock.IsVisible = true;
        ToolsRail.IsVisible = false;
        if (ProjectDock.IsVisible && Grid.GetColumn(ProjectDock) == 4)
        {
            ProjectDock.IsVisible = false;
            ProjectRail.IsVisible = true;
        }

        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the tools pane to the left side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockToolsLeftClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ToolsDock, 0);
        Grid.SetRow(ToolsDock, 0);
        Grid.SetRowSpan(ToolsDock, 3);
        ToolsDock.IsVisible = true;
        ToolsRail.IsVisible = false;
        ProjectDock.IsVisible = false;
        ProjectRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the tools pane to the bottom region.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockToolsBottomClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(ToolsDock, 2);
        Grid.SetRow(ToolsDock, 2);
        Grid.SetRowSpan(ToolsDock, 1);
        ToolsDock.IsVisible = true;
        ToolsRail.IsVisible = false;
        BottomDock.IsVisible = false;
        BottomRail.IsVisible = true;
        if (ProjectDock.IsVisible && Grid.GetRow(ProjectDock) == 2)
        {
            ProjectDock.IsVisible = false;
            ProjectRail.IsVisible = true;
        }

        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the bottom pane to the bottom region.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockBottomBottomClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(BottomDock, 2);
        Grid.SetRow(BottomDock, 2);
        Grid.SetRowSpan(BottomDock, 1);
        BottomDock.IsVisible = true;
        BottomRail.IsVisible = false;
        if (ProjectDock.IsVisible && Grid.GetRow(ProjectDock) == 2)
        {
            ProjectDock.IsVisible = false;
            ProjectRail.IsVisible = true;
        }

        if (ToolsDock.IsVisible && Grid.GetRow(ToolsDock) == 2)
        {
            ToolsDock.IsVisible = false;
            ToolsRail.IsVisible = true;
        }

        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the bottom pane to the left side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockBottomLeftClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(BottomDock, 0);
        Grid.SetRow(BottomDock, 0);
        Grid.SetRowSpan(BottomDock, 3);
        BottomDock.IsVisible = true;
        BottomRail.IsVisible = false;
        ProjectDock.IsVisible = false;
        ProjectRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Docks the bottom pane to the right side.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDockBottomRightClicked(object? sender, EventArgs e)
    {
        Grid.SetColumn(BottomDock, 4);
        Grid.SetRow(BottomDock, 0);
        Grid.SetRowSpan(BottomDock, 3);
        BottomDock.IsVisible = true;
        BottomRail.IsVisible = false;
        ToolsDock.IsVisible = false;
        ToolsRail.IsVisible = true;
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Handles header drag gestures for the project pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnProjectDockHeaderPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        DockPaneFromHeaderDrag(e, OnDockProjectLeftClicked, OnDockProjectRightClicked, OnDockProjectBottomClicked);
    }

    /// <summary>
    /// Handles header drag gestures for the tools pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnToolsDockHeaderPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        DockPaneFromHeaderDrag(e, OnDockToolsLeftClicked, OnDockToolsRightClicked, OnDockToolsBottomClicked);
    }

    /// <summary>
    /// Handles header drag gestures for the bottom pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnBottomDockHeaderPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        DockPaneFromHeaderDrag(e, OnDockBottomLeftClicked, OnDockBottomRightClicked, OnDockBottomBottomClicked);
    }

    /// <summary>
    /// Starts drag-and-drop docking for the project pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drag-starting arguments.</param>
    private void OnProjectDockDragStarting(object? sender, DragStartingEventArgs e)
    {
        StartDockDrag(e, DockPaneKind.Project, "Project");
    }

    /// <summary>
    /// Starts drag-and-drop docking for the tools pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drag-starting arguments.</param>
    private void OnToolsDockDragStarting(object? sender, DragStartingEventArgs e)
    {
        StartDockDrag(e, DockPaneKind.Tools, "Tools");
    }

    /// <summary>
    /// Starts drag-and-drop docking for the bottom pane.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drag-starting arguments.</param>
    private void OnBottomDockDragStarting(object? sender, DragStartingEventArgs e)
    {
        StartDockDrag(e, DockPaneKind.Bottom, BottomPaneTitleLabel.Text ?? "Bottom pane");
    }

    /// <summary>
    /// Populates drag data for dock pane drag operations.
    /// </summary>
    /// <param name="e">The drag-starting arguments.</param>
    /// <param name="paneKind">The pane being dragged.</param>
    /// <param name="title">The drag label text.</param>
    private void StartDockDrag(DragStartingEventArgs e, DockPaneKind paneKind, string title)
    {
        _draggedDockPane = paneKind;
        e.Data.Text = title;
        e.Data.Properties[DockPaneDragDataKey] = paneKind.ToString();
    }

    /// <summary>
    /// Handles drag-over events for pane docking targets.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drag event arguments.</param>
    private void OnDockPaneDragOver(object? sender, DragEventArgs e)
    {
        if (TryResolveDraggedDockPane(e.Data) is not null)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    /// <summary>
    /// Handles dropping a dragged pane onto the workbench.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drop event arguments.</param>
    private void OnDockPaneDropped(object? sender, DropEventArgs e)
    {
        DockPaneKind? paneKind = TryResolveDraggedDockPane(e.Data);
        if (paneKind is not { } resolvedPaneKind)
        {
            _draggedDockPane = null;
            return;
        }

        Point? point = e.GetPosition(WorkbenchGrid);
        _draggedDockPane = null;
        DockTarget target = ResolveDockTarget(point, WorkbenchGrid.Width, WorkbenchGrid.Height);
        DockPane(resolvedPaneKind, target);
    }

    /// <summary>
    /// Docks a pane to a target region.
    /// </summary>
    /// <param name="paneKind">The pane to dock.</param>
    /// <param name="target">The docking target.</param>
    private void DockPane(DockPaneKind paneKind, DockTarget target)
    {
        switch (paneKind, target)
        {
            case (DockPaneKind.Project, DockTarget.Left):
                OnDockProjectLeftClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Project, DockTarget.Right):
                OnDockProjectRightClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Project, DockTarget.Bottom):
                OnDockProjectBottomClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Tools, DockTarget.Left):
                OnDockToolsLeftClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Tools, DockTarget.Right):
                OnDockToolsRightClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Tools, DockTarget.Bottom):
                OnDockToolsBottomClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Bottom, DockTarget.Left):
                OnDockBottomLeftClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Bottom, DockTarget.Right):
                OnDockBottomRightClicked(this, EventArgs.Empty);
                break;
            case (DockPaneKind.Bottom, DockTarget.Bottom):
                OnDockBottomBottomClicked(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Resolves header-pan gestures into docking actions.
    /// </summary>
    /// <param name="e">The pan gesture arguments.</param>
    /// <param name="dockLeft">The action used for left docking.</param>
    /// <param name="dockRight">The action used for right docking.</param>
    /// <param name="dockBottom">The action used for bottom docking.</param>
    private void DockPaneFromHeaderDrag(
        PanUpdatedEventArgs e,
        Action<object?, EventArgs> dockLeft,
        Action<object?, EventArgs> dockRight,
        Action<object?, EventArgs> dockBottom)
    {
        if (e.StatusType != GestureStatus.Completed)
        {
            return;
        }

        if (Math.Abs(e.TotalX) >= Math.Abs(e.TotalY) && Math.Abs(e.TotalX) > 90)
        {
            if (e.TotalX < 0)
            {
                dockLeft(this, EventArgs.Empty);
            }
            else
            {
                dockRight(this, EventArgs.Empty);
            }

            return;
        }

        if (e.TotalY > 70)
        {
            dockBottom(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Resolves a docking target from drop coordinates.
    /// </summary>
    /// <param name="dropPoint">The drop point relative to the workbench.</param>
    /// <param name="surfaceWidth">The workbench width.</param>
    /// <param name="surfaceHeight">The workbench height.</param>
    /// <returns>The resolved docking target.</returns>
    private static DockTarget ResolveDockTarget(Point? dropPoint, double surfaceWidth, double surfaceHeight)
    {
        if (dropPoint is null || surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return DockTarget.Right;
        }

        if (dropPoint.Value.Y > surfaceHeight * 0.68)
        {
            return DockTarget.Bottom;
        }

        if (dropPoint.Value.X < surfaceWidth * 0.34)
        {
            return DockTarget.Left;
        }

        return DockTarget.Right;
    }

    /// <summary>
    /// Attempts to resolve the dragged dock pane from drag data.
    /// </summary>
    /// <param name="data">The drag data package.</param>
    /// <returns>The dragged pane kind, or <see langword="null"/> when unavailable.</returns>
    private static DockPaneKind? TryResolveDraggedDockPane(DataPackage data)
    {
        return TryResolveDraggedDockPaneFromProperties(data.Properties);
    }

    /// <summary>
    /// Attempts to resolve the dragged dock pane from drag data view.
    /// </summary>
    /// <param name="data">The drag data package view.</param>
    /// <returns>The dragged pane kind, or <see langword="null"/> when unavailable.</returns>
    private static DockPaneKind? TryResolveDraggedDockPane(DataPackageView data)
    {
        return TryResolveDraggedDockPaneFromProperties(data.Properties);
    }

    /// <summary>
    /// Attempts to resolve a dragged dock pane from mutable drag properties.
    /// </summary>
    /// <param name="properties">The mutable drag property set.</param>
    /// <returns>The dragged pane kind, or <see langword="null"/> when unavailable.</returns>
    private static DockPaneKind? TryResolveDraggedDockPaneFromProperties(DataPackagePropertySet properties)
    {
        if (!properties.TryGetValue(DockPaneDragDataKey, out object? value))
        {
            return null;
        }

        return value is string paneName &&
            Enum.TryParse(paneName, out DockPaneKind paneKind) &&
            Enum.IsDefined(paneKind)
            ? paneKind
            : null;
    }

    /// <summary>
    /// Attempts to resolve a dragged dock pane from immutable drag properties.
    /// </summary>
    /// <param name="properties">The immutable drag property set.</param>
    /// <returns>The dragged pane kind, or <see langword="null"/> when unavailable.</returns>
    private static DockPaneKind? TryResolveDraggedDockPaneFromProperties(DataPackagePropertySetView properties)
    {
        if (!properties.TryGetValue(DockPaneDragDataKey, out object? value))
        {
            return null;
        }

        return value is string paneName &&
            Enum.TryParse(paneName, out DockPaneKind paneKind) &&
            Enum.IsDefined(paneKind)
            ? paneKind
            : null;
    }

    /// <summary>
    /// Recomputes workbench column and row sizes based on pane visibility and docking.
    /// </summary>
    private void UpdateWorkbenchLayout()
    {
        bool projectLeft = ProjectDock.IsVisible && Grid.GetColumn(ProjectDock) == 0;
        bool projectBottom = ProjectDock.IsVisible && Grid.GetRow(ProjectDock) == 2;
        bool projectRight = ProjectDock.IsVisible && Grid.GetColumn(ProjectDock) == 4;
        bool toolsLeft = ToolsDock.IsVisible && Grid.GetColumn(ToolsDock) == 0;
        bool toolsBottom = ToolsDock.IsVisible && Grid.GetRow(ToolsDock) == 2;
        bool toolsRight = ToolsDock.IsVisible && Grid.GetColumn(ToolsDock) == 4;
        bool bottomLeft = BottomDock.IsVisible && Grid.GetColumn(BottomDock) == 0;
        bool bottomRight = BottomDock.IsVisible && Grid.GetColumn(BottomDock) == 4;
        bool bottomBottom = BottomDock.IsVisible && Grid.GetRow(BottomDock) == 2;
        bool bottomVisible = bottomBottom || projectBottom || toolsBottom;

        WorkbenchGrid.ColumnDefinitions[0].Width = new GridLength(projectLeft ? _projectPaneWidth : toolsLeft ? _toolsPaneWidth : bottomLeft ? _projectPaneWidth : ProjectRail.IsVisible ? 36 : 0);
        WorkbenchGrid.ColumnDefinitions[1].Width = new GridLength(projectLeft || toolsLeft ? 6 : 0);
        WorkbenchGrid.ColumnDefinitions[3].Width = new GridLength(toolsRight ? 6 : 0);
        WorkbenchGrid.ColumnDefinitions[4].Width = new GridLength(toolsRight ? _toolsPaneWidth : projectRight ? _projectPaneWidth : bottomRight ? _toolsPaneWidth : ToolsRail.IsVisible ? 36 : 0);
        WorkbenchGrid.RowDefinitions[1].Height = new GridLength(bottomVisible ? 6 : 0);
        WorkbenchGrid.RowDefinitions[2].Height = new GridLength(bottomVisible ? _bottomPaneHeight : BottomRail.IsVisible ? 32 : 0);

        ProjectSplitter.IsVisible = projectLeft;
        ToolsSplitter.IsVisible = toolsLeft || toolsRight;
        Grid.SetColumn(ToolsSplitter, toolsLeft ? 1 : 3);
        BottomSplitter.IsVisible = bottomVisible;
        Grid.SetRowSpan(EditorDock, bottomVisible ? 1 : 3);

        ProjectRail.IsVisible = !ProjectDock.IsVisible;
        ToolsRail.IsVisible = !ToolsDock.IsVisible;
        BottomRail.IsVisible = !BottomDock.IsVisible && !projectBottom && !toolsBottom;
    }

    /// <summary>
    /// Handles resizing the project pane via splitter drag.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnProjectSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        ResizeColumn(e, columnIndex: 0, minimum: 180, maximum: 520, ref _projectSplitterStartWidth, invertDelta: false);
    }

    /// <summary>
    /// Handles resizing the tools pane via splitter drag.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnToolsSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        int columnIndex = Grid.GetColumn(ToolsDock);
        ResizeColumn(
            e,
            columnIndex,
            minimum: 240,
            maximum: 620,
            ref _toolsSplitterStartWidth,
            invertDelta: columnIndex == 4,
            setPaneWidth: width => _toolsPaneWidth = width);
    }

    /// <summary>
    /// Handles resizing the bottom pane via splitter drag.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The pan gesture arguments.</param>
    private void OnBottomSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _bottomSplitterStartHeight = WorkbenchGrid.RowDefinitions[2].Height.Value;
            return;
        }

        if (e.StatusType != GestureStatus.Running)
        {
            return;
        }

        double resized = Math.Clamp(_bottomSplitterStartHeight - e.TotalY, 120, 420);
        _bottomPaneHeight = resized;
        WorkbenchGrid.RowDefinitions[2].Height = new GridLength(resized);
    }

    /// <summary>
    /// Applies horizontal splitter drag updates to a target grid column.
    /// </summary>
    /// <param name="e">The pan gesture arguments.</param>
    /// <param name="columnIndex">The column index to resize.</param>
    /// <param name="minimum">The minimum width.</param>
    /// <param name="maximum">The maximum width.</param>
    /// <param name="startWidth">The captured starting width.</param>
    /// <param name="invertDelta">A value indicating whether drag delta should be inverted.</param>
    /// <param name="setPaneWidth">An optional setter for persisting pane width.</param>
    private void ResizeColumn(
        PanUpdatedEventArgs e,
        int columnIndex,
        double minimum,
        double maximum,
        ref double startWidth,
        bool invertDelta,
        Action<double>? setPaneWidth = null)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            startWidth = WorkbenchGrid.ColumnDefinitions[columnIndex].Width.Value;
            return;
        }

        if (e.StatusType != GestureStatus.Running)
        {
            return;
        }

        double delta = invertDelta ? -e.TotalX : e.TotalX;
        double resized = Math.Clamp(startWidth + delta, minimum, maximum);
        if (setPaneWidth is not null)
        {
            setPaneWidth(resized);
        }
        else if (columnIndex == 0)
        {
            _projectPaneWidth = resized;
        }
        else if (columnIndex == 4)
        {
            _toolsPaneWidth = resized;
        }

        WorkbenchGrid.ColumnDefinitions[columnIndex].Width = new GridLength(resized);
    }

    /// <summary>
    /// Appends keyword-highlight summary information to the log.
    /// </summary>
    /// <param name="document">The document containing keyword highlights.</param>
    private void AppendKeywordHighlights(ProjectFileDocument document)
    {
        if (document.KeywordHighlights.Count == 0)
        {
            return;
        }

        string summary = string.Join(", ", document.KeywordHighlights.Take(8).Select(static highlight => $"{highlight.Keyword}@{highlight.LineNumber}"));
        AppendLog($"Entity keyword highlights: {summary}");
    }

    /// <summary>
    /// Updates the current file label and line indicator.
    /// </summary>
    private void UpdateCurrentFileLabel()
    {
        string label = string.IsNullOrWhiteSpace(_currentFilePath) ? "No file open" : _currentFilePath;
        CurrentFileLabel.Text = _hasUnsavedChanges ? $"{label} *" : label;
        UpdateCurrentLineLabel(GetCurrentEditorLineNumber());
    }

    /// <summary>
    /// Updates page title and heading labels for the current project.
    /// </summary>
    private void UpdateProjectTitle()
    {
        string projectName = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? "No project"
            : Path.GetFileName(_currentProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Title = $"Grimoire - {projectName}";
        string heading = $"Grimoire - {projectName}";
        MainHeadingLabel.Text = heading;
        MainHeadingOutlineTopLabel.Text = heading;
        MainHeadingOutlineTopLeftLabel.Text = heading;
        MainHeadingOutlineTopRightLabel.Text = heading;
        MainHeadingOutlineRightLabel.Text = heading;
        MainHeadingOutlineBottomLabel.Text = heading;
        MainHeadingOutlineBottomLeftLabel.Text = heading;
        MainHeadingOutlineBottomRightLabel.Text = heading;
        MainHeadingOutlineLeftLabel.Text = heading;
        ProjectStatusLabel.Text = _currentProjectPath ?? string.Empty;
    }

    /// <summary>
    /// Updates build button text based on the last selected target.
    /// </summary>
    private void UpdateBuildButtonText()
    {
        BuildButton.Text = _lastBuildTarget switch
        {
            BuildTarget.Html => "Build HTML",
            BuildTarget.FoundryVtt => "Build FoundryVTT",
            BuildTarget.Pdf => "Build PDF",
            _ => "Build all",
        };
    }

    /// <summary>
    /// Appends a timestamped message to the log panel.
    /// </summary>
    /// <param name="message">The message to append.</param>
    private void AppendLog(string message)
    {
        string timestamp = DateTimeOffset.Now.ToString(Text("Ui:Status:TimestampFormat"), System.Globalization.CultureInfo.CurrentCulture);
        string line = Text("Ui:Status:Line", timestamp, message);
        if (string.IsNullOrWhiteSpace(LogEditor.Text))
        {
            LogEditor.Text = line;
            return;
        }

        LogEditor.Text += Environment.NewLine + line;
    }

    /// <summary>
    /// Extracts a project path from a preview open URL.
    /// </summary>
    /// <param name="url">The URL to inspect.</param>
    /// <returns>The extracted project path, or <see langword="null"/>.</returns>
    private static string? ExtractPreviewPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string? path = ExtractQueryValue(uri.Query, "path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return ExtractQueryValue(url, "path");
    }

    /// <summary>
    /// Determines whether a preview URL is a trusted Grimoire open command.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns><see langword="true"/> when the URL uses the trusted open command format.</returns>
    private static bool IsTrustedPreviewOpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("grimoire", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath.Equals("/", StringComparison.Ordinal);
    }
}
