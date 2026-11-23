using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Lmm.Core.Ink;
using Lmm.Core.Model;
using Lmm.Format;
using Lmm.Import;
using Lmm.Plugins.Abstractions;
using Lmm.Plugins.English;
using Lmm.Translation;
using Microsoft.Win32;
using Section = Lmm.Core.Model.Section;

namespace InkCase.App;

/// <summary>
/// The review shell. It knows nothing about English: the paper's moduleId is
/// resolved through <see cref="IModuleCatalog"/> and every subject-specific
/// decision (validation, question cards) goes through <see cref="ISubjectModule"/>.
/// The shell starts empty — no built-in paper — so every piece of content comes
/// from an .lmm file opened or imported by the user.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush EvidenceBrush = Frozen(Color.FromRgb(0xFF, 0xF1, 0xB8));
    private static readonly Brush EvidenceBrushStrong = Frozen(Color.FromRgb(0xFF, 0xD5, 0x5A));
    private static readonly Brush CorrectBrush = Frozen(Color.FromRgb(0x1B, 0x7F, 0x3B));
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x64, 0x6A, 0x72));
    private static readonly Brush InkBrush = Frozen(Color.FromRgb(0x1B, 0x1E, 0x23));

    /// <summary>版本号来自 Directory.Build.props（大版本.小版本.本小版本提交数.总提交数）。</summary>
    private static string AppVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var info = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "0.0.0.0";

        // SourceLink 会在 informational version 后面追加 "+<sha>"，界面里不要。
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    private readonly IModuleCatalog _catalog;
    private readonly LmmPackageReader _reader;
    private readonly LmmPackageWriter _writer;
    private readonly ITranslationProvider _translator;

    private Paper? _paper;
    private ISubjectModule? _module;
    private Section? _section;
    private string? _path;
    private bool _dirty;
    private int _selectedQuestionNumber;
    private double _zoom = 1.0;
    private bool _revealAll;

    public MainWindow(
        IModuleCatalog catalog,
        LmmPackageReader reader,
        LmmPackageWriter writer,
        ITranslationProvider translator)
    {
        _catalog = catalog;
        _reader = reader;
        _writer = writer;
        _translator = translator;
        InitializeComponent();
        SetInkTool(InkTool.Pen);
        VersionText.Text = "v" + AppVersion();

        // The default 1440x920 doesn't fit every screen (e.g. a 1280x720 panel at
        // 150% scaling reports ~850x460 DIPs). Clamp to the work area so no window
        // button is pushed off-screen, and start maximised when the panel is small
        // enough that a floating window would be cramped.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 20);
        Height = Math.Min(Height, area.Height - 20);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (area.Width < 1200 || area.Height < 760) WindowState = WindowState.Maximized;

        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += (_, _) => MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        UpdateZoomLabel();
        ShowEmpty();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---------- window chrome ----------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { /* drag ended early; nothing to do */ }
        }
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---------- paper lifecycle ----------

    private void ShowEmpty()
    {
        EmptyState.Visibility = Visibility.Visible;
        SectionList.ItemsSource = null;
        ClearSection();
        SubtitleText.Text = ".lmm 试卷 · 未打开";
        Title = "墨案";
        UpdateStatus("就绪");
    }

    private void OpenPath(string path)
    {
        try
        {
            var paper = _reader.Read(path);
            if (TrySetPaper(paper, path))
                UpdateStatus($"已打开：{path}");
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
        Title = $"{paper.Title} — {module.DisplayName} — 墨案";
        SubtitleText.Text = string.IsNullOrWhiteSpace(path) ? $"{paper.Title} · 未保存" : Path.GetFileName(path);
        EmptyState.Visibility = Visibility.Collapsed;

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
            Filter = "墨案 试卷 (*.lmm)|*.lmm|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        OpenPath(dialog.FileName);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;

        var dialog = new OpenFileDialog
        {
            Title = "导入试卷",
            Filter = "试卷文件 (*.txt;*.md;*.docx;*.doc)|*.txt;*.md;*.docx;*.doc|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        ImportPath(dialog.FileName);
    }

    private void ImportPath(string path)
    {
        ImportResult result;
        try
        {
            result = PaperTextImporter.ParseFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "无法读取文件", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (ImportFormatException ex)
        {
            MessageBox.Show(this, ex.Message, "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result.Notes.Count > 0)
        {
            MessageBox.Show(this,
                "导入时有这些情况需要留意：" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, result.Notes.Select(n => "• " + n)),
                "导入提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (!TrySetPaper(result.Paper, path: null)) return;

        _dirty = true;
        var section = result.Paper.Sections.FirstOrDefault();
        UpdateStatus($"已导入：{Path.GetFileName(path)}" +
                     (section is null ? "" : $"（{section.Questions.Count} 题，{section.Article?.Paragraphs.Count ?? 0} 段）"));
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
            Filter = "墨案 试卷 (*.lmm)|*.lmm",
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
            SubtitleText.Text = Path.GetFileName(path);
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
            UpdateStatus($"校验通过，{warnings} 条提醒；正在保存…");
        return true;
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
            case Key.I:
                Import_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Open (or import) a file from the command line or a shell drop.</summary>
    public void OpenExternal(string path)
    {
        if (path.EndsWith(".lmm", StringComparison.OrdinalIgnoreCase)) OpenPath(path);
        else if (PaperTextImporter.IsImportable(path)) ImportPath(path);
        else UpdateStatus($"不支持的文件类型：{Path.GetFileName(path)}");
    }

    // ---------- drag & drop ----------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;
        if (!ConfirmDiscard()) return;

        var file = files[0];
        if (file.EndsWith(".lmm", StringComparison.OrdinalIgnoreCase)) OpenPath(file);
        else if (PaperTextImporter.IsImportable(file)) ImportPath(file);
        else UpdateStatus($"不支持的文件类型：{Path.GetFileName(file)}");
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
        ArticleTitle.Text = "";
        ArticleMeta.Text = "";
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

        ArticleTitle.Text = _section.Title;
        ArticleMeta.Text = string.IsNullOrWhiteSpace(_section.Gist)
            ? _section.Meta
            : $"{_section.Meta}    {_section.Gist}";

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

        double bodySize = 17 * _zoom;
        double lineHeight = 32 * _zoom;

        foreach (var paragraph in _section.Article.Paragraphs.OrderBy(p => p.Index))
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = bodySize,
                LineHeight = lineHeight,
                Foreground = InkBrush,
                Margin = new Thickness(0, 0, 0, 16 * _zoom),
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
            Foreground = InkBrush,
            Margin = new Thickness(0, 2, 0, 8),
        });

        DetailPanel.Children.Add(Body(question.Stem, 14.5));

        if (question.Options.Count > 0)
        {
            foreach (var option in question.Options)
            {
                var line = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 13.5,
                };
                var isAnswer = _revealAll &&
                               string.Equals(option.Letter?.Trim(), question.CorrectAnswer?.Trim(), StringComparison.OrdinalIgnoreCase);
                line.Inlines.Add(new Run(option.Letter + ". ")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isAnswer ? CorrectBrush : InkBrush,
                });
                line.Inlines.Add(new Run(option.Text) { Foreground = isAnswer ? CorrectBrush : InkBrush });
                DetailPanel.Children.Add(line);
            }
        }

        if (!_revealAll)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "答案与解析已隐藏 · 顶部「显示全部答案」可一键展开",
                FontSize = 12,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer))
            DetailPanel.Children.Add(SectionLabel($"答案：{question.CorrectAnswer}", CorrectBrush));

        if (!string.IsNullOrWhiteSpace(question.Explanation))
            DetailPanel.Children.Add(SectionLabel("解析" + Environment.NewLine + question.Explanation));

        if (!string.IsNullOrWhiteSpace(question.DistractorAnalysis))
            DetailPanel.Children.Add(SectionLabel("干扰项分析" + Environment.NewLine + question.DistractorAnalysis));
    }

    private static TextBlock Body(string text, double size = 14) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        LineHeight = size * 1.65,
        Foreground = InkBrush,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock SectionLabel(string text, Brush? foreground = null) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 13,
        LineHeight = 21,
        Margin = new Thickness(0, 10, 0, 0),
        Foreground = foreground ?? Frozen(Color.FromRgb(0x44, 0x44, 0x44)),
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
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13.5, LineHeight = 22 };
            block.Inlines.Add(new Run(row.ParaLabel + "  ") { FontWeight = FontWeights.Bold, Foreground = InkBrush });
            block.Inlines.Add(new Run(row.Function + "  ") { Foreground = Frozen(Color.FromRgb(0x0D, 0x5A, 0x53)) });
            block.Inlines.Add(new Run(row.ContentNote) { Foreground = InkBrush });
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
            BilingualPanel.Children.Add(Body("这一节还没有中英对照。切到「中英对照」后点「机翻本节」可由 MyMemory 自动生成，并随试卷保存。"));
            return;
        }

        foreach (var pair in _section.Bilingual)
        {
            BilingualPanel.Children.Add(new TextBlock
            {
                Text = pair.En,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 23,
                Foreground = InkBrush,
                FontFamily = new FontFamily("Georgia, Segoe UI"),
                Margin = new Thickness(0, 8, 0, 3),
            });
            BilingualPanel.Children.Add(new TextBlock
            {
                Text = pair.Zh,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13.5,
                LineHeight = 23,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 0, 10),
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
                FontSize = 13.5,
                Foreground = InkBrush,
                Margin = new Thickness(0, 10, 0, 4),
            });
            DrillPanel.Children.Add(Body(drill.Question));
            DrillPanel.Children.Add(new TextBlock
            {
                Text = drill.Answer,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = CorrectBrush,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }
    }

    // ---------- view options ----------

    private void RevealToggle_Changed(object sender, RoutedEventArgs e)
    {
        _revealAll = RevealToggle.IsChecked == true;
        RevealToggle.Content = _revealAll ? "隐藏全部答案" : "显示全部答案";
        if (QuestionList.SelectedItem is QuestionEntry entry) RenderDetail(entry.Question);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom + 0.1);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom - 0.1);

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(Math.Round(zoom, 2), 0.8, 2.0);
        UpdateZoomLabel();
        RenderArticle();
    }

    private void UpdateZoomLabel() => ZoomText.Text = $"{_zoom * 100:0}%";

    // ---------- translation ----------

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (_section?.Article is null || _section.Article.Paragraphs.Count == 0)
        {
            UpdateStatus("这一节没有文章可翻译。");
            return;
        }

        var section = _section;
        TranslateBtn.IsEnabled = false;
        TranslateHint.Text = $"{_translator.Name} 翻译中…";

        try
        {
            var pairs = new List<BilingualPair>();
            foreach (var paragraph in section.Article.Paragraphs.OrderBy(p => p.Index))
            {
                var zh = await _translator.TranslateAsync(paragraph.Text, "en", "zh-CN");
                pairs.Add(new BilingualPair { En = paragraph.Text, Zh = zh });
            }

            section.Bilingual.Clear();
            section.Bilingual.AddRange(pairs);
            _dirty = true;
            RenderBilingual();
            RightTabs.SelectedIndex = 2;
            TranslateHint.Text = $"已翻译 {pairs.Count} 段（{_translator.Name}）";
        }
        catch (Exception ex) when (ex is TranslationException or HttpRequestException or TaskCanceledException)
        {
            TranslateHint.Text = "翻译失败：" + ex.Message;
        }
        finally
        {
            TranslateBtn.IsEnabled = true;
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

    private void InkStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button) SetInkTool(InkStyle.ParseTool(button.Tag as string));
    }

    /// <summary>
    /// Switches pens. The picker behaves like a radio group (the buttons are plain
    /// toggle buttons, so the handler has to enforce "exactly one checked"), and the
    /// highlighter takes its own ink colour because a red highlighter reads as a
    /// strike-through rather than a highlight.
    /// </summary>
    private void SetInkTool(InkTool tool)
    {
        InkCanvas.Pen = InkStyle.For(tool);
        InkCanvas.InkColor = tool == InkTool.Highlighter
            ? Color.FromRgb(0xE9, 0xA9, 0x3B)
            : Color.FromRgb(0xD3, 0x3A, 0x2C);

        var buttons = new (ToggleButton Button, InkTool Tool)[]
        {
            (InkStylePen, InkTool.Pen),
            (InkStyleDashed, InkTool.Dashed),
            (InkStyleCalligraphy, InkTool.Calligraphy),
            (InkStyleSpeed, InkTool.Speed),
            (InkStyleTaper, InkTool.Taper),
            (InkStyleHighlighter, InkTool.Highlighter),
        };
        foreach (var (button, t) in buttons) button.IsChecked = t == tool;

        if (InkToggle.IsChecked != true) InkToggle.IsChecked = true;
        UpdateStatus($"笔：{ToolName(tool)}");
    }

    private static string ToolName(InkTool tool) => tool switch
    {
        InkTool.Dashed => "虚线",
        InkTool.Calligraphy => "书法",
        InkTool.Speed => "速度",
        InkTool.Taper => "收锋",
        InkTool.Highlighter => "荧光",
        _ => "钢笔",
    };

    private void InkCanvas_StrokeFinished(object? sender, InkStroke stroke)
    {
        _paper?.InkStrokes.Add(stroke);
        _dirty = true;
        UpdateStatus("已添加一笔批注");
    }

    private void UndoInk_Click(object sender, RoutedEventArgs e)
    {
        if (_paper is null || _section is null) return;

        var last = _paper.InkStrokes.LastOrDefault(s =>
            string.Equals(s.Anchor.SectionId, _section.Id, StringComparison.Ordinal));
        if (last is null)
        {
            UpdateStatus("这一节没有可撤销的笔迹");
            return;
        }

        _paper.InkStrokes.Remove(last);
        RenderInk();
        _dirty = true;
        UpdateStatus("已撤销一笔");
    }

    private void ClearInk_Click(object sender, RoutedEventArgs e)
    {
        if (_paper is null || _section is null) return;

        var removed = _paper.InkStrokes.RemoveAll(s =>
            string.Equals(s.Anchor.SectionId, _section.Id, StringComparison.Ordinal));
        RenderInk();
        if (removed > 0) _dirty = true;
        UpdateStatus(removed > 0 ? $"已清除本节 {removed} 笔" : "这一节没有笔迹");
    }

    // ---------- misc ----------

    private void UpdateStatus(string? message = null)
    {
        if (message is not null)
        {
            StatusText.Text = message;
            return;
        }

        StatusText.Text = _paper is null
            ? "就绪"
            : $"{_paper.Sections.Count} 节 · {_paper.Sections.Sum(s => s.Questions.Count)} 题" +
              (_dirty ? " · 未保存" : "");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "paper" : clean;
    }
}

internal sealed class SectionEntry(Section section)
{
    public Section Section { get; } = section;

    public string Header => string.IsNullOrWhiteSpace(Section.Title) ? Section.Id : Section.Title;

    public string Sub => string.IsNullOrWhiteSpace(Section.Meta) ? Section.TypeId : $"{Section.TypeId} · {Section.Meta}";
}

internal sealed class QuestionEntry(Question question)
{
    public Question Question { get; } = question;

    public string Header => string.IsNullOrWhiteSpace(Question.SkillTag)
        ? $"第 {Question.Number} 题"
        : $"第 {Question.Number} 题 · {Question.SkillTag}";

    public string Preview => Question.Stem;
}
