using Lmm.Core.Model;

namespace Lmm.Plugins.English;

/// <summary>
/// The built-in demo paper ("内置英语示例卷"). It exists so the shell can be
/// opened and the whole host → format → plugin path exercised without needing
/// an import pipeline first; the content is one reading section plus a cloze
/// section, enough to cover evidence spans, blanks, structure rows, bilingual
/// pairs and drills.
/// </summary>
public static class EnglishSamplePaper
{
    public static Paper Create()
    {
        var now = DateTimeOffset.Now;
        return new Paper
        {
            Id = "sample-english-0001",
            ModuleId = EnglishSubjectModule.Id,
            Title = "示例卷：海洋的碳泵",
            Subtitle = "内置示例 · 阅读理解 + 完形填空",
            CreatedAt = now,
            ModifiedAt = now,
            Sections = [CreateReadingSection(), CreateClozeSection()],
        };
    }

    private static Section CreateReadingSection()
    {
        var paragraphs = new List<Paragraph>
        {
            Paragraph(0,
                "For decades the ocean has quietly absorbed close to a third of the carbon dioxide that humans release. The service is invisible, but the machinery behind it is not: every spring, billions of tonnes of microscopic plants bloom across the surface, bloom and sink, carrying carbon down with them.",
                ("carrying carbon down with them", SpanKind.Evidence, 1)),
            Paragraph(1,
                "Scientists call this slow cascade the biological carbon pump. It begins when phytoplankton use sunlight to turn carbon dioxide into living tissue. When they die, or are eaten and excreted, a fraction of that carbon turns into particles heavy enough to sink into the deep sea, where it can stay locked away for centuries.",
                ("a fraction of that carbon turns into particles heavy enough to sink", SpanKind.Evidence, 2)),
            Paragraph(2,
                "The pump is not a simple conveyor belt, however. Only a tiny share of the carbon ever reaches the seafloor; most is eaten, respired and returned to the surface as carbon dioxide. A 2023 study found that the pump's efficiency varies far more than models assumed, and that the difference could double the ocean's estimated carbon storage capacity.",
                ("the pump's efficiency varies far more than models assumed", SpanKind.Evidence, 3)),
            Paragraph(3,
                "That uncertainty matters for climate policy. If the ocean's uptake is larger than expected, the world has slightly more room to emit; if it weakens as waters warm, the same emissions produce more warming. Neither outcome is a reason to relax. The ocean is a buffer, not a substitute for cutting greenhouse gases."),
        };

        var questions = new List<Question>
        {
            Choice(1, "细节理解",
                "What does the biological carbon pump mainly do?",
                [("A", "It moves carbon dioxide from the sea surface into the deep ocean."),
                 ("B", "It turns sunlight directly into carbon dioxide."),
                 ("C", "It stops carbon from ever returning to the surface."),
                 ("D", "It removes carbon from the atmosphere by itself.")],
                "A",
                "文章第 2 段说明：浮游植物固定碳，死亡或被吞食后一部分碳变成可下沉的颗粒进入深海。",
                "B 把光合作用的过程说反了；C 错在 ever，第 3 段说大部分碳会被分解并返回表层；D 过度推断，海洋是缓冲，不是自行清除。"),

            Choice(2, "词句理解",
                "What does the phrase \"a slow cascade\" in paragraph 2 refer to?",
                [("A", "The rapid growth of phytoplankton in spring."),
                 ("B", "The gradual downward movement of carbon through the food web."),
                 ("C", "The rise of warm water from the deep sea."),
                 ("D", "The seasonal change in ocean currents.")],
                "B",
                "cascade 指碳一步步往下沉的连续过程：浮游植物固定 → 被吃/死亡 → 颗粒下沉，是一个渐进的传递链。",
                "A 只讲表层繁殖，方向不对；C 讲上升流，与下沉相反；D 在文中没有依据。"),

            Choice(3, "推理判断",
                "Why does the author mention the 2023 study?",
                [("A", "To prove that the ocean absorbs more carbon than expected."),
                 ("B", "To show that the pump's storage capacity is still uncertain."),
                 ("C", "To criticise climate models as unreliable."),
                 ("D", "To argue that policy should wait for better data.")],
                "B",
                "2023 年研究发现碳泵效率的波动远大于模型假设，可能让海洋储碳能力翻倍——强调的是不确定性，而不是某个确定结论。",
                "A 只说可能更大，不是证明；C 过度否定模型；D 第 4 段明确说不应因此拖延减排。"),

            Choice(4, "主旨大意",
                "What is the author's main message in the last paragraph?",
                [("A", "The ocean can replace emission cuts if its uptake is large."),
                 ("B", "Scientists should stop studying the carbon pump."),
                 ("C", "The ocean helps, but it cannot substitute for reducing emissions."),
                 ("D", "Warming oceans absorb more carbon, so emissions matter less.")],
                "C",
                "末段 The ocean is a buffer, not a substitute for cutting greenhouse gases. 就是主旨：海洋是缓冲，不能替代减排。",
                "A、D 都把它当替代品；B 与文中强调不确定性的态度相反。"),
        };

        return new Section
        {
            Id = "sec-0001",
            TypeId = "reading-c",
            Title = "阅读理解 C",
            Meta = "说明文 · 约 320 词 · 难度中等",
            Gist = "海洋生物碳泵如何把碳带进深海，以及这种能力的不确定性和政策含义。",
            Article = new Article { Paragraphs = paragraphs },
            Structure =
            [
                new StructureRow { ParaLabel = "Para 1", Function = "引入现象", ContentNote = "海洋吸收大量二氧化碳，浮游植物是那台机器。", QuestionRefs = [1] },
                new StructureRow { ParaLabel = "Para 2", Function = "解释机制", ContentNote = "生物碳泵的定义与下沉过程。", QuestionRefs = [2] },
                new StructureRow { ParaLabel = "Para 3", Function = "补充限制", ContentNote = "效率远不如传送带，且波动很大。", QuestionRefs = [3] },
                new StructureRow { ParaLabel = "Para 4", Function = "点明主旨", ContentNote = "海洋是缓冲不是替代，减排不能松。", QuestionRefs = [4] },
            ],
            Questions = questions,
            Bilingual =
            [
                new BilingualPair { En = "The ocean is a buffer, not a substitute for cutting greenhouse gases.", Zh = "海洋是缓冲，不能替代削减温室气体。" },
                new BilingualPair { En = "Only a tiny share of the carbon ever reaches the seafloor.", Zh = "只有很小一部分碳最终到达海底。" },
            ],
            Drills =
            [
                new Drill { Heading = "重点搭配", Question = "carry carbon down / lock carbon away", Answer = "把碳带下去 / 把碳锁定起来" },
            ],
        };
    }

    private static Section CreateClozeSection()
    {
        var p1 = Paragraph(0,
            "Every year the ocean takes up roughly a ___1___ of the carbon dioxide released by human activity.",
            ("___1___", SpanKind.Blank, 1));
        var p2 = Paragraph(1,
            "Researchers once treated this uptake as a ___2___ number, but new measurements suggest that it changes with temperature and with the ___3___ of life in the water.",
            ("___2___", SpanKind.Blank, 2),
            ("___3___", SpanKind.Blank, 3));

        var questions = new List<Question>
        {
            Choice(1, "完形·名词",
                "第 1 空",
                [("A", "quarter"), ("B", "third"), ("C", "half"), ("D", "tenth")],
                "B", "科学界常引用约三分之一，quarter/half/tenth 都与常识数据不符。", "A 四分之一偏小；C 一半明显夸大；D 十分之一过小。"),
            Choice(2, "完形·形容词",
                "第 2 空",
                [("A", "fixed"), ("B", "random"), ("C", "tiny"), ("D", "endless")],
                "A", "but 转折提示：人们曾以为这个数字是固定的，新测量发现它会变。", "B 与曾以为的对比不成立；C、D 与上下文搭配不通。"),
            Choice(3, "完形·名词",
                "第 3 空",
                [("A", "colour"), ("B", "amount"), ("C", "sound"), ("D", "taste")],
                "B", "与温度并列的是水中生命的数量，amount 与前文对吸收量级的讨论一致。", "A、C、D 都无法与温度并列描述海洋吸收能力的变化。"),
        };

        return new Section
        {
            Id = "sec-0002",
            TypeId = "cloze",
            Title = "完形填空",
            Meta = "科普短文 · 3 空",
            Gist = "海洋碳吸收量并非固定值，会随温度和生物量变化。",
            Article = new Article { Paragraphs = [p1, p2] },
            Questions = questions,
        };
    }

    private static Paragraph Paragraph(
        int index,
        string text,
        params (string Phrase, SpanKind Kind, int QuestionRef)[] spans) => new()
        {
            Index = index,
            Text = text,
            Spans = spans.Select(s =>
            {
                var start = text.IndexOf(s.Phrase, StringComparison.Ordinal);
                if (start < 0) throw new InvalidOperationException($"示例卷文本里找不到片段：{s.Phrase}");
                return new Span { Kind = s.Kind, Start = start, Length = s.Phrase.Length, QuestionRef = s.QuestionRef };
            }).ToList(),
        };

    private static Question Choice(
        int number,
        string skillTag,
        string stem,
        (string Letter, string Text)[] options,
        string correctAnswer,
        string explanation,
        string distractorAnalysis) => new()
        {
            Number = number,
            Id = $"q{number:D3}",
            SkillTag = skillTag,
            Stem = stem,
            Options = options.Select(o => new Option { Letter = o.Letter, Text = o.Text }).ToList(),
            CorrectAnswer = correctAnswer,
            Explanation = explanation,
            DistractorAnalysis = distractorAnalysis,
        };
}
