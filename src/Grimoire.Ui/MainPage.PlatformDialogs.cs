using System.Diagnostics;
using System.Text;
#if WINDOWS
using CommunityToolkit.Maui.Storage;
#endif

namespace Grimoire.Ui;

/// <summary>
/// Represents platform-specific file and directory dialog helpers used by the main UI page.
/// </summary>
public partial class MainPage
{
    /// <summary>
    /// Opens a platform directory picker and returns a <see cref="Task{TResult}"/> representing the selected project directory path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected directory, or <see langword="null"/> when cancelled or unavailable.</returns>
    private async Task<string?> PickProjectDirectoryAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        try
        {
            FolderPickerResult result = await FolderPicker.Default.PickAsync(cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccessful)
            {
                AppendLog(result.Exception?.Message ?? "Folder selection cancelled.");
                return null;
            }

            return result.Folder.Path;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or InvalidOperationException)
        {
            AppendLog($"Folder picker unavailable: {ex.Message}");
            return null;
        }
#elif MACCATALYST
        return await PickMacDirectoryAsync(cancellationToken).ConfigureAwait(true);
#else
        return await PickLinuxDirectoryAsync(cancellationToken).ConfigureAwait(true);
#endif
    }

    /// <summary>
    /// Opens a platform save-file picker and returns a <see cref="Task{TResult}"/> representing the selected output file path.
    /// </summary>
    /// <param name="defaultFileName">The default file name representing the initial save suggestion.</param>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected file, or <see langword="null"/> when cancelled or unavailable.</returns>
    private async Task<string?> PickSaveFilePathAsync(string defaultFileName, CancellationToken cancellationToken)
    {
        string fileName = string.IsNullOrWhiteSpace(defaultFileName) ? "output" : defaultFileName;
#if WINDOWS
        try
        {
            using MemoryStream stream = new();
            FileSaverResult result = await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccessful)
            {
                AppendLog(result.Exception?.Message ?? "Save file selection cancelled.");
                return null;
            }

            return result.FilePath;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or InvalidOperationException or IOException)
        {
            AppendLog($"Save file picker unavailable: {ex.Message}");
            return null;
        }
#elif MACCATALYST
        return await PickMacSaveFilePathAsync(fileName, cancellationToken).ConfigureAwait(true);
#else
        return await PickLinuxSaveFilePathAsync(fileName, cancellationToken).ConfigureAwait(true);
#endif
    }

#if MACCATALYST
    /// <summary>
    /// Opens a Mac directory picker and returns a <see cref="Task{TResult}"/> representing the selected directory path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected directory, or <see langword="null"/> when cancelled.</returns>
    private async Task<string?> PickMacDirectoryAsync(CancellationToken cancellationToken)
    {
        string script = "POSIX path of (choose folder with prompt \"Select project directory\")";
        string? path = await RunOsascriptAsync(script, cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("Directory selection cancelled.");
            return null;
        }

        return path.TrimEnd('/');
    }

    /// <summary>
    /// Opens a Mac save-file picker and returns a <see cref="Task{TResult}"/> representing the selected file path.
    /// </summary>
    /// <param name="defaultFileName">The default file name representing the initial save suggestion.</param>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected file, or <see langword="null"/> when cancelled.</returns>
    private async Task<string?> PickMacSaveFilePathAsync(string defaultFileName, CancellationToken cancellationToken)
    {
        StringBuilder escapedNameBuilder = new(defaultFileName.Length + 8);
        foreach (char character in defaultFileName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (character is '\\' or '"')
            {
                escapedNameBuilder.Append('\\');
            }

            escapedNameBuilder.Append(character);
        }

        string escapedName = escapedNameBuilder.ToString();
        string script = $"POSIX path of (choose file name with prompt \"Save output\" default name \"{escapedName}\")";
        string? path = await RunOsascriptAsync(script, cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("Save selection cancelled.");
            return null;
        }

        return path;
    }

    /// <summary>
    /// Executes an AppleScript dialog command and returns a <see cref="Task{TResult}"/> representing the script output text.
    /// </summary>
    /// <param name="script">The AppleScript command representing the dialog to execute.</param>
    /// <param name="cancellationToken">The cancellation token indicating when process execution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> output path from osascript, or <see langword="null"/> when unavailable.</returns>
    private static Task<string?> RunOsascriptAsync(string script, CancellationToken cancellationToken)
    {
        return RunDialogProcessAsync("osascript", ["-e", script], cancellationToken);
    }
#endif

#if !(WINDOWS || MACCATALYST)
    /// <summary>
    /// Opens a Linux directory picker and returns a <see cref="Task{TResult}"/> representing the selected directory path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected directory, or <see langword="null"/> when unavailable.</returns>
    private async Task<string?> PickLinuxDirectoryAsync(CancellationToken cancellationToken)
    {
        string? path = await RunDialogProcessAsync("zenity", ["--file-selection", "--directory", "--title=Select directory"], cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        path = await RunDialogProcessAsync("kdialog", ["--getexistingdirectory", ".", "--title", "Select directory"], cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        AppendLog("No Linux desktop folder dialog helper was found. Install zenity or kdialog.");
        return null;
    }

    /// <summary>
    /// Opens a Linux save-file picker and returns a <see cref="Task{TResult}"/> representing the selected file path.
    /// </summary>
    /// <param name="defaultFileName">The default file name representing the initial save suggestion.</param>
    /// <param name="cancellationToken">The cancellation token indicating when dialog operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> path for the selected file, or <see langword="null"/> when unavailable.</returns>
    private async Task<string?> PickLinuxSaveFilePathAsync(string defaultFileName, CancellationToken cancellationToken)
    {
        string? path = await RunDialogProcessAsync("zenity", ["--file-selection", "--save", "--confirm-overwrite", $"--filename={defaultFileName}", "--title=Save output"], cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        path = await RunDialogProcessAsync("kdialog", ["--getsavefilename", defaultFileName, "--title", "Save output"], cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        AppendLog("No Linux desktop save dialog helper was found. Install zenity or kdialog.");
        return null;
    }
#endif

#if MACCATALYST || !(WINDOWS || MACCATALYST)
    /// <summary>
    /// Executes a native dialog helper process and returns a <see cref="Task{TResult}"/> representing trimmed standard-output text.
    /// </summary>
    /// <param name="fileName">The executable name representing the dialog helper command.</param>
    /// <param name="arguments">The argument list representing process command-line options.</param>
    /// <param name="cancellationToken">The cancellation token indicating when process execution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> output path, or <see langword="null"/> when execution fails.</returns>
    private static async Task<string?> RunDialogProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo startInfo = new(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new()
            {
                StartInfo = startInfo,
            };
            if (!process.Start())
            {
                return null;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(true);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
#endif
}
