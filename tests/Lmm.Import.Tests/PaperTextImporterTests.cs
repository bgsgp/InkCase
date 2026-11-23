using Lmm.Import;

namespace Lmm.Import.Tests;

public class PaperTextImporterTests
{
    private const string Sample = """
        The ocean absorbs carbon. It hides the machinery well.

        Every spring plankton bloom across the surface.

        1. What does the ocean absorb?
        A. Oxygen  B. Carbon dioxide  C. Sand  D. Salt

        2. Why does the author mention plankton?
        A. To show the pump works  B. To describe a holiday
        答案：A
        解析：因为浮游植物是碳泵的起点。

        3. What is the tone of the passage?
        A. Neutral  B. Angry
        """;

    [Fact]
    public void Parse_SplitsArticleAndQuestions()
    {
        var result = PaperTextImporter.Parse(Sample, "海洋的碳泵");
        var section = Assert.Single(result.Paper.Sections);

        Assert.Equal("海洋的碳泵", result.Paper.Title);
        Assert.Equal("reading-c", section.TypeId);
        Assert.NotNull(section.Article);
        Assert.Equal(2, section.Article!.Paragraphs.Count);
        Assert.Equal(3, section.Questions.Count);
    }

    [Fact]
    public void Parse_ReadsOptionsAnswersAndExplanation()
    {
        var result = PaperTextImporter.Parse(Sample, "t");
        var q1 = result.Paper.Sections[0].Questions[0];
        var q2 = result.Paper.Sections[0].Questions[1];

        Assert.Equal("What does the ocean absorb?", q1.Stem);
        Assert.Equal(["A", "B", "C", "D"], q1.Options.Select(o => o.Letter));
        Assert.Equal("Carbon dioxide", q1.Options[1].Text);

        Assert.Equal("A", q2.CorrectAnswer);
        Assert.Contains("浮游植物", q2.Explanation);
    }

    [Fact]
    public void Parse_MissingAnswerGetsPlaceholderAndANote()
    {
        var result = PaperTextImporter.Parse(Sample, "t");
        var q3 = result.Paper.Sections[0].Questions[2];

        Assert.Equal("A", q3.CorrectAnswer);
        Assert.Contains(result.Notes, n => n.Contains("第 3 题"));
    }

    [Fact]
    public void Parse_NoQuestions_ProducesArticleOnlySection()
    {
        var result = PaperTextImporter.Parse("Just a passage.\n\nSecond paragraph.", "p");
        var section = Assert.Single(result.Paper.Sections);

        Assert.Empty(section.Questions);
        Assert.Equal(2, section.Article!.Paragraphs.Count);
        Assert.Contains(result.Notes, n => n.Contains("没有识别到题目"));
    }

    [Fact]
    public void Parse_WrappedLinesBecomeOneParagraph()
    {
        var result = PaperTextImporter.Parse("line one\nline two\n\nnext para", "p");
        Assert.Equal("line one line two", result.Paper.Sections[0].Article!.Paragraphs[0].Text);
    }

    [Fact]
    public void Parse_WrappedQuestionLinesPreserveSingleSpaces()
    {
        var result = PaperTextImporter.Parse("1. first line\nsecond line\nthird line\nA. one  B. two", "t");
        var question = result.Paper.Sections[0].Questions[0];

        Assert.Equal("first line second line third line", question.Stem);
        Assert.Equal(["one", "two"], question.Options.Select(o => o.Text));
    }
}
