namespace LALR.CC.LexicalGrammar;

/// <summary>
/// How the lexer reports <see cref="SourcePosition.Column"/> as it advances
/// through the input. Default is <see cref="Codepoints"/> — what users
/// generally expect for diagnostic messages on non-ASCII grammars (one
/// emoji is one column, not four). <see cref="Bytes"/> is preserved as an
/// opt-in for callers that want literal UTF-8 byte offsets, e.g. to feed
/// back into a byte-oriented editor index.
/// </summary>
/// <remarks>
/// Both modes are O(1) per byte in the inner lexer loop. Codepoint mode
/// just skips UTF-8 continuation bytes (<c>0b10xxxxxx</c>) when bumping
/// the column — no decoding, no surrogate-pair handling, no allocation.
/// Lines are bumped on <c>\n</c> in either mode; only the column counter
/// differs.
/// </remarks>
public enum ColumnMode
{
    /// <summary>
    /// Each codepoint contributes 1 to the column. The default — diagnostics
    /// using <see cref="SourcePosition.Column"/> directly are correct on
    /// non-ASCII input without further decoding.
    /// </summary>
    Codepoints = 0,

    /// <summary>
    /// Each UTF-8 byte contributes 1 to the column. Useful when feeding
    /// positions back into a byte-oriented index (a region in a file by byte
    /// offset, a Pipe-aware diagnostic surface, etc).
    /// </summary>
    Bytes = 1,
}
