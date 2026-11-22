using Lmm.Core.Model;
using Lmm.Plugins.Abstractions;

namespace Lmm.Plugins.English;

/// <summary>Standard card data for an English question, choice or not.</summary>
public sealed class EnglishQuestionCard(Question question, string questionTypeId) : IQuestionCardViewModel
{
    public string QuestionId { get; } = question.Id;

    public int Number { get; } = question.Number;

    public string SkillTag { get; } = question.SkillTag;

    public string Stem { get; } = question.Stem;

    public IReadOnlyList<QuestionOptionCard> Options { get; } =
        question.Options.Select(o => new QuestionOptionCard(o.Letter, o.Text)).ToList();

    public string CorrectAnswer { get; } = question.CorrectAnswer;

    public string Explanation { get; } = question.Explanation;

    public string DistractorAnalysis { get; } = question.DistractorAnalysis;

    public bool IsChoice { get; } = question.Options.Count > 0;

    public string QuestionTypeId { get; } = questionTypeId;

    public Question Source { get; } = question;
}
