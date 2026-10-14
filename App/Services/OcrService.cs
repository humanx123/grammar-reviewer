using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GrammarBubble.App.Services;

/// OCR local con el motor integrado de Windows (Windows.Media.Ocr). Sin internet ni claves.
public static class OcrService
{
    private const int MinLongestSide = 1200;
    private static readonly Regex WordPattern = new(@"\p{L}{2,}");

    /// Lee el texto de una imagen PNG. Prueba español e inglés (los paquetes instalados) y devuelve
    /// el resultado con más palabras reconocibles.
    public static async Task<string> RecognizeAsync(byte[] png)
    {
        var engines = CreateEngines();
        if (engines.Count == 0)
            throw new InvalidOperationException(
                "Windows no tiene paquetes OCR instalados. Instala el idioma español o inglés en Configuración > Idioma.");

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var candidates = new List<string>();
        foreach (var engine in engines)
        {
            var result = await engine.RecognizeAsync(bitmap);
            candidates.Add(string.Join("\n", result.Lines.Select(line => line.Text)).Trim());
        }

        var best = candidates.MaxBy(text => WordPattern.Matches(text).Count) ?? "";
        if (string.IsNullOrWhiteSpace(best))
            throw new InvalidOperationException("No se detectó texto en el área seleccionada.");
        return best;
    }

    /// Amplía las capturas pequeñas (el texto pequeño se lee mal) y las codifica como PNG.
    public static byte[] Prepare(Bitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        var scale = longest < MinLongestSide ? Math.Min(2.0, (double)MinLongestSide / longest) : 1.0;
        var width = (int)(source.Width * scale);
        var height = (int)(source.Height * scale);

        using var resized = new Bitmap(width, height);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, width, height);
        }

        using var buffer = new MemoryStream();
        resized.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
        return buffer.ToArray();
    }

    private static List<OcrEngine> CreateEngines()
    {
        var installed = OcrEngine.AvailableRecognizerLanguages;
        var engines = new List<OcrEngine>();
        foreach (var family in new[] { "es", "en" })
        {
            var tag = PickTag(installed, family);
            if (tag is null) continue;
            var engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
            if (engine is not null) engines.Add(engine);
        }
        return engines;
    }

    /// Prefiere la variante principal de cada familia (es-ES, en-US) y acepta cualquiera instalada.
    private static string? PickTag(IReadOnlyList<Language> installed, string family)
    {
        var preferred = family == "es" ? new[] { "es-ES", "es-MX" } : new[] { "en-US", "en-GB" };
        var tags = installed.Select(l => l.LanguageTag)
            .Where(tag => tag.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int Rank(string tag)
        {
            var index = Array.IndexOf(preferred, tag);
            return index >= 0 ? index : preferred.Length;
        }
        return tags.OrderBy(Rank).FirstOrDefault();
    }
}
