// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;

namespace PdfExtractor.App;

internal static class Program
{
    public static string[] Files { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Files = args.Where(arg => arg != "--").ToArray();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
