// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace PdfExtractor.Tests;

internal sealed class PdfFixture : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "pdf-extractor-test-" + Guid.NewGuid().ToString("N"));
    public string PdfPath => Path.Combine(Directory, "Тест с пробелами.pdf");

    public PdfFixture(bool withMask = false, bool empty = false)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var objects = new List<byte[]>();
        void Text(string value) => objects.Add(Encoding.Latin1.GetBytes(value));
        void Stream(string dictionary, byte[] bytes)
        {
            using var output = new MemoryStream();
            output.Write(Encoding.ASCII.GetBytes($"<< {dictionary} /Length {bytes.Length} >>\nstream\n"));
            output.Write(bytes);
            output.Write(Encoding.ASCII.GetBytes("\nendstream"));
            objects.Add(output.ToArray());
        }
        Text("<< /Type /Catalog /Pages 2 0 R >>");
        Text("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Text(empty
            ? "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> /Contents 4 0 R >>"
            : "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /Im1 5 0 R /Im2 6 0 R >> >> /Contents 4 0 R >>");
        Stream("", Encoding.ASCII.GetBytes(empty ? "" : "q 64 0 0 40 0 0 cm /Im1 Do Q\nq 8 0 0 8 70 0 cm /Im2 Do Q"));
        if (!empty)
        {
            var large = new byte[64 * 40 * 3];
            for (var i = 0; i < large.Length; i += 3) { large[i] = 240; large[i + 1] = 40; large[i + 2] = 20; }
            Stream("/Type /XObject /Subtype /Image /Width 64 /Height 40 /ColorSpace /DeviceRGB /BitsPerComponent 8" +
                   (withMask ? " /SMask 7 0 R" : ""), large);
            var small = new byte[8 * 8 * 3];
            for (var i = 0; i < small.Length; i += 3) small[i + 2] = 255;
            Stream("/Type /XObject /Subtype /Image /Width 8 /Height 8 /ColorSpace /DeviceRGB /BitsPerComponent 8", small);
            if (withMask)
            {
                var alpha = new byte[64 * 40];
                for (var y = 0; y < 40; y++) for (var x = 32; x < 64; x++) alpha[y * 64 + x] = 255;
                Stream("/Type /XObject /Subtype /Image /Width 64 /Height 40 /ColorSpace /DeviceGray /BitsPerComponent 8", alpha);
            }
        }
        using var pdf = File.Create(PdfPath);
        void Write(string value) => pdf.Write(Encoding.Latin1.GetBytes(value));
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            Write($"{i + 1} 0 obj\n"); pdf.Write(objects[i]); Write("\nendobj\n");
        }
        var xref = pdf.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
