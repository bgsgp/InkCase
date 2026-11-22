using Lmm.Core.Model;

namespace Lmm.Plugins.Abstractions;

public sealed record QuestionOptionCard(string Letter, string Text);

/// <summary>
/// The shape the built-in review/editor card renders. Handlers may return
/// richer objects, but implementing this interface is the cheap way to get the
/// standard card without the shell knowing anything about the subject.
/// </summary>
public interface IQuestionCardViewModel
{
    string QuestionId { get; }
    int Number { get; }
    string SkillTag { get; }
    string Stem { get; }
    IReadOnlyList<QuestionOptionCard> Options { get; }
    string CorrectAnswer { get; }
    string Explanation { get; }
    string DistractorAnalysis { get; }
    bool IsChoice { get; }

    Question Source { get; }
}
