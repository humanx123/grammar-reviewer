using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace GrammarBubble.App.Services;

/// Extrae el texto de un documento (.txt, .md, .docx o .pdf) para revisarlo.
public static class DocumentReader
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = ext switch
        {
            ".txt" or ".md" => File.ReadAllText(path),
            ".docx" => ReadDocx(path),
            ".pdf" => ReadPdf(path),
            _ => throw new InvalidOperationException("Formato no compatible. Usa PDF, Word (.docx), .txt o .md.")
        };

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(ext == ".pdf"
                ? "El PDF no tiene texto seleccionable (puede ser escaneado). Usa «Capturar área» para leerlo."
                : "El archivo no tiene texto.");
        return text.Trim();
    }

    private static string ReadDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("El .docx no es válido o no contiene texto.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var paragraphs = document.Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)));
        return string.Join("\n", paragraphs);
    }

    private static string ReadPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        return string.Join("\n\n", document.GetPages().Select(page => page.Text));
    }
}
