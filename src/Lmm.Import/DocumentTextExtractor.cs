using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lmm.Import;

/// <summary>把文件读不成文本时抛这个；消息直接给用户看。</summary>
public sealed class ImportFormatException(string message) : Exception(message);

/// <summary>
/// 从 .txt / .md / .docx / .doc 里取出纯文本，交给 <see cref="PaperTextImporter"/>。
/// .docx 走 OOXML（直接读 zip 里的 word/document.xml，不引第三方包）；
/// .doc 是 Word 97 二进制格式，本机有 Word 时用 Word 另存成 .docx 再读，
/// 没有 Word 就给一句明确的提示，而不是丢一个看不懂的异常。
/// </summary>
public static class DocumentTextExtractor
{
    public static string Extract(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".markdown" => File.ReadAllText(path),
            ".docx" => ReadDocx(path),
            ".doc" => ReadLegacyDoc(path),
            _ => throw new ImportFormatException(
                $"不支持的文件类型：{Path.GetFileName(path)}。可以导入 .txt / .md / .docx / .doc。"),
        };
    }

    /// <summary>读 .docx（OOXML zip）里的正文。段落之间空一行，段落内的换行/制表符保留。</summary>
    public static string ReadDocx(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = zip.GetEntry("word/document.xml")
                ?? throw new ImportFormatException(
                    $"这个 .docx 里找不到 word/document.xml，可能不是 Word 文档：{Path.GetFileName(path)}");

            using var body = entry.Open();
            var xml = XDocument.Load(body);
            return ExtractParagraphs(xml);
        }
        catch (InvalidDataException)
        {
            throw new ImportFormatException(
                $"打不开 {Path.GetFileName(path)}：它不是一个有效的 .docx（可能其实是旧版 .doc 或者文件损坏）。");
        }
    }

    /// <summary>从 document.xml 取出段落文本。表格里的段落也算（按文档顺序出现）。</summary>
    internal static string ExtractParagraphs(XDocument xml)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = new List<string>();
        foreach (var paragraph in xml.Descendants(w + "p"))
        {
            var text = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == w + "t") text.Append(node.Value);
                else if (node.Name == w + "tab") text.Append('\t');
                else if (node.Name == w + "br" || node.Name == w + "cr") text.Append('\n');
                // w:instrText / w:delText 之类不是正文，跳过。
            }

            var value = text.ToString().TrimEnd();
            if (value.Trim().Length > 0) paragraphs.Add(value);
        }

        return string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// .doc（Word 97-2003 二进制）。先按 zip 试一次（有些文件扩展名写错成 .doc 其实是 .docx），
    /// 再退回本机 Word 走另存为。整条链路失败也只返回一句人话。
    /// </summary>
    private static string ReadLegacyDoc(string path)
    {
        if (LooksLikeZip(path))
        {
            try { return ReadDocx(path); } catch (ImportFormatException) { /* 继续走 Word */ }
        }

        if (!OperatingSystem.IsWindows())
            throw new ImportFormatException("旧版 .doc 只有 Windows + Word 才能自动转换");

        var temp = Path.Combine(Path.GetTempPath(), $"inkcase-{Guid.NewGuid():N}.docx");
        try
        {
            ConvertWithWord(path, temp);
            return ReadDocx(temp);
        }
        catch (Exception ex)
        {
            throw new ImportFormatException(
                $"这是旧版 .doc 格式，墨案自己读不了（{(ex is ImportFormatException ? ex.Message : ex.GetType().Name)}）。" +
                Environment.NewLine +
                "请用 Word / WPS 打开后「另存为」.docx 再导入；本机装了 Word 的话也会自动转换，但这次没成功。");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            return stream.Read(header) == 4 && header[0] == 0x50 && header[1] == 0x4B;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>用本机 Word（COM）把文档另存为 .docx。目标机器没装 Word 时这里会抛异常。</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ConvertWithWord(string source, string destination)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
            ?? throw new ImportFormatException("本机没有安装 Microsoft Word，无法自动转换 .doc");

        dynamic app = Activator.CreateInstance(type)!;
        try
        {
            app.Visible = false;
            app.DisplayAlerts = 0;

            dynamic doc = app.Documents.Open(source, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            try
            {
                doc.SaveAs2(destination, 16 /* wdFormatDocumentDefault = .docx */);
            }
            finally
            {
                doc.Close(0 /* wdDoNotSaveChanges */);
            }
        }
        finally
        {
            try { app.Quit(0); } catch { /* Quit 失败就算了，下面 ReleaseComObject 已经尽力 */ }
            System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }
}
