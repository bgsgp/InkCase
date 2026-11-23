using Lmm.Core.Model;
using Lmm.Format;

namespace Lmm.Format.Tests;

public class RoundTripTests
{
    [Fact]
    public void WriteThenRead_PreservesEveryField()
    {
        var original = BuildSamplePaper();

        using var mem = new MemoryStream();
        new LmmPackageWriter().Write(original, mem);
        mem.Position = 0;
        var roundTripped = new LmmPackageReader().Read(mem);

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.ModuleId, roundTripped.ModuleId);
        Assert.Equal(original.Title, roundTripped.Title);
        Assert.Equal(original.Subtitle, roundTripped.Subtitle);
        Assert.Equal(original.CreatedAt, roundTripped.CreatedAt);
        Assert.Equal(original.ModifiedAt, roundTripped.ModifiedAt);

        Assert.Equal(original.Sections.Count, roundTripped.Sections.Count);
        var s0 = original.Sections[0];
        var r0 = roundTripped.Sections[0];
        Assert.Equal(s0.Id, r0.Id);
        Assert.Equal(s0.TypeId, r0.TypeId);
        Assert.Equal(s0.Title, r0.Title);
        Assert.Equal(s0.Meta, r0.Meta);
        Assert.Equal(s0.Gist, r0.Gist);

        Assert.NotNull(r0.Article);
        Assert.Equal(s0.Article!.Paragraphs.Count, r0.Article!.Paragraphs.Count);
        Assert.Equal(s0.Article.Paragraphs[0].Text, r0.Article.Paragraphs[0].Text);
        Assert.Equal(s0.Article.Paragraphs[0].Spans[0].Kind, r0.Article.Paragraphs[0].Spans[0].Kind);
        Assert.Equal(s0.Article.Paragraphs[0].Spans[0].QuestionRef, r0.Article.Paragraphs[0].Spans[0].QuestionRef);

        Assert.Single(r0.Structure);
        Assert.Equal(s0.Structure[0].ParaLabel, r0.Structure[0].ParaLabel);
        Assert.Equal(s0.Structure[0].QuestionRefs, r0.Structure[0].QuestionRefs);

        Assert.Equal(s0.Questions.Count, r0.Questions.Count);
        Assert.Equal(s0.Questions[0].Stem, r0.Questions[0].Stem);
        Assert.Equal(s0.Questions[0].CorrectAnswer, r0.Questions[0].CorrectAnswer);
        Assert.Equal(s0.Questions[0].DistractorAnalysis, r0.Questions[0].DistractorAnalysis);
        Assert.Equal(s0.Questions[0].Options[0].Letter, r0.Questions[0].Options[0].Letter);

        Assert.Equal(s0.Bilingual[0].En, r0.Bilingual[0].En);
        Assert.Equal(s0.Drills[0].Heading, r0.Drills[0].Heading);

        Assert.Equal(original.Assets.Count, roundTripped.Assets.Count);
        Assert.Equal(original.Assets[0].Bytes, roundTripped.Assets[0].Bytes);
        Assert.Equal(original.Assets[0].MimeType, roundTripped.Assets[0].MimeType);

        Assert.Equal(original.InkStrokes.Count, roundTripped.InkStrokes.Count);
        Assert.Equal(original.InkStrokes[0].Points[0].X, roundTripped.InkStrokes[0].Points[0].X, precision: 10);
        Assert.Equal(original.InkStrokes[0].WidthFactors, roundTripped.InkStrokes[0].WidthFactors);
    }

    /// <summary>
    /// Ink on a question's answer pane is anchored by section + surface + question
    /// number; all three have to survive a save/load, otherwise the strokes would
    /// come back on the article layer of whichever question happens to be selected.
    /// </summary>
    [Fact]
    public void WriteThenRead_KeepsAnswerPaneAnchors()
    {
        var paper = BuildSamplePaper();
        paper.InkStrokes.Add(new InkStroke
        {
            Id = "stroke-0002",
            Anchor = new InkAnchor { SectionId = "sec-0001", Surface = InkSurfaces.Answer, QuestionNumber = 7 },
            Color = "#D33A2C",
            Tool = "pen",
            BaseWidth = 5.0,
            Points = [new InkPoint(0.4, 0.4), new InkPoint(0.5, 0.5)],
            WidthFactors = [1.0, 1.0],
        });

        using var mem = new MemoryStream();
        new LmmPackageWriter().Write(paper, mem);
        mem.Position = 0;
        var loaded = new LmmPackageReader().Read(mem);

        var answer = loaded.InkStrokes.Single(s => s.Id == "stroke-0002");
        Assert.Equal(InkSurfaces.Answer, answer.Anchor.Surface);
        Assert.Equal(7, answer.Anchor.QuestionNumber);

        // Article strokes from older files have no Surface at all; null is read as
        // "article" by the app, so the writer must not invent a value for them.
        var article = loaded.InkStrokes.Single(s => s.Id == "stroke-0001");
        Assert.Null(article.Anchor.Surface);
    }

    /// <summary>
    /// Every right-hand pane gets its own ink layer; a stroke must come back on the
    /// pane it was drawn on, not lumped onto the passage.
    /// </summary>
    [Fact]
    public void WriteThenRead_KeepsRightPaneLayersApart()
    {
        var paper = BuildSamplePaper();
        var panes = new[] { InkSurfaces.Structure, InkSurfaces.Bilingual, InkSurfaces.Drills };
        foreach (var (surface, i) in panes.Select((s, i) => (s, i)))
        {
            paper.InkStrokes.Add(new InkStroke
            {
                Id = $"stroke-pane-{i}",
                Anchor = new InkAnchor { SectionId = "sec-0001", Surface = surface },
                Color = "#D33A2C",
                Tool = "pen",
                BaseWidth = 5.0,
                Points = [new InkPoint(0.2, 0.2), new InkPoint(0.3, 0.3)],
                WidthFactors = [1.0, 1.0],
            });
        }

        using var mem = new MemoryStream();
        new LmmPackageWriter().Write(paper, mem);
        mem.Position = 0;
        var loaded = new LmmPackageReader().Read(mem);

        foreach (var (surface, i) in panes.Select((s, i) => (s, i)))
        {
            var stroke = loaded.InkStrokes.Single(s => s.Id == $"stroke-pane-{i}");
            Assert.Equal(surface, stroke.Anchor.Surface);
            Assert.Null(stroke.Anchor.QuestionNumber);
        }
    }

    [Fact]
    public void Read_ChunksInkStrokesAcrossMultipleFiles()
    {
        var paper = BuildSamplePaper();
        for (int i = 0; i < 1200; i++)
        {
            paper.InkStrokes.Add(new InkStroke
            {
                Id = $"stroke-{i}",
                Anchor = new InkAnchor { SectionId = "sec-0001" },
                Points = [new InkPoint(0.1, 0.1), new InkPoint(0.2, 0.2)],
                WidthFactors = [1.0, 1.0],
            });
        }

        using var mem = new MemoryStream();
        new LmmPackageWriter().Write(paper, mem);
        mem.Position = 0;

        using (var archive = new System.IO.Compression.ZipArchive(mem, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            var inkEntries = archive.Entries.Where(e => e.FullName.StartsWith("ink/", StringComparison.Ordinal)).ToList();
            Assert.True(inkEntries.Count > 1, "expected ink strokes to be chunked across more than one file");
        }

        mem.Position = 0;
        var roundTripped = new LmmPackageReader().Read(mem);
        Assert.Equal(paper.InkStrokes.Count, roundTripped.InkStrokes.Count);
    }

    private static Paper BuildSamplePaper()
    {
        var question = new Question
        {
            Number = 1,
            Id = "q-0001",
            SkillTag = "main-idea",
            Stem = "What is the main idea?",
            Options = [new Option { Letter = "A", Text = "..." }, new Option { Letter = "B", Text = "..." }],
            CorrectAnswer = "A",
            Explanation = "Because ...",
            DistractorAnalysis = "B is wrong because ...",
        };

        var section = new Section
        {
            Id = "sec-0001",
            TypeId = "reading-c",
            Title = "Reading C",
            Meta = "320 words",
            Gist = "A passage about ...",
            Article = new Article
            {
                Paragraphs =
                [
                    new Paragraph
                    {
                        Index = 0,
                        Text = "This is the first paragraph of the passage.",
                        Spans = [new Span { Kind = SpanKind.Evidence, Start = 0, Length = 4, QuestionRef = 1 }],
                    }
                ],
            },
            Structure = [new StructureRow { ParaLabel = "Para 1", Function = "intro", ContentNote = "sets the scene", QuestionRefs = [1] }],
            Questions = [question],
            Bilingual = [new BilingualPair { En = "Hello.", Zh = "你好。" }],
            Drills = [new Drill { Heading = "Practice", Question = "Translate: Hello.", Answer = "你好。" }],
        };

        return new Paper
        {
            Id = "paper-0001",
            ModuleId = "cn.examreview.subject.english.v1",
            Title = "2024 Mock Exam",
            Subtitle = "",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ModifiedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            Sections = [section],
            Assets = [new PaperAsset { Id = "img-0001", MimeType = "image/png", Bytes = [1, 2, 3, 4, 5] }],
            InkStrokes =
            [
                new InkStroke
                {
                    Id = "stroke-0001",
                    Anchor = new InkAnchor { SectionId = "sec-0001" },
                    Color = "#D33A2C",
                    BaseWidth = 3.0,
                    Points = [new InkPoint(0.1234, 0.5678), new InkPoint(0.2, 0.6)],
                    WidthFactors = [0.9, 1.0],
                }
            ],
        };
    }
}
