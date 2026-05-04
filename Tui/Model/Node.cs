using CL = global::Console.Lib;

namespace CodeProject.Syntax.LALR.Tui.Model;

/// <summary>
/// Generic tree node for the grammar/lexer trees. Holds a label (the plain-
/// text content, used for status-line breadcrumb), an optional secondary tag
/// rendered dimmed on the right, and a styled glyph that decorates the row.
/// Children are eagerly populated by the builder classes — these trees are
/// small (dozens to hundreds of nodes) so lazy loading isn't needed.
/// </summary>
internal sealed class Node : CL.ITreeNode<Node>
{
    public Node(string label, NodeKind kind, IReadOnlyList<Node>? children = null, string? rightTag = null)
    {
        Label = label;
        Kind = kind;
        Children = children ?? [];
        RightTag = rightTag;
    }

    public string Label { get; }
    public NodeKind Kind { get; }
    public string? RightTag { get; }
    public IReadOnlyList<Node> Children { get; }
    public bool HasChildren => Children.Count > 0;

    /// <summary>Plain-text label used by the status bar (no escape sequences).</summary>
    public string PlainTitle => RightTag is null ? Label : $"{Label}    {RightTag}";

    public string FormatNodeContent(int width, CL.ColorMode mode, bool isSelected)
    {
        if (width <= 0) return "";

        // Color the primary label by node kind. Selected rows always get the
        // bright variant on a non-black background to make the cursor obvious.
        var (fg, bg) = StyleFor(Kind, isSelected);
        var labelStyle = new CL.VtStyle(fg, bg).Apply(mode);
        var rightStyle = new CL.VtStyle(CL.SgrColor.BrightBlack, bg).Apply(mode);

        var label = Label;
        var right = RightTag ?? "";

        // Layout: [label]   [right tag]   padding to width.
        // If they don't both fit, the right tag is dropped first, then the label
        // gets truncated.
        int rightLen = right.Length;
        int gap = right.Length > 0 ? 2 : 0;
        if (rightLen + gap >= width) { right = ""; rightLen = 0; gap = 0; }

        int labelMax = width - rightLen - gap;
        if (label.Length > labelMax)
        {
            label = labelMax > 1 ? label.Substring(0, labelMax - 1) + "…" : label.Substring(0, labelMax);
        }
        int pad = width - label.Length - rightLen - gap;

        var sb = new System.Text.StringBuilder(width + 64);
        sb.Append(labelStyle).Append(label).Append(CL.VtStyle.Reset);
        if (rightLen > 0)
        {
            sb.Append(' ', gap);
            sb.Append(rightStyle).Append(right).Append(CL.VtStyle.Reset);
        }
        if (pad > 0)
        {
            sb.Append(new CL.VtStyle(fg, bg).Apply(mode));
            sb.Append(' ', pad);
            sb.Append(CL.VtStyle.Reset);
        }
        return sb.ToString();
    }

    private static (CL.SgrColor Fg, CL.SgrColor Bg) StyleFor(NodeKind kind, bool isSelected)
    {
        if (isSelected) return (CL.SgrColor.BrightWhite, CL.SgrColor.Blue);
        return kind switch
        {
            NodeKind.Group       => (CL.SgrColor.BrightYellow, CL.SgrColor.Black),
            NodeKind.Production  => (CL.SgrColor.BrightWhite,  CL.SgrColor.Black),
            NodeKind.LhsSymbol   => (CL.SgrColor.BrightCyan,   CL.SgrColor.Black),
            NodeKind.RhsTerminal => (CL.SgrColor.BrightGreen,  CL.SgrColor.Black),
            NodeKind.RhsNonTerm  => (CL.SgrColor.BrightCyan,   CL.SgrColor.Black),
            NodeKind.LexerState  => (CL.SgrColor.BrightYellow, CL.SgrColor.Black),
            NodeKind.LexerRule   => (CL.SgrColor.BrightWhite,  CL.SgrColor.Black),
            NodeKind.Symbol      => (CL.SgrColor.White,        CL.SgrColor.Black),
            _                    => (CL.SgrColor.White,        CL.SgrColor.Black),
        };
    }
}

internal enum NodeKind
{
    Group,
    Production,
    LhsSymbol,
    RhsTerminal,
    RhsNonTerm,
    LexerState,
    LexerRule,
    Symbol,
}
