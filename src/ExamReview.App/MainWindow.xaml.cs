using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Lmm.Core.Model;
using Lmm.Format;
using Lmm.Plugins.Abstractions;
using Lmm.Plugins.English;
using Microsoft.Win32;
using Section = Lmm.Core.Model.Section;

namespace ExamReview.App;

/// <summary>
/// The review shell. It knows nothing about English: the paper's moduleId is
/// resolved through <see cref="IModuleCatalog"/> and every subject-specific
/// decision (validation, question cards) goes through <see cref="ISubjectModule"/>.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush EvidenceBrush = Frozen(Color.FromRgb(0xFF, 0xF1, 0xB8));
    private static readonly Brush EvidenceBrushStrong = Frozen(Color.FromRgb(0xFF, 0xD5, 0x5A));
    private static readonly Brush CorrectBrush = Frozen(Color.FromRgb(0x1B, 0x7F, 0x3B));
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x66, 0x66, 0x66));

    private readonly IModuleCatalog _catalog;
    private readonly LmmPackageReader _reader;
    private readonly LmmPackageWriter _writer;

    private Paper? _paper;
    private ISubjectModule? _module;
    private Section? _section;
    private string? _path;
    private bool _dirty;
    private int _selectedQuestionNumber;

    public MainWindow(IModuleCatalog catalog, LmmPackageReader reader, LmmPackageWriter writer)
    {
        _catalog = catalog;
        _reader = reader;
        _writer = writer;
        InitializeComponent();

        // 860x1380 doesn't fit every screen (e.g. 1280x720 laptops); clamp to the
        // work area so the toolbar and status bar are never off-screen.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 20);
        Height = Math.Min(Height, area.Height - 20);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        PreviewKeyDown += OnPreviewKeyDown;
        LoadSample();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---------- paper lifecycle ----------

    private void LoadSample()
    {
        TrySetPaper(EnglishSamplePaper.Create(), path: null);
    }

    private bool TrySetPaper(Paper paper, string? path)
    {
        var module = _catalog.Find(paper.ModuleId);
        if (module is null)
        {
            // Per the plugin contract this must be an explicit, readable error —
            // never a silent fallback to a generic renderer.
            var ex = new ModuleNotInstalledException(paper.ModuleId);
            MessageBox.Show(this, ex.Message, "无法打开试卷", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _paper = paper;
        _module = module;
        _path = path;
        _dirty = false;
        _selectedQuestionNumber = 0;
        Title = $"{paper.Title} — {module.DisplayName} — ExamReview";

        var entries = paper.Sections.Select(s => new SectionEntry(s)).ToList();
        SectionList.ItemsSource = entries;
        if (entries.Count > 0)
            SectionList.SelectedIndex = 0;
        else
            ClearSection();

        UpdateStatus();
        return true;
    }

    private void OpenPaper_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;

        var dialog = new OpenFileDialog
        {
            Title = "打开试卷",
            Filter = "ExamReview 试卷 (*.lmm)|*.lmm|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var paper = _reader.Read(dialog.FileName);
            if (TrySetPaper(paper, dialog.FileName))
                UpdateStatus($"已打开：{dialog.FileName}");
        }
        catch (LmmFormatException ex)
        {
            MessageBox.Show(this, ex.Message, "文件格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "无法读取文件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePaper_Click(object sender, RoutedEventArgs e)
    {
        if (_paper is null) return;
        if (_path is null) { SaveAsPaper_Click(sender, e); return; }
        SaveTo(_path);
    }

    private void SaveAsPaper_Click(object sender, RoutedEventArgs e)
    {
        if (_paper is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "保存试卷",
            Filter = "ExamReview 试卷 (*.lmm)|*.lmm",
            FileName = $"{SanitizeFileName(_paper.Title)}.lmm",
        };
        if (dialog.ShowDialog(this) != true) return;
        SaveTo(dialog.FileName);
    }

    private void SaveTo(string path)
    {
        if (_paper is null || _module is null) return;
        if (!ValidateBeforeSave()) return;

        try
        {
            _paper.ModifiedAt = DateTimeOffset.Now;
            _writer.Write(_paper, path);
            _path = path;
            _dirty = false;
            UpdateStatus($"已保存：{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "无法保存文件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ValidateBeforeSave()
    {
        if (_paper is null || _module is null) return false;

        var issues = _paper.Sections.SelectMany(s => _module.Validate(s).Issues).ToList();
        var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var text = string.Join(Environment.NewLine, errors.Take(12).Select(i => "• " + i));
            if (errors.Count > 12) text += Environment.NewLine + $"… 另有 {errors.Count - 12} 条";
            MessageBox.Show(this, "内容校验没通过，未保存：" + Environment.NewLine + Environment.NewLine + text,
                "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
        if (warnings > 0)
            StatusText.Text = $"校验通过，{warnings} 条提醒；正在保存…";
        return true;
    }

    private void NewSample_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        LoadSample();
        UpdateStatus("已载入内置示例卷");
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, "当前试卷有未保存的修改，仍要放弃吗？", "未保存的修改",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        return answer == MessageBoxResult.Yes;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.O:
                OpenPaper_Click(sender, e);
                e.Handled = true;
                break;
            case Key.S:
                SavePaper_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    // ---------- section rendering ----------

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not SectionEntry entry) return;
        _section = entry.Section;
        _selectedQuestionNumber = 0;
        RenderSection();
        UpdateStatus();
    }

    private void ClearSection()
    {
        _section = null;
        ArticleHeader.Text = "";
        ArticlePanel.Children.Clear();
        QuestionList.ItemsSource = null;
        DetailPanel.Children.Clear();
        StructurePanel.Children.Clear();
        BilingualPanel.Children.Clear();
        DrillPanel.Children.Clear();
        InkCanvas.SetStrokes([]);
    }

    private void RenderSection()
    {
        if (_section is null) { ClearSection(); return; }

        ArticleHeader.Text = string.IsNullOrWhiteSpace(_section.Gist)
            ? $"{_section.Title}  ·  {_section.Meta}"
            : $"{_section.Title}  ·  {_section.Meta}\n{_section.Gist}";
        ArticleHeader.TextWrapping = TextWrapping.Wrap;
        ArticleHeader.Inlines.Clear();
        ArticleHeader.Inlines.Add(new Run($"{_section.Title}   ") { FontWeight = FontWeights.SemiBold, FontSize = 16 });
        if (!string.IsNullOrWhiteSpace(_section.Meta))
            ArticleHeader.Inlines.Add(new Run(_section.Meta + "   ") { Foreground = MutedBrush });
        if (!string.IsNullOrWhiteSpace(_section.Gist))
            ArticleHeader.Inlines.Add(new Run("\n" + _section.Gist) { Foreground = MutedBrush });

        RenderArticle();

        QuestionList.ItemsSource = _section.Questions.Select(q => new QuestionEntry(q)).ToList();
        if (_section.Questions.Count > 0)
            QuestionList.SelectedIndex = 0;
        else
            DetailPanel.Children.Clear();

        RenderStructure();
        RenderBilingual();
        RenderDrills();
        RenderInk();
    }

    private void RenderArticle()
    {
        ArticlePanel.Children.Clear();
        if (_section?.Article is null) return;

        foreach (var paragraph in _section.Article.Paragraphs.OrderBy(p => p.Index))
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                LineHeight = 27,
                Margin = new Thickness(0, 0, 0, 14),
                FontFamily = new FontFamily("Georgia, Cambria, Segoe UI"),
            };

            var position = 0;
            foreach (var span in paragraph.Spans.OrderBy(s => s.Start))
            {
                if (span.Start < position || span.Start + span.Length > paragraph.Text.Length) continue;
                if (span.Start > position)
                    block.Inlines.Add(new Run(paragraph.Text[position..span.Start]));

                var run = new Run(paragraph.Text.Substring(span.Start, span.Length));
                if (span.Kind == SpanKind.Evidence)
                {
                    run.Background = span.QuestionRef == _selectedQuestionNumber ? EvidenceBrushStrong : EvidenceBrush;
                }
                else
                {
                    run.Foreground = Brushes.SteelBlue;
                    run.FontWeight = FontWeights.SemiBold;
                    run.TextDecorations = TextDecorations.Underline;
                }

                block.Inlines.Add(run);
                position = span.Start + span.Length;
            }

            if (position < paragraph.Text.Length)
                block.Inlines.Add(new Run(paragraph.Text[position..]));

            ArticlePanel.Children.Add(block);
        }
    }

    private void QuestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuestionList.SelectedItem is not QuestionEntry entry) return;
        _selectedQuestionNumber = entry.Question.Number;
        RenderArticle();
        RenderDetail(entry.Question);
        UpdateStatus();
    }

    private void RenderDetail(Question question)
    {
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(new TextBlock
        {
            Text = $"第 {question.Number} 题" + (string.IsNullOrWhiteSpace(question.SkillTag) ? "" : $"  ·  {question.SkillTag}"),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 6),
        });

        DetailPanel.Children.Add(Body(question.Stem));

        if (_module is not null && question.Options.Count > 0)
        {
            var handler = _module.ResolveHandler(GuessQuestionType(question));
            if (handler.CreateReviewViewModel(question) is IQuestionCardViewModel card)
            {
                foreach (var option in card.Options)
                {
                    var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), FontSize = 13 };
                    var isAnswer = string.Equals(option.Letter?.Trim(), card.CorrectAnswer?.Trim(), StringComparison.OrdinalIgnoreCase);
                    line.Inlines.Add(new Run(option.Letter + ". ") { FontWeight = FontWeights.SemiBold, Foreground = isAnswer ? CorrectBrush : null });
                    line.Inlines.Add(new Run(option.Text) { Foreground = isAnswer ? CorrectBrush : null });
                    DetailPanel.Children.Add(line);
                }

                DetailPanel.Children.Add(SectionLabel($"答案：{card.CorrectAnswer}"));
            }
        }
        else if (!string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            DetailPanel.Children.Add(SectionLabel($"答案：{question.CorrectAnswer}"));
        }

        if (!string.IsNullOrWhiteSpace(question.Explanation))
            DetailPanel.Children.Add(SectionLabel("解析\n" + question.Explanation));

        if (!string.IsNullOrWhiteSpace(question.DistractorAnalysis))
            DetailPanel.Children.Add(SectionLabel("干扰项分析\n" + question.DistractorAnalysis));
    }

    private string GuessQuestionType(Question question)
    {
        if (_section?.TypeId == "cloze") return EnglishQuestionTypes.ClozeChoice;
        if (_section?.TypeId == "seven-of-five") return EnglishQuestionTypes.SevenOfFive;
        return EnglishQuestionTypes.ReadingChoice;
    }

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        LineHeight = 23,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 13,
        Margin = new Thickness(0, 10, 0, 0),
        Foreground = Frozen(Color.FromRgb(0x44, 0x44, 0x44)),
    };

    private void RenderStructure()
    {
        StructurePanel.Children.Clear();
        if (_section is null) return;

        if (_section.Structure.Count == 0)
        {
            StructurePanel.Children.Add(Body("这一节没有段落结构表。"));
            return;
        }

        foreach (var row in _section.Structure)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13 };
            block.Inlines.Add(new Run(row.ParaLabel + "  ") { FontWeight = FontWeights.Bold });
            block.Inlines.Add(new Run(row.Function + "  ") { Foreground = Brushes.SteelBlue });
            block.Inlines.Add(new Run(row.ContentNote));
            if (row.QuestionRefs.Count > 0)
                block.Inlines.Add(new Run($"  → 第 {string.Join("、", row.QuestionRefs)} 题") { Foreground = MutedBrush });
            StructurePanel.Children.Add(block);
        }
    }

    private void RenderBilingual()
    {
        BilingualPanel.Children.Clear();
        if (_section is null) return;

        if (_section.Bilingual.Count == 0)
        {
            BilingualPanel.Children.Add(Body("这一节没有中英对照。"));
            return;
        }

        foreach (var pair in _section.Bilingual)
        {
            BilingualPanel.Children.Add(new TextBlock
            {
                Text = pair.En,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                FontFamily = new FontFamily("Georgia, Segoe UI"),
                Margin = new Thickness(0, 6, 0, 2),
            });
            BilingualPanel.Children.Add(new TextBlock
            {
                Text = pair.Zh,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }

    private void RenderDrills()
    {
        DrillPanel.Children.Clear();
        if (_section is null) return;

        if (_section.Drills.Count == 0)
        {
            DrillPanel.Children.Add(Body("这一节没有课堂小练。"));
            return;
        }

        foreach (var drill in _section.Drills)
        {
            DrillPanel.Children.Add(new TextBlock
            {
                Text = drill.Heading,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 2),
            });
            DrillPanel.Children.Add(Body(drill.Question));
            DrillPanel.Children.Add(new TextBlock
            {
                Text = drill.Answer,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = CorrectBrush,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }

    // ---------- ink ----------

    private void RenderInk()
    {
        InkCanvas.SectionId = _section?.Id;
        var strokes = _paper is null
            ? []
            : _paper.InkStrokes.Where(s => string.Equals(s.Anchor.SectionId, _section?.Id, StringComparison.Ordinal));
        InkCanvas.SetStrokes(strokes);
        InkCanvas.Visibility = InkToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        InkCanvas.InkEnabled = InkToggle.IsChecked == true;
    }

    private void InkToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (InkCanvas is null) return;
        var on = InkToggle.IsChecked == true;
        InkCanvas.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        InkCanvas.InkEnabled = on;
    }

    private void InkCanvas_StrokeFinished(object? sender, InkStroke stroke)
    {
        _paper?.InkStrokes.Add(stroke);
        _dirty = true;
        UpdateStatus("已添加一笔批注");
    }

    private void ClearInk_Click(object sender, RoutedEventArgs e)
    {
        if (_paper is null || _section is null) return;

        var removed = _paper.InkStrokes.RemoveAll(s => string.Equals(s.Anchor.SectionId, _section.Id, StringComparison.Ordinal));
        if (removed == 0) return;

        InkCanvas.ClearInk();
        _dirty = true;
        UpdateStatus($"已清除本节 {removed} 笔批注");
    }

    // ---------- misc ----------

    private void UpdateStatus(string? message = null)
    {
        if (_paper is null || _module is null)
        {
            StatusText.Text = message ?? "就绪";
            return;
        }

        var parts = new List<string> { message ?? _paper.Title };
        parts.Add($"模块：{_module.DisplayName}");
        if (_section is not null) parts.Add($"章节：{_section.Title}");
        parts.Add(_dirty ? "未保存" : "已保存");
        StatusText.Text = string.Join("    ", parts);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "paper" : cleaned;
    }

    public sealed class SectionEntry(Section section)
    {
        public Section Section { get; } = section;

        public string Header => string.IsNullOrWhiteSpace(Section.Title) ? Section.Id : Section.Title;

        public string Sub => string.IsNullOrWhiteSpace(Section.Meta) ? Section.TypeId : $"{Section.TypeId} · {Section.Meta}";
    }

    public sealed class QuestionEntry(Question question)
    {
        public Question Question { get; } = question;

        public string Header => $"Q{Question.Number}" + (string.IsNullOrWhiteSpace(Question.SkillTag) ? "" : $" · {Question.SkillTag}");

        public string Preview => Question.Stem ?? "";
    }
}
