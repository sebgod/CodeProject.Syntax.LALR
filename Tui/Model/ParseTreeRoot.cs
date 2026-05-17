using LALR.CC.LexicalGrammar;

namespace LALR.CC.Tui.Model;

/// <summary>
/// Adapts a parser <see cref="Item"/> tree (terminal tokens at leaves, <see cref="Reduction"/>s
/// at branches) into the <see cref="Node"/> tree the TreeView widget renders. Mirrors the
/// shape of <see cref="GrammarRoot"/> / <see cref="LexerRoot"/>: pure data, no rendering.
/// </summary>
internal static class ParseTreeRoot
{
    public static Node Build(Item? root, Grammar grammar, string? parseError = null)
    {
        if (root is null)
        {
            var label = parseError is null ? "(no parse — empty input)" : "(parse failed)";
            return new Node(label, NodeKind.Production, rightTag: parseError);
        }
        var tag = parseError ?? (root.IsError ? "error" : null);
        return BuildNode(root, grammar, tag);
    }

    private static Node BuildNode(Item item, Grammar grammar, string? rightTagOverride = null)
    {
        var symbolName = SymbolName(item.ID, grammar);

        switch (item.ContentType)
        {
            case ContentType.Reduction:
            {
                var children = item.Reduction.Children;
                var childNodes = new List<Node>(children.Count);
                foreach (var c in children) childNodes.Add(BuildNode(c, grammar));
                var label = symbolName;
                var tag = rightTagOverride ?? (item.IsError ? "error" : $"{children.Count} child{(children.Count == 1 ? "" : "ren")}");
                return new Node(label, NodeKind.LhsSymbol, childNodes, tag);
            }
            case ContentType.Nested:
                // Should be rare; collapse into the inner item but keep the outer label.
                return BuildNode(item.Nested, grammar, rightTagOverride);
            default:
            {
                // Terminal: show position + content snippet.
                var content = item.Content?.ToString() ?? "";
                var snippet = Truncate(content, 32);
                var pos = item.Position.IsKnown ? $"{item.Position.Line}:{item.Position.Column}" : "?:?";
                var kind = item.IsError ? NodeKind.RhsTerminal : NodeKind.RhsTerminal;
                var label = string.IsNullOrEmpty(snippet)
                    ? symbolName
                    : $"{symbolName}  '{snippet}'";
                var tag = rightTagOverride ?? pos + (item.IsError ? "  ERR" : "");
                return new Node(label, kind, rightTag: tag);
            }
        }
    }

    private static string SymbolName(int id, Grammar grammar)
    {
        if (id == -1) return "$";
        if (grammar.SymbolNames is { } sn && id >= 0 && id < sn.Length) return sn[id].Name;
        return $"#{id}";
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length == 0) return s;
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, max) + 4);
        for (var i = 0; i < s.Length && sb.Length < max; i++)
        {
            var c = s[i];
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
        if (sb.Length >= max) sb.Append('…');
        return sb.ToString();
    }
}
