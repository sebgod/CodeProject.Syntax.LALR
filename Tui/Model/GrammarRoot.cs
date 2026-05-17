using LALR.CC.Schema;

namespace LALR.CC.Tui.Model;

/// <summary>
/// Builds a Node-tree out of a <see cref="GrammarSchema"/> for the TreeView
/// in the grammar view: root → precedence groups → productions → RHS symbols.
/// Symbols flagged as terminals (i.e. they appear in a lexer rule's symbol)
/// get a different colour than non-terminals so the production shape is
/// readable at a glance.
/// </summary>
internal static class GrammarRoot
{
    public static Node Build(GrammarSchema schema, Grammar grammar)
    {
        var terminals = CollectTerminalNames(schema);

        // Synthetic root carries the symbol count as its tag.
        var groups = new List<Node>();
        var productionGroups = schema.Productions ?? [];
        for (int gi = 0; gi < productionGroups.Count; gi++)
        {
            var g = productionGroups[gi];
            var rules = g?.Rules ?? [];
            var ruleNodes = new List<Node>(rules.Count);
            for (int ri = 0; ri < rules.Count; ri++)
            {
                var r = rules[ri];
                ruleNodes.Add(BuildProductionNode(r, terminals));
            }
            var groupLabel = $"group#{gi}  ({rules.Count} rule{(rules.Count == 1 ? "" : "s")})";
            var derivTag = g?.Derivation.ToString();
            groups.Add(new Node(groupLabel, NodeKind.Group, ruleNodes, derivTag));
        }

        var symbolCount = schema.Symbols?.Count ?? 0;
        var startSymbol = symbolCount > 0 ? schema.Symbols![0] : "(empty)";
        var rootLabel = $"grammar  start: {startSymbol}";
        var rootTag = $"{symbolCount} symbols";
        return new Node(rootLabel, NodeKind.Group, groups, rootTag);
    }

    private static Node BuildProductionNode(ProductionSchema rule, HashSet<string> terminals)
    {
        var lhs = rule?.Lhs ?? "?";
        var rhs = rule?.Rhs ?? [];

        // Pretty-print the production on the parent row so the tree reads like
        // the YAML: "E -> E + T". Children carry the individual RHS symbols so
        // the user can drill into each one to see whether it's terminal/not.
        var sb = new System.Text.StringBuilder();
        sb.Append(lhs).Append(" -> ");
        if (rhs.Count == 0) sb.Append("ε");
        else for (int i = 0; i < rhs.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(rhs[i] ?? "?");
        }

        var rhsNodes = new List<Node>(rhs.Count);
        for (int i = 0; i < rhs.Count; i++)
        {
            var sym = rhs[i] ?? "?";
            var kind = terminals.Contains(sym) ? NodeKind.RhsTerminal : NodeKind.RhsNonTerm;
            var tag = terminals.Contains(sym) ? "terminal" : "non-terminal";
            rhsNodes.Add(new Node($"[{i}] {sym}", kind, rightTag: tag));
        }

        var tag2 = rule?.Action;
        return new Node(sb.ToString(), NodeKind.Production, rhsNodes,
            string.IsNullOrEmpty(tag2) ? null : "action: " + tag2);
    }

    private static HashSet<string> CollectTerminalNames(GrammarSchema schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Lexer is null) return set;
        foreach (var rules in schema.Lexer.Values)
        {
            if (rules is null) continue;
            foreach (var r in rules)
            {
                if (!string.IsNullOrEmpty(r?.Symbol)) set.Add(r.Symbol);
            }
        }
        return set;
    }
}
