// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace PdfExtractor.Core;

public enum OutputFormat { Jpeg, Png, Webp, Bmp }

public sealed record ExtractionOptions(
    int MinimumWidth = 0,
    int MinimumHeight = 0,
    OutputFormat Format = OutputFormat.Jpeg,
    string NameTemplate = "image_",
    string? OutputDirectory = null)
{
    public string Extension => Format.ToString().ToLowerInvariant();

    public string GetOutputDirectory(string pdfPath) => string.IsNullOrWhiteSpace(OutputDirectory)
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pdfPath))!, Path.GetFileNameWithoutExtension(pdfPath))
        : Path.GetFullPath(OutputDirectory.Trim());

    public void Validate()
    {
        if (MinimumWidth < 0 || MinimumHeight < 0)
            throw new ArgumentException("Минимальные размеры не могут быть отрицательными.");
        if (!Enum.IsDefined(Format)) throw new ArgumentException("Неизвестный формат изображения.");
        _ = GetFileName("document.pdf", 1, 1);
        if (!string.IsNullOrWhiteSpace(OutputDirectory)) _ = Path.GetFullPath(OutputDirectory.Trim());
    }

    public string GetFileName(string pdfPath, int page, int index)
    {
        if (string.IsNullOrWhiteSpace(NameTemplate))
            throw new ArgumentException("Введите шаблон имени, например image_.");
        var hasIndex = false;
        var name = Regex.Replace(NameTemplate, @"\{([^{}]+)\}", match =>
        {
            var token = match.Groups[1].Value;
            if (token == "pdf") return Path.GetFileNameWithoutExtension(pdfPath);
            if (token == "page") return page.ToString(CultureInfo.InvariantCulture);
            if (token == "index") { hasIndex = true; return index.ToString("D4", CultureInfo.InvariantCulture); }
            if (Regex.IsMatch(token, @"^index:0{1,9}$"))
            {
                hasIndex = true;
                return index.ToString(token[6..], CultureInfo.InvariantCulture);
            }
            throw new ArgumentException($"Неизвестное поле шаблона: {{{token}}}. Доступны {{pdf}}, {{page}}, {{index:0000}}.");
        });
        if (!hasIndex) name += index.ToString("D4", CultureInfo.InvariantCulture);
        // Use the same portable filename rules on both operating systems.
        if (name.IndexOfAny("<>:\"/\\|?*{}".ToCharArray()) >= 0 || name.Any(char.IsControl)
            || name.EndsWith('.') || name.EndsWith(' ') || name.Length > 180
            || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase))
            throw new ArgumentException("Шаблон даёт недопустимое имя файла. Не используйте пути и символы <>:\"/\\|?*{}.");
        return name + "." + Extension;
    }
}
