using Lmm.Core.Model;
using Lmm.Format;
using Lmm.Plugins.Abstractions;
using Lmm.Plugins.English;

namespace Lmm.Plugins.English.Tests;

public class EnglishModuleTests
{
    private readonly EnglishSubjectModule _module = new();

    [Fact]
    public void SamplePaper_PassesItsOwnValidation()
    {
        var paper = EnglishSamplePaper.Create();
        Assert.Equal(2, paper.Sections.Count);

        foreach (var section in paper.Sections)
        {
            var result = _module.Validate(section);
            Assert.True(result.IsValid, string.Join("; ", result.ErrorMessages));
        }
    }

    [Fact]
    public void SamplePaper_RoundTripsThroughLmmContainer()
    {
        var paper = EnglishSamplePaper.Create();
        using var stream = new MemoryStream();
        new LmmPackageWriter().Write(paper, stream);
        stream.Position = 0;

        var reloaded = new LmmPackageReader().Read(stream);

        Assert.Equal(paper.Id, reloaded.Id);
        Assert.Equal(EnglishSubjectModule.Id, reloaded.ModuleId);
        Assert.Equal(paper.Sections.Count, reloaded.Sections.Count);
        Assert.Equal(paper.Sections[0].Questions.Count, reloaded.Sections[0].Questions.Count);
        Assert.Equal(paper.Sections[0].Article!.Paragraphs[1].Spans[0].QuestionRef,
                     reloaded.Sections[0].Article!.Paragraphs[1].Spans[0].QuestionRef);
    }

    [Fact]
    public void ResolveHandler_ReturnsChoiceHandler_ForReadingChoice()
    {
        var handler = _module.ResolveHandler(EnglishQuestionTypes.ReadingChoice);
        var question = EnglishSamplePaper.Create().Sections[0].Questions[0];

        var card = Assert.IsAssignableFrom<IQuestionCardViewModel>(handler.CreateReviewViewModel(question));
        Assert.Equal(question.Number, card.Number);
        Assert.Equal(4, card.Options.Count);
        Assert.Equal("A", card.CorrectAnswer);
    }

    [Fact]
    public void ResolveHandler_FallsBack_ForUnknownType()
    {
        var handler = _module.ResolveHandler("future-type-2099");
        var card = Assert.IsAssignableFrom<IQuestionCardViewModel>(
            handler.CreateReviewViewModel(new Question { Number = 1, Stem = "x" }));

        Assert.Equal("future-type-2099", handler.QuestionTypeId);
        Assert.Equal(1, card.Number);
    }

    [Fact]
    public void Validate_FlagsAnswerNotAmongOptions()
    {
        var section = EnglishSamplePaper.Create().Sections[0];
        section.Questions[0].CorrectAnswer = "E";

        var result = _module.Validate(section);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("不在选项里"));
    }

    [Fact]
    public void Validate_FlagsDuplicateQuestionNumbers()
    {
        var section = EnglishSamplePaper.Create().Sections[0];
        section.Questions[1].Number = section.Questions[0].Number;

        var result = _module.Validate(section);

        Assert.Contains(result.Issues, i => i.Message.Contains("重复"));
    }

    [Fact]
    public void Validate_FlagsSpanPastParagraphEnd()
    {
        var section = EnglishSamplePaper.Create().Sections[0];
        var span = section.Article!.Paragraphs[0].Spans[0];
        span.Length = section.Article.Paragraphs[0].Text.Length + 10;

        var result = _module.Validate(section);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("越界"));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    public void Validate_FlagsOverflowingAndNegativeSpanLengths(int length)
    {
        var section = EnglishSamplePaper.Create().Sections[0];
        var span = section.Article!.Paragraphs[0].Spans[0];
        span.Start = 1;
        span.Length = length;

        var result = _module.Validate(section);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("越界"));
    }

    [Fact]
    public void Validate_RequiresArticle_ForReadingSection()
    {
        var section = EnglishSamplePaper.Create().Sections[0];
        section.Article = null;

        var result = _module.Validate(section);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("缺少文章正文"));
    }
}
