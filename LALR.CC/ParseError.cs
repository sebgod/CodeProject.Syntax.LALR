using System;
using System.Collections.Generic;
using System.Text;
using LALR.CC.LexicalGrammar;

namespace LALR.CC;

/// <summary>
/// Controls how <see cref="Parser.ParseInputAsync"/> reacts when the parse table has
/// no valid action for the current (state, lookahead) cell.
/// </summary>
public enum ParserErrorMode : byte
{
    /// <summary>
    /// Throw <see cref="ParseErrorException"/> at the first parse error. Default —
    /// matches <see cref="GrammarConflictException"/> and <see cref="LexerException"/>.
    /// </summary>
    Throw,

    /// <summary>
    /// Return the offending <see cref="Item"/> from <see cref="Parser.ParseInputAsync"/>
    /// and let the caller inspect <see cref="Item.IsError"/>. Useful for IDE/LSP-style
    /// consumers that want to surface the error and keep working with the partial tree.
    /// </summary>
    Return,
}

/// <summary>
/// Thrown by <see cref="Parser.ParseInputAsync"/> on a parse error when
/// <see cref="ParserErrorMode.Throw"/> is in effect (the default). The exception
/// carries the offending lookahead token, the LALR(1) state, and the set of symbol
/// ids that *would* have been valid at this state — so callers and tooling can
/// produce diagnostics like "expected one of '+', '-', ')', $".
/// </summary>
public sealed class ParseErrorException : InvalidOperationException
{
    /// <summary>The lookahead token whose id had no valid action at <see cref="State"/>.</summary>
    public Item OffendingToken { get; }

    /// <summary>LALR(1) parser state at which the error occurred.</summary>
    public int State { get; }

    /// <summary>
    /// Symbol ids that were valid lookaheads at <see cref="State"/>. -1 represents
    /// end-of-input. Empty if every column at this state is also <see cref="ActionType.Error"/>
    /// (a degenerate case for a malformed grammar).
    /// </summary>
    public IReadOnlyList<int> ExpectedSymbolIds { get; }

    /// <summary>Convenience: the offending token's source position (or Unknown).</summary>
    public SourcePosition Position => OffendingToken?.Position ?? SourcePosition.Unknown;

    public ParseErrorException(Item offendingToken, int state, IReadOnlyList<int> expectedSymbolIds, string message)
        : base(message)
    {
        OffendingToken = offendingToken;
        State = state;
        ExpectedSymbolIds = expectedSymbolIds;
    }

    /// <summary>
    /// Build a human-readable diagnostic naming the offending token and the
    /// expected-symbol set via <paramref name="grammar"/>'s symbol table.
    /// </summary>
    internal static string FormatMessage(Item token, int state, IReadOnlyList<int> expected, Grammar grammar)
    {
        var sb = new StringBuilder("parse error");
        if (token != null && token.Position.IsKnown)
        {
            sb.Append(" at line ").Append(token.Position.Line)
              .Append(", column ").Append(token.Position.Column)
              .Append(" (byte offset ").Append(token.Position.ByteOffset).Append(')');
        }
        sb.Append(": unexpected ");
        AppendSymbol(sb, token?.ID ?? -1, grammar);
        sb.Append(" in state ").Append(state);

        if (expected != null && expected.Count > 0)
        {
            sb.Append("; expected one of: ");
            for (var i = 0; i < expected.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                AppendSymbol(sb, expected[i], grammar);
            }
        }
        return sb.ToString();
    }

    private static void AppendSymbol(StringBuilder sb, int symbolId, Grammar grammar)
    {
        if (symbolId == -1)
        {
            sb.Append('$');
            return;
        }
        if (symbolId < 0 || symbolId >= grammar.SymbolNames.Length)
        {
            sb.Append("(id=").Append(symbolId).Append(')');
            return;
        }
        sb.Append('\'').Append(grammar.SymbolNames[symbolId].Name).Append("' (id=").Append(symbolId).Append(')');
    }
}
