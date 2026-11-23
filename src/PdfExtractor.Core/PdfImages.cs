// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;

namespace PdfExtractor.Core;

public sealed record PdfImageInfo(int Page, int Number, string Type, int Width, int Height);

public sealed class PdfImages
{
    public string ExecutablePath { get; }

    public PdfImages(string? executablePath = null)
    {
        ExecutablePath = executablePath ?? Locate();
    }

    public static string Locate()
    {
        var configured = Environment.GetEnvironmentVariable("PDFIMAGES_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured)) throw new FileNotFoundException("PDFIMAGES_PATH указывает на несуществующий файл.", configured);
            return Path.GetFullPath(configured);
        }
        var name = OperatingSystem.IsWindows() ? "pdfimages.exe" : "pdfimages";
        foreach (var candidate in new[] {
            Path.Combine(AppContext.BaseDirectory, "poppler", "Library", "bin", name),
            Path.Combine(AppContext.BaseDirectory, "poppler", "bin", name),
            Path.Combine(AppContext.BaseDirectory, name) })
            if (File.Exists(candidate)) return candidate;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException(OperatingSystem.IsWindows()
            ? "Не найден pdfimages.exe. Переустановите приложение: Poppler должен входить в комплект."
            : "Не найден pdfimages. Установите poppler-utils (Debian/Ubuntu) или poppler (Arch/Fedora)." );
    }

    public static IReadOnlyList<PdfImageInfo> ParseList(string output)
    {
        var images = new List<PdfImageInfo>();
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)) continue;
            if (!int.TryParse(fields[1], out var number) || !int.TryParse(fields[3], out var width)
                || !int.TryParse(fields[4], out var height) || width <= 0 || height <= 0)
                throw new InvalidDataException("Не удалось прочитать список изображений pdfimages.");
            if (fields[2] is not ("image" or "mask" or "smask" or "stencil"))
                throw new InvalidDataException($"Неизвестный тип изображения pdfimages: {fields[2]}.");
            images.Add(new(page, number, fields[2], width, height));
        }
        return images;
    }

    public async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["LC_ALL"] = "C";
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить pdfimages.");
        // Drain both pipes concurrently; large PDFs must not deadlock on a full stderr pipe.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        await process.WaitForExitAsync(CancellationToken.None);
        var output = await stdout;
        var error = await stderr;
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var detail = error.Trim();
            if (detail.Length > 1500) detail = detail[..1500];
            throw new IOException($"pdfimages завершился с кодом {process.ExitCode}. {detail}");
        }
        return output;
    }
}
