using Lmm.Core.Model;
using Lmm.Plugins.Abstractions;

namespace Lmm.Plugins.English;

/// <summary>
/// Content checks for a section. Kept as plain static logic so it can be unit
/// tested without spinning up the whole module.
/// </summary>
public static class EnglishSectionValidator
{
    public static ValidationResult Validate(Section section)
    {
        var issues = new List<ValidationIssue>();
        var path = string.IsNullOrWhiteSpace(section.Title) ? section.Id : section.Title;

        if (string.IsNullOrWhiteSpace(section.Id))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "章节缺少 id。", path));
        if (string.IsNullOrWhiteSpace(section.TypeId))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "章节缺少 typeId（题型）。", path));
        else if (!EnglishQuestionTypes.SectionTypes.Contains(section.TypeId, StringComparer.OrdinalIgnoreCase))
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"未知章节类型 \"{section.TypeId}\"，将按普通文本显示。", path));

        ValidateArticle(section, path, issues);
        ValidateQuestions(section, path, issues);
        ValidateStructure(section, path, issues);

        for (int i = 0; i < section.Bilingual.Count; i++)
        {
            var pair = section.Bilingual[i];
            if (string.IsNullOrWhiteSpace(pair.En) || string.IsNullOrWhiteSpace(pair.Zh))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"中英对照第 {i + 1} 条缺少英文或中文。", $"{path}.bilingual[{i}]"));
        }

        if (section.TypeId == "writing" && section.Questions.Count == 0 && string.IsNullOrWhiteSpace(section.Gist))
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "写作章节既没有题目也没有摘要。", path));

        return ValidationResult.FromIssues(issues);
    }

    private static void ValidateArticle(Section section, string path, List<ValidationIssue> issues)
    {
        var requiresArticle = EnglishQuestionTypes.RequiresArticle(section.TypeId);
        if (requiresArticle && (section.Article is null || section.Article.Paragraphs.Count == 0))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, $"\"{section.TypeId}\" 章节缺少文章正文。", $"{path}.article"));
            return;
        }

        if (section.Article is null) return;

        for (int p = 0; p < section.Article.Paragraphs.Count; p++)
        {
            var paragraph = section.Article.Paragraphs[p];
            if (string.IsNullOrEmpty(paragraph.Text))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"第 {p + 1} 段是空的。", $"{path}.article.paragraphs[{p}]"));

            for (int s = 0; s < paragraph.Spans.Count; s++)
            {
                var span = paragraph.Spans[s];
                var tooLong = span.Start < 0 || span.Length <= 0 || span.Start + span.Length > paragraph.Text.Length;
                if (tooLong)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"高亮/挖空区间越界（start={span.Start}, length={span.Length}, 段落长度={paragraph.Text.Length}）。",
                        $"{path}.article.paragraphs[{p}].spans[{s}]"));
            }
        }
    }

    private static void ValidateQuestions(Section section, string path, List<ValidationIssue> issues)
    {
        var seenNumbers = new HashSet<int>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var questionNumbers = section.Questions.Select(q => q.Number).ToHashSet();

        foreach (var question in section.Questions)
        {
            var questionPath = $"{path}.questions[{question.Number}]";

            if (!seenNumbers.Add(question.Number))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"题号 {question.Number} 重复。", questionPath));
            if (!string.IsNullOrWhiteSpace(question.Id) && !seenIds.Add(question.Id))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"题目 id \"{question.Id}\" 重复。", questionPath));
            if (string.IsNullOrWhiteSpace(question.Stem))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"第 {question.Number} 题题干为空。", questionPath));

            var isChoice = question.Options.Count > 0;
            if (!isChoice) continue;

            var letters = question.Options.Select(o => o.Letter.Trim().ToUpperInvariant()).ToList();
            var duplicateLetters = letters.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var letter in duplicateLetters)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"第 {question.Number} 题选项 {letter} 重复。", questionPath));
            foreach (var option in question.Options.Where(o => string.IsNullOrWhiteSpace(o.Text)))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"第 {question.Number} 题选项 {option.Letter} 没有内容。", questionPath));

            var answer = (question.CorrectAnswer ?? string.Empty).Trim().ToUpperInvariant();
            if (answer.Length == 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"第 {question.Number} 题没有正确答案。", questionPath));
            else if (!letters.Contains(answer))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"第 {question.Number} 题答案 \"{question.CorrectAnswer}\" 不在选项里。", questionPath));
        }

        foreach (var questionNumber in section.Structure.SelectMany(r => r.QuestionRefs).Where(n => !questionNumbers.Contains(n)).Distinct())
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"段落结构引用了不存在的题号 {questionNumber}。", $"{path}.structure"));
    }

    private static void ValidateStructure(Section section, string path, List<ValidationIssue> issues)
    {
        for (int i = 0; i < section.Structure.Count; i++)
        {
            var row = section.Structure[i];
            if (string.IsNullOrWhiteSpace(row.ParaLabel) && string.IsNullOrWhiteSpace(row.Function))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"段落结构第 {i + 1} 行是空的。", $"{path}.structure[{i}]"));
        }
    }
}
