using LALR.CC.LexicalGrammar;
using LALR.CC.Schema;

namespace LALR.CC.Tui.Model;

/// <summary>
/// Combines the grammar and lexer subtrees under a single synthetic root, so
/// the left-hand TreeView can show both halves of the schema at once. Each
/// half is collapsible independently — the user expands whichever one they
/// want to inspect without losing their place in the other.
/// </summary>
internal static class SchemaRoot
{
    public static Node Build(GrammarSchema schema, Grammar grammar, IReadOnlyDictionary<string, LexRule[]> lexer)
    {
        var grammarSubtree = GrammarRoot.Build(schema, grammar);
        var lexerSubtree = LexerRoot.Build(schema, grammar, lexer);
        var summary = $"{schema.Productions?.Count ?? 0} groups · {schema.Lexer?.Count ?? 0} lex states";
        return new Node("schema", NodeKind.Group, [grammarSubtree, lexerSubtree], summary);
    }
}
