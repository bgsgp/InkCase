using System.Text.RegularExpressions;
using Lmm.Core.Model;

namespace Lmm.Import;

/// <summary>Result of importing an external file into an .lmm paper.</summary>
public sealed class ImportResult
{
    public required Paper Paper { get; init; }

    /// <summary>Human-readable notes: what was recognised, what was assumed, what was skipped.</summary>
    public List<string> Notes { get; init; } = [];
}

/// <summary>
/// Turns a plain-text exam (article paragraphs + numbered questions with A/B/C/D
/// options and an optional 答案/解析 line) into a <see cref="Paper"/>. This is the
/// offline, dependency-free first stage of the import pipeline; PDF/OCR and DOCX
/// front-ends will feed the same text shape into it.
/// </summary>
public static partial class PaperTextImporter
{
    public const string EnglishModuleId = "cn.examreview.subject.english.v1";

    [GeneratedRegex(@"^\s*(\d{1,2})\s*[.、．)]\s*(.*)$")]
    private static partial Regex QuestionStart();

    [GeneratedRegex(@"(?:^|\s)([A-H])\s*[.、．)]\s*")]
    private static partial Regex OptionMarker();

    [GeneratedRegex(@"(?:参考)?答案\s*[:：]?\s*([A-H]{1,4}|[^\s，,。]{1,20})")]
    private static partial Regex AnswerLine();

    [GeneratedRegex(@"(?:【?解析】?|Explanation)\s*[:：]\s*")]
    private static partial Regex ExplanationMarker();

    public static ImportResult Parse(string text, string title, string? subtitle = null)
    {
        var notes = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var firstQuestion = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (QuestionStart().IsMatch(lines[i])) { firstQuestion = i; break; }
        }

        var articleLines = firstQuestion < 0 ? lines : lines[..firstQuestion];
        var questionLines = firstQuestion < 0 ? [] : lines[firstQuestion..];

        var paragraphs = SplitParagraphs(articleLines);
        var questions = ParseQuestions(questionLines, notes);

        var section = new Section
        {
            Id = "sec-0001",
            TypeId = paragraphs.Count > 0 ? "reading-c" : "grammar-fill",
            Title = string.IsNullOrWhiteSpace(title) ? "导入的试卷" : title.Trim(),
            Meta = paragraphs.Count > 0 ? $"导入 · 约 {WordCount(paragraphs)} 词" : "导入 · 纯题目",
            Gist = "",
        };

        if (paragraphs.Count > 0)
        {
            section.Article = new Article
            {
                Paragraphs = paragraphs.Select((p, i) => new Paragraph { Index = i, Text = p }).ToList(),
            };
        }

        section.Questions.AddRange(questions);

        var paper = new Paper
        {
            Id = $"paper-{Guid.NewGuid():N}",
            ModuleId = EnglishModuleId,
            Title = section.Title,
            Subtitle = subtitle ?? "",
            CreatedAt = DateTimeOffset.Now,
            ModifiedAt = DateTimeOffset.Now,
        };
        paper.Sections.Add(section);

        if (paragraphs.Count == 0)
            notes.Add("没有识别到文章段落，按纯题目章节导入。");
        if (questions.Count == 0)
            notes.Add("没有识别到题目（需要形如 \"1. ...\" 的编号行）。");

        return new ImportResult { Paper = paper, Notes = notes };
    }

    public static ImportResult ParseFile(string path, string? title = null)
    {
        var text = File.ReadAllText(path);
        return Parse(text, title ?? Path.GetFileNameWithoutExtension(path));
    }

    private static List<string> SplitParagraphs(IReadOnlyList<string> lines)
    {
        var paragraphs = new List<string>();
        var buffer = new List<string>();

        void Flush()
        {
            if (buffer.Count == 0) return;
            var joined = string.Join(" ", buffer.Select(l => l.Trim()).Where(l => l.Length > 0));
            if (joined.Length > 0) paragraphs.Add(joined);
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) Flush();
            else buffer.Add(line);
        }
        Flush();
        return paragraphs;
    }

    private static List<Question> ParseQuestions(IReadOnlyList<string> lines, List<string> notes)
    {
        var questions = new List<Question>();
        string? current = null;
        int number = 0;

        void Flush()
        {
            if (current is null) return;
            questions.Add(BuildQuestion(number, current, notes));
            current = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var match = QuestionStart().Match(line);
            if (match.Success)
            {
                Flush();
                number = int.Parse(match.Groups[1].Value);
                current = match.Groups[2].Value.Trim();
            }
            else if (current is not null)
            {
                current += " " + line;
            }
        }

        Flush();
        return questions;
    }

    private static Question BuildQuestion(int number, string block, List<string> notes)
    {
        var answer = "";
        var explanation = "";

        var explanationMatch = ExplanationMarker().Match(block);
        if (explanationMatch.Success)
        {
            explanation = block[(explanationMatch.Index + explanationMatch.Length)..].Trim();
            block = block[..explanationMatch.Index].Trim();
        }

        var answerMatch = AnswerLine().Match(block);
        if (answerMatch.Success)
        {
            answer = answerMatch.Groups[1].Value.Trim().ToUpperInvariant();
            block = (block[..answerMatch.Index] + " " + block[(answerMatch.Index + answerMatch.Length)..]).Trim();
        }

        var markers = OptionMarker().Matches(block);
        var question = new Question
        {
            Number = number,
            Id = $"q-{number:D3}",
            CorrectAnswer = answer,
            Explanation = explanation,
        };

        if (markers.Count >= 2)
        {
            var stem = block[..markers[0].Index].Trim();
            question.Stem = stem.TrimEnd('（', '(', ' ', '：', ':');

            for (int i = 0; i < markers.Count; i++)
            {
                var start = markers[i].Index + markers[i].Length;
                var end = i + 1 < markers.Count ? markers[i + 1].Index : block.Length;
                question.Options.Add(new Option
                {
                    Letter = markers[i].Groups[1].Value,
                    Text = block[start..end].Trim().TrimEnd(';', '；', ','),
                });
            }
        }
        else
        {
            question.Stem = block.Trim();
        }

        if (question.Options.Count >= 2 && question.CorrectAnswer.Length == 0)
        {
            // The validator requires an answer for multiple-choice questions, and
            // silently inventing one would be worse than saying so: use the first
            // letter as a placeholder and report it.
            question.CorrectAnswer = question.Options[0].Letter;
            notes.Add($"第 {number} 题没解析到答案，暂以 {question.CorrectAnswer} 占位，请核对。");
        }

        return question;
    }

    private static int WordCount(IEnumerable<string> paragraphs) =>
        paragraphs.Sum(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
}
