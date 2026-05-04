using CL = global::Console.Lib;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Tui.Model;

/// <summary>
/// One row in the Tokens scrollable list. Stores the raw fields plus an
/// <c>IsError</c> flag so error tokens can be highlighted in red without
/// reparsing the content. Layout is fixed-width columns:
///   <c>[idx]  [line:col]  [symbol]  [content]</c>
/// </summary>
internal readonly struct TokenRow : CL.IRowFormatter
{
    public TokenRow(int index, SourcePosition position, string symbolName, string content, bool isError)
    {
        Index = index;
        Position = position;
        SymbolName = symbolName;
        Content = content;
        IsError = isError;
    }

    public int Index { get; }
    public SourcePosition Position { get; }
    public string SymbolName { get; }
    public string Content { get; }
    public bool IsError { get; }

    public string FormatRow(int width, CL.ColorMode colorMode)
    {
        if (width <= 0) return "";
        var fg = IsError ? CL.SgrColor.BrightWhite : CL.SgrColor.White;
        var bg = IsError ? CL.SgrColor.Red          : CL.SgrColor.Black;
        var prefix = new CL.VtStyle(fg, bg).Apply(colorMode);

        var pos = Position.IsKnown ? $"{Position.Line}:{Position.Column}" : "?:?";
        // Escape control chars so a newline in the token's content doesn't
        // break the row layout. Visible width is what matters for padding.
        var content = Escape(Content);
        var raw = $" {Index,4}  {pos,9}  {SymbolName,-20}  {content}";
        if (raw.Length > width)
        {
            raw = width > 1 ? raw.Substring(0, width - 1) + "…" : raw.Substring(0, width);
        }
        else if (raw.Length < width)
        {
            raw = raw.PadRight(width);
        }
        return prefix + raw + CL.VtStyle.Reset;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append('?');
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
