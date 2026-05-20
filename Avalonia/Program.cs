using System;
using Avalonia;

namespace DngOpcodesEditor.Avalonia;

// Entry point for the cross-platform Avalonia front-end. The WPF project still
// works on Windows; this project lets the same opcode editor run on Linux and
// macOS while sharing the Core opcode + TIFF logic.
public class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
