using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ClassSchedule.Core;

/// <summary>Writes a one-page PDF that holds a picture of the page, so any font or script prints as it looks on screen.</summary>
public static class PdfWriter
{
    private static readonly HashSet<string> LetterRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "US", "CA", "MX", "PH", "CL", "CO", "VE", "GT", "CR", "DO", "PR"
    };

    /// <summary>Whether people in this region (a two-letter code such as "US") print on Letter paper rather than A4.</summary>
    public static bool UsesLetter(string? region) => region is not null && LetterRegions.Contains(region);

    /// <summary>A US Letter or A4 page in PDF points, landscape.</summary>
    public static (double Width, double Height) LandscapePage(bool a4) => a4 ? (841.89, 595.28) : (792, 612);

    /// <param name="rgb">Width × height pixels, three bytes each (red, green, blue), top row first.</param>
    public static void WriteImagePage(Stream output, int width, int height, byte[] rgb, double pageWidth, double pageHeight, string title)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The picture has no size.");
        if (rgb.Length != width * height * 3) throw new ArgumentException("The pixel data doesn't match the picture size.", nameof(rgb));

        byte[] packed;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) zip.Write(rgb);
            packed = buffer.ToArray();
        }

        var offsets = new List<long>();
        long position = 0;

        void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes);
            position += bytes.Length;
        }

        void Begin(int number)
        {
            offsets.Add(position);
            Write($"{number} 0 obj\n");
        }

        string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        Write("%PDF-1.4\n");

        Begin(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        Begin(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        Begin(3);
        Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(pageWidth)} {Num(pageHeight)}] " +
              "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        var draw = $"q {Num(pageWidth)} 0 0 {Num(pageHeight)} 0 0 cm /Im0 Do Q\n";
        Begin(4);
        Write($"<< /Length {draw.Length} >>\nstream\n{draw}endstream\nendobj\n");

        Begin(5);
        Write($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB " +
              $"/BitsPerComponent 8 /Filter /FlateDecode /Length {packed.Length} >>\nstream\n");
        output.Write(packed);
        position += packed.Length;
        Write("\nendstream\nendobj\n");

        Begin(6);
        Write($"<< /Title {TextString(title)} /Producer (Class Schedule) >>\nendobj\n");

        var xref = position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    /// <summary>A PDF text string: plain when it is ASCII, otherwise UTF-16 with a byte-order mark.</summary>
    private static string TextString(string text)
    {
        if (text.All(c => c >= ' ' && c < 127 && c is not ('(' or ')' or '\\')))
            return "(" + text + ")";

        var bytes = Encoding.BigEndianUnicode.GetBytes(text);
        return "<FEFF" + Convert.ToHexString(bytes) + ">";
    }
}
