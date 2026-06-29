using UIKit;

#pragma warning disable IDE0130
namespace Grimoire.Ui;
#pragma warning restore IDE0130

/// <summary>
/// Represents the Mac Catalyst application entry point that launches the MAUI app delegate.
/// </summary>
public class Program
{
    /// <summary>
    /// Starts the Mac Catalyst process and returns <see langword="void"/> after transferring execution to UIKit.
    /// </summary>
    /// <param name="args">Command-line arguments representing startup parameters passed by the host process.</param>
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
