using LALR.CC;
using LALR.CC.Schema;
using LALR.CC.LexicalGrammar;

namespace LALR.CC.Tui.Model;

/// <summary>
/// Pre-computes the rows for the parse-table view from a built <see cref="Parser"/>.
/// Splitting this out keeps <c>Program.cs</c> focused on UI plumbing and
/// makes the table re-rendering cheap when the user just resizes the pane.
/// </summary>
internal static class ParseTableView
{
    /// <summary>Builds one row per LALR state with pre-formatted cells in symbol-id order.</summary>
    public static List<ParseTableRow> BuildRows(Parser parser, Grammar grammar, GrammarSchema schema)
    {
        var nonTerminals = CollectNonTerminalIds(schema, grammar);
        var actions = parser.ParseTable.Actions;
        var states = actions.GetLength(0);
        var cols = actions.GetLength(1);     // cols = symbols + 1 (col 0 = EOF)
        var rows = new List<ParseTableRow>(states);
        for (var s = 0; s < states; s++)
        {
            var cells = new string[cols];
            var isShift = new bool[cols];
            var isReduce = new bool[cols];
            var isGoto = new bool[cols];
            for (var c = 0; c < cols; c++)
            {
                var a = actions[s, c];
                // c == 0 is EOF (a real terminal); c-1 is the symbol id otherwise.
                var symId = c - 1;
                var nt = symId >= 0 && nonTerminals.Contains(symId);
                cells[c] = ParseTableRow.FormatCell(a, isNonTerminal: nt);
                isShift[c] = a.ActionType == ActionType.Shift && !nt;
                isReduce[c] = a.ActionType == ActionType.Reduce;
                isGoto[c] = a.ActionType == ActionType.Shift && nt;
            }
            rows.Add(new ParseTableRow(s, cells, isShift, isReduce, isGoto));
        }
        return rows;
    }

    /// <summary>Renders the column-header line: state | $ | symbol-name | symbol-name | …</summary>
    public static string BuildHeader(Grammar grammar, int width)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(' ').Append("st".PadLeft(ParseTableRow.StateColWidth - 2)).Append(' ');
        var used = ParseTableRow.StateColWidth;
        // Column 0 = EOF.
        AppendCell(sb, "$", ref used, width);
        if (grammar.SymbolNames is { } names)
        {
            for (var i = 0; i < names.Length && used + ParseTableRow.CellWidth <= width; i++)
            {
                AppendCell(sb, Truncate(names[i].Name, ParseTableRow.CellWidth - 1), ref used, width);
            }
        }
        if (used < width) sb.Append(' ', width - used);
        return sb.ToString();
    }

    private static void AppendCell(System.Text.StringBuilder sb, string text, ref int used, int width)
    {
        if (used + ParseTableRow.CellWidth > width) return;
        sb.Append(text.PadLeft(ParseTableRow.CellWidth - 1)).Append(' ');
        used += ParseTableRow.CellWidth;
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    private static HashSet<int> CollectNonTerminalIds(GrammarSchema schema, Grammar grammar)
    {
        // A symbol is terminal iff it appears as the `symbol:` of any lexer rule.
        // Everything else with a symbol id is a non-terminal (start symbol included).
        var terminalNames = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Lexer is { } lex)
        {
            foreach (var rules in lex.Values)
            {
                if (rules is null) continue;
                foreach (var r in rules)
                {
                    if (!string.IsNullOrEmpty(r?.Symbol)) terminalNames.Add(r.Symbol);
                }
            }
        }
        var result = new HashSet<int>();
        if (grammar.SymbolNames is { } names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (!terminalNames.Contains(names[i].Name)) result.Add(i);
            }
        }
        return result;
    }
}
