using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;

namespace CodeProject.Syntax.LALR.Tui.Model;

/// <summary>
/// Builds a Node-tree out of a <see cref="GrammarSchema"/>'s lexer table for
/// the lexer view: root → state name → rule. Rules are read straight from the
/// YAML schema (rather than the compiled <see cref="LexRule"/>) because the
/// schema preserves the human-readable <c>match:</c> regex string; the compiled
/// rule only carries the IRx AST.
/// </summary>
internal static class LexerRoot
{
    public static Node Build(GrammarSchema schema, Grammar grammar, IReadOnlyDictionary<string, LexRule[]> compiledLexer)
    {
        var stateNodes = new List<Node>();
        var lexer = schema.Lexer;
        if (lexer is { Count: > 0 })
        {
            // Stable ordering: root first, then everything else alphabetically.
            // The runtime treats "root" as the entry state, so it deserves the
            // top of the tree even if the YAML happens to define it later.
            var keys = new List<string>(lexer.Keys);
            keys.Sort((a, b) =>
            {
                if (a == PipeBytesLexer.RootState) return -1;
                if (b == PipeBytesLexer.RootState) return  1;
                return string.CompareOrdinal(a, b);
            });
            foreach (var stateName in keys)
            {
                var rules = lexer[stateName] ?? [];
                var ruleNodes = new List<Node>(rules.Count);
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    ruleNodes.Add(BuildRuleNode(i, r));
                }
                stateNodes.Add(new Node($"state: {stateName}", NodeKind.LexerState, ruleNodes,
                    rightTag: $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")}"));
            }
        }

        return new Node("lexer", NodeKind.LexerState, stateNodes,
            rightTag: $"{stateNodes.Count} state{(stateNodes.Count == 1 ? "" : "s")}");
    }

    private static Node BuildRuleNode(int index, LexRuleSchema rule)
    {
        if (rule == null)
        {
            return new Node($"[{index}] (null rule)", NodeKind.LexerRule);
        }
        var sb = new System.Text.StringBuilder();
        sb.Append('[').Append(index).Append("] ").Append(rule.Symbol ?? "?");
        sb.Append("  /").Append(rule.Match ?? "").Append('/');

        // Mutually-exclusive instruction tag.
        string? tag = null;
        if (!string.IsNullOrEmpty(rule.Push))    tag = "push: " + rule.Push;
        else if (rule.Pop)                       tag = "pop";
        else if (!string.IsNullOrEmpty(rule.Action)) tag = "action: " + rule.Action;
        return new Node(sb.ToString(), NodeKind.LexerRule, rightTag: tag);
    }
}
