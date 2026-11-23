// SPDX-License-Identifier: GPL-3.0-or-later
using SkiaSharp;

namespace PdfExtractor.Core;

public sealed record ExtractionProgress(string Message, int Completed = 0, int Total = 0);
public sealed record ExtractionResult(string PdfPath, string Directory, int Saved, int Skipped);

public sealed class ExtractionService(PdfImages pdfImages)
{
    public async Task<ExtractionResult> ExtractAsync(string pdfPath, ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        pdfPath = Path.GetFullPath(pdfPath);
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("PDF-файл не найден.", pdfPath);
        if (!Path.GetExtension(pdfPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите файл с расширением .pdf.");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("Чтение списка изображений…"));
        var all = PdfImages.ParseList(await pdfImages.RunAsync(["-list", pdfPath], cancellationToken));
        var positions = all.Select((image, position) => (image.Number, position)).ToDictionary(x => x.Number, x => x.position);
        var images = all.Where(x => x.Type == "image").ToArray();
        var eligible = images.Where(x => x.Width >= options.MinimumWidth && x.Height >= options.MinimumHeight).ToArray();
        var directory = options.GetOutputDirectory(pdfPath);
        // Validate PDF-dependent template fields before creating any output files.
        foreach (var image in eligible) _ = options.GetFileName(pdfPath, image.Page, 1);
        if (eligible.Length == 0) return new(pdfPath, directory, 0, images.Length);

        var temporary = Path.Combine(Path.GetTempPath(), "pdf-extractor-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temporary);
        try
        {
            progress?.Report(new("Извлечение исходных изображений…", 0, eligible.Length));
            // PNG is a universal intermediate; -j preserves embedded JPEG streams.
            await pdfImages.RunAsync(["-png", "-j", pdfPath, Path.Combine(temporary, "img")], cancellationToken);
            var extracted = System.IO.Directory.EnumerateFiles(temporary)
                .ToDictionary(path => int.Parse(Path.GetFileNameWithoutExtension(path)[4..]), path => path);
            System.IO.Directory.CreateDirectory(directory);
            var saved = 0;
            foreach (var image in eligible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!extracted.TryGetValue(image.Number, out var source))
                    throw new IOException($"pdfimages не создал изображение №{image.Number}.");
                using var decoded = SKBitmap.Decode(source) ?? throw new IOException($"Не удалось прочитать изображение №{image.Number}.");
                using var bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(decoded, 0, 0); }
                var position = positions[image.Number];
                // pdfimages lists an image's transparency mask immediately after it.
                var mask = position + 1 < all.Count ? all[position + 1] : null;
                if (mask is { Type: "smask" or "mask" } && mask.Page == image.Page
                    && extracted.TryGetValue(mask.Number, out var maskPath))
                    ApplyMask(bitmap, maskPath, cancellationToken);

                using var encoded = Encode(bitmap, options.Format);
                var name = options.GetFileName(pdfPath, image.Page, ++saved);
                var target = WriteUnique(directory, name, encoded, cancellationToken);
                progress?.Report(new($"Сохранено: {Path.GetFileName(target)} ({image.Width} × {image.Height})", saved, eligible.Length));
            }
            return new(pdfPath, directory, saved, images.Length - eligible.Length);
        }
        finally
        {
            try { System.IO.Directory.Delete(temporary, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ApplyMask(SKBitmap bitmap, string maskPath, CancellationToken token)
    {
        using var originalMask = SKBitmap.Decode(maskPath) ?? throw new IOException("Не удалось прочитать маску прозрачности.");
        using var scaledMask = originalMask.Width != bitmap.Width || originalMask.Height != bitmap.Height
            ? originalMask.Resize(new SKImageInfo(bitmap.Width, bitmap.Height), SKFilterQuality.Medium) : null;
        var mask = scaledMask ?? originalMask;
        for (var y = 0; y < bitmap.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                bitmap.SetPixel(x, y, color.WithAlpha(mask.GetPixel(x, y).Red));
            }
        }
    }

    private static SKData Encode(SKBitmap bitmap, OutputFormat format)
    {
        if (format is OutputFormat.Jpeg or OutputFormat.Bmp)
        {
            using var opaque = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(opaque)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(bitmap, 0, 0); }
            if (format == OutputFormat.Bmp) return EncodeBmp(opaque);
            return opaque.Encode(SKEncodedImageFormat.Jpeg, 95) ?? throw new IOException("Ошибка кодирования JPEG.");
        }
        return bitmap.Encode(format == OutputFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp,
            format == OutputFormat.Png ? 100 : 95) ?? throw new IOException("Ошибка кодирования изображения.");
    }

    private static SKData EncodeBmp(SKBitmap bitmap)
    {
        // Skia does not encode BMP; write a standard 24-bit, bottom-up BITMAPINFOHEADER.
        var stride = checked((bitmap.Width * 3 + 3) & ~3);
        var dataSize = checked(stride * bitmap.Height);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42); writer.Write(checked(54 + dataSize));
        writer.Write(0); writer.Write(54); writer.Write(40);
        writer.Write(bitmap.Width); writer.Write(bitmap.Height);
        writer.Write((ushort)1); writer.Write((ushort)24); writer.Write(0); writer.Write(dataSize);
        writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        var row = new byte[stride];
        for (var y = bitmap.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                row[x * 3] = color.Blue; row[x * 3 + 1] = color.Green; row[x * 3 + 2] = color.Red;
            }
            writer.Write(row);
        }
        return SKData.CreateCopy(stream.ToArray());
    }

    private static string WriteUnique(string directory, string name, SKData data, CancellationToken token)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var suffix = 0; ; suffix++)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(directory, suffix == 0 ? name : $"{stem}_{suffix}{extension}");
            FileStream output;
            try { output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(target)) { continue; }
            try
            {
                using (output) { data.SaveTo(output); output.Flush(); }
                return target;
            }
            catch { output.Dispose(); File.Delete(target); throw; }
        }
    }
}
