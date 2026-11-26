using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Z10.Core.Localization;

namespace Z10.App;

/// <summary>
/// Übersetzt die in XAML fest eingetragenen (deutschen) Texte einer Ansicht nach dem Laden in die aktive Sprache.
/// So bleibt XAML lesbar, und neue Texte brauchen nur einen Eintrag in der Sprachdatei.
/// </summary>
public static class UiTranslator
{
    public static void Apply(DependencyObject root)
    {
        if (Loc.Current == Loc.German) return;
        Visit(root);
    }

    private static void Visit(DependencyObject d)
    {
        switch (d)
        {
            case Window w:
                w.Title = T(w.Title);
                break;
            case TextBlock tb when !BindingOperations.IsDataBound(tb, TextBlock.TextProperty):
                if (tb.Inlines.Count > 1 || tb.Inlines.FirstInline is not Run) TranslateInlines(tb.Inlines);
                else tb.Text = T(tb.Text);
                break;
            case HeaderedContentControl hc when hc.Header is string h:
                hc.Header = T(h);
                break;
            case DataGrid grid:
                foreach (var col in grid.Columns) if (col.Header is string ch) col.Header = T(ch);
                break;
        }
        if (d is ContentControl cc && cc.Content is string s && !BindingOperations.IsDataBound(cc, ContentControl.ContentProperty))
            cc.Content = T(s);
        if (d is FrameworkElement fe && fe.ToolTip is string tip)
            fe.ToolTip = T(tip);
        if (d is ItemsControl ic && ic.ItemsSource is null)
            for (int i = 0; i < ic.Items.Count; i++)
                if (ic.Items[i] is string item) ic.Items[i] = T(item);

        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            Visit(child);
    }

    private static void TranslateInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run r && !BindingOperations.IsDataBound(r, Run.TextProperty)) r.Text = T(r.Text);
            else if (inline is Span span) TranslateInlines(span.Inlines);
        }
    }

    private static string T(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        if (!Loc.TryTranslate(trimmed, out var t)) return text;
        // führende/abschließende Leerzeichen erhalten (z. B. " Bausteine · ")
        int lead = text.Length - text.TrimStart().Length, trail = text.Length - text.TrimEnd().Length;
        return text[..lead] + t + text[(text.Length - trail)..];
    }
}
