// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PdfExtractor.App;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PdfExtractor.Tests.TestAppBuilder))]

namespace PdfExtractor.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PdfExtractor.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class GuiTests
{
    [AvaloniaFact]
    public void WindowLoadsWithRequestedDefaultsAndAcceptsFileArguments()
    {
        using var fixture = new PdfFixture();
        var window = new MainWindow();
        window.Show();
        Assert.Equal("image_", window.FindControl<TextBox>("TemplateInput")!.Text);
        Assert.Equal(0, window.FindControl<ComboBox>("FormatInput")!.SelectedIndex);
        Assert.True(string.IsNullOrEmpty(window.FindControl<TextBox>("OutputInput")!.Text));
        window.AddFiles([fixture.PdfPath, fixture.PdfPath, "not-a-pdf.txt"]);
        Assert.Single(window.FindControl<ListBox>("FileList")!.Items);
        Assert.Contains("Тест с пробелами", window.FindControl<TextBlock>("OutputPreview")!.Text);
        var screenshot = Environment.GetEnvironmentVariable("PDF_EXTRACTOR_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot))
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(screenshot);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task DroppingPdfOnAnInputFieldAddsItToTheQueue()
    {
        using var fixture = new PdfFixture();
        var window = new MainWindow();
        window.Show();
        var file = await window.StorageProvider.TryGetFileFromPathAsync(fixture.PdfPath);
        Assert.NotNull(file);
        using var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        var input = window.FindControl<TextBox>("TemplateInput")!;
        var point = input.TranslatePoint(new Point(10, 10), window)!.Value;
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
        Assert.Single(window.FindControl<ListBox>("FileList")!.Items);
        Assert.Equal("image_", input.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ExtractButtonRunsActualExtractionWithoutBlockingTheWindow()
    {
        using var fixture = new PdfFixture();
        var window = new MainWindow();
        window.Show();
        window.AddFiles([fixture.PdfPath]);
        var start = window.FindControl<Button>("StartButton")!;
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(start.IsEnabled);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!start.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(start.IsEnabled);
        Assert.Contains("Изображений: 2", window.FindControl<TextBlock>("Status")!.Text);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "Тест с пробелами", "image_0001.jpeg")));
        window.Close();
    }
}
