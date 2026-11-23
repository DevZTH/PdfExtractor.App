// SPDX-License-Identifier: GPL-3.0-or-later
using PdfExtractor.Core;
using SkiaSharp;
using Xunit;

namespace PdfExtractor.Tests;

public sealed class ExtractionTests
{
    [Theory]
    [InlineData(OutputFormat.Jpeg)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Webp)]
    [InlineData(OutputFormat.Bmp)]
    public async Task ExtractsActualImagesInRequestedFormat(OutputFormat format)
    {
        using var fixture = new PdfFixture();
        var options = new ExtractionOptions(Format: format);
        var result = await new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, options);
        Assert.Equal(2, result.Saved);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(Path.Combine(fixture.Directory, "Тест с пробелами"), result.Directory);
        var file = Path.Combine(result.Directory, "image_0001." + options.Extension);
        using var image = SKBitmap.Decode(file);
        Assert.NotNull(image);
        Assert.Equal(64, image.Width);
        Assert.Equal(40, image.Height);
        Assert.InRange(image.GetPixel(40, 10).Red, (byte)230, (byte)250);
        using var codec = SKCodec.Create(file);
        Assert.Equal(format switch { OutputFormat.Jpeg => SKEncodedImageFormat.Jpeg, OutputFormat.Png => SKEncodedImageFormat.Png,
            OutputFormat.Webp => SKEncodedImageFormat.Webp, _ => SKEncodedImageFormat.Bmp }, codec.EncodedFormat);
    }

    [Fact]
    public async Task CutoffChecksBothDimensionsAndUsesCustomDestinationAndTemplate()
    {
        using var fixture = new PdfFixture();
        var output = Path.Combine(fixture.Directory, "выход", "nested");
        var options = new ExtractionOptions(16, 10, OutputFormat.Png, "{pdf}_p{page}_{index:000}", output);
        var result = await new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, options);
        Assert.Equal(1, result.Saved);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(Path.Combine(output, "Тест с пробелами_p1_001.png")));
        var excluded = await new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, options with { MinimumHeight = 41 });
        Assert.Equal(0, excluded.Saved);
        Assert.Equal(2, excluded.Skipped);
    }

    [Fact]
    public async Task ExistingFilesAreNeverOverwritten()
    {
        using var fixture = new PdfFixture();
        var service = new ExtractionService(new PdfImages());
        var first = await service.ExtractAsync(fixture.PdfPath, new());
        var path = Path.Combine(first.Directory, "image_0001.jpeg");
        await File.WriteAllTextAsync(path, "keep this file");
        await service.ExtractAsync(fixture.PdfPath, new());
        Assert.Equal("keep this file", await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(Path.Combine(first.Directory, "image_0001_1.jpeg")));
    }

    [Fact]
    public async Task SoftMaskBecomesAlphaAndIsNotSavedAsAnExtraImage()
    {
        using var fixture = new PdfFixture(withMask: true);
        var service = new ExtractionService(new PdfImages());
        var png = await service.ExtractAsync(fixture.PdfPath, new(Format: OutputFormat.Png));
        Assert.Equal(2, png.Saved);
        using var transparent = SKBitmap.Decode(Path.Combine(png.Directory, "image_0001.png"));
        Assert.Equal(0, transparent.GetPixel(0, 0).Alpha);
        Assert.Equal(255, transparent.GetPixel(50, 0).Alpha);
        var jpeg = await service.ExtractAsync(fixture.PdfPath, new());
        using var white = SKBitmap.Decode(Path.Combine(jpeg.Directory, "image_0001.jpeg"));
        Assert.InRange(white.GetPixel(0, 0).Red, (byte)250, (byte)255);
        Assert.InRange(white.GetPixel(0, 0).Green, (byte)250, (byte)255);
    }

    [Fact]
    public async Task EmptyPdfDoesNotCreateOutputDirectory()
    {
        using var fixture = new PdfFixture(empty: true);
        var result = await new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, new());
        Assert.Equal(0, result.Saved);
        Assert.False(Directory.Exists(result.Directory));
    }

    [Fact]
    public async Task InvalidPdfReportsAnError()
    {
        using var fixture = new PdfFixture();
        await File.WriteAllTextAsync(fixture.PdfPath, "not a PDF");
        var error = await Assert.ThrowsAsync<IOException>(() => new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, new()));
        Assert.Contains("pdfimages", error.Message);
    }

    [Fact]
    public async Task CancelledOperationCreatesNoOutput()
    {
        using var fixture = new PdfFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, new(), cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(new ExtractionOptions().GetOutputDirectory(fixture.PdfPath)));
    }

    [Fact]
    public async Task CancellationDuringSavingKeepsCompletedFileAndStopsBeforeNextImage()
    {
        using var fixture = new PdfFixture();
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value.Completed == 1) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExtractionService(new PdfImages()).ExtractAsync(fixture.PdfPath, new(), progress, cancellation.Token));
        var directory = new ExtractionOptions().GetOutputDirectory(fixture.PdfPath);
        Assert.Single(Directory.GetFiles(directory));
        using var bitmap = SKBitmap.Decode(Path.Combine(directory, "image_0001.jpeg"));
        Assert.NotNull(bitmap);
    }

    private sealed class InlineProgress(Action<ExtractionProgress> report) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => report(value);
    }

    [Theory]
    [InlineData("../escape_")]
    [InlineData("{pdf.Title}")]
    [InlineData("{index:../../}")]
    [InlineData("")]
    [InlineData("{unclosed")]
    public void InvalidTemplatesAreRejected(string template) =>
        Assert.Throws<ArgumentException>(() => new ExtractionOptions(NameTemplate: template).Validate());

    [Fact]
    public void PdfImageListRetainsMaskOrderAndImageNumbers()
    {
        var list = PdfImages.ParseList("page num type width height\n----\n 1 0 image 64 40 rgb 3 8 image no 5 0\n1 1 smask 64 40 gray\n2 2 image 8 8 rgb");
        Assert.Equal(3, list.Count);
        Assert.Equal("smask", list[1].Type);
        Assert.Equal(2, list[2].Page);
    }
}
