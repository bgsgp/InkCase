using System.IO.Compression;
using System.Text;
using Lmm.Import;

namespace Lmm.Import.Tests;

public class DocumentImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inkcase-docx-" + Guid.NewGuid().ToString("N"));

    public DocumentImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>手搓一个最小 .docx：只要 word/document.xml，Word 能读，我们的抽取器也能读。</summary>
    private string WriteDocx(string fileName, params string[] paragraphs)
    {
        var path = Path.Combine(_dir, fileName);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = zip.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));

        var body = new StringBuilder();
        foreach (var paragraph in paragraphs) body.Append(paragraph);

        writer.Write("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
            """ + body + """
              </w:body>
            </w:document>
            """);

        return path;
    }

    private static string Wp(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    [Fact]
    public void ReadDocx_KeepsParagraphsAndLineBreaks()
    {
        // 一段里带 <w:br/>（软换行）和 <w:tab/>，都要保留下来。
        var path = WriteDocx("a.docx",
            Wp("The ocean absorbs carbon."),
            "<w:p><w:r><w:t>First line</w:t><w:br/><w:t>Second line</w:t><w:tab/><w:t>Tabbed</w:t></w:r></w:p>",
            Wp("Last paragraph."));

        var text = DocumentTextExtractor.ReadDocx(path);

        Assert.Contains("The ocean absorbs carbon.", text);
        Assert.Contains("First line\nSecond line\tTabbed", text);
        Assert.EndsWith("Last paragraph.", text);
        // 段落之间空一行，这样 PaperTextImporter 才会切成段。
        Assert.Contains("carbon.\n\nFirst line", text);
    }

    [Fact]
    public void ParseFile_DocxBecomesReadingSection()
    {
        var path = WriteDocx("paper.docx",
            Wp("The ocean has quietly absorbed carbon dioxide for decades."),
            Wp("Every spring plankton bloom across the surface."),
            Wp("1. What does the ocean absorb?"),
            Wp("A. Oxygen  B. Carbon dioxide  C. Sand  D. Salt"),
            Wp("答案：B"),
            Wp("2. Why does the author mention plankton? A. To show the pump works  B. To describe a holiday"));

        var result = PaperTextImporter.ParseFile(path, "从 Word 导入");
        var section = Assert.Single(result.Paper.Sections);

        Assert.Equal("从 Word 导入", result.Paper.Title);
        Assert.Equal("reading-c", section.TypeId);
        Assert.NotNull(section.Article);
        Assert.Equal(2, section.Article!.Paragraphs.Count);
        Assert.Equal(2, section.Questions.Count);
        Assert.Equal("B", section.Questions[0].CorrectAnswer);
        Assert.Equal(4, section.Questions[0].Options.Count);
    }

    [Fact]
    public void ParseFile_RejectsUnknownExtension()
    {
        var path = Path.Combine(_dir, "notes.xyz");
        File.WriteAllText(path, "whatever");

        var ex = Assert.Throws<ImportFormatException>(() => PaperTextImporter.ParseFile(path));
        Assert.Contains("不支持的文件类型", ex.Message);
    }

    [Fact]
    public void ParseFile_BrokenDocxSaysSoInsteadOfCrashing()
    {
        var path = Path.Combine(_dir, "broken.docx");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("this is not a zip"));

        var ex = Assert.Throws<ImportFormatException>(() => PaperTextImporter.ParseFile(path));
        Assert.Contains("不是一个有效的 .docx", ex.Message);
    }

    [Fact]
    public void IsImportable_CoversTextAndWord()
    {
        Assert.True(PaperTextImporter.IsImportable("a.txt"));
        Assert.True(PaperTextImporter.IsImportable("A.DOCX"));
        Assert.True(PaperTextImporter.IsImportable("a.doc"));
        Assert.False(PaperTextImporter.IsImportable("a.pdf"));
        Assert.False(PaperTextImporter.IsImportable("a.lmm"));
    }
}
