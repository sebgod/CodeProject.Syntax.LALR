using System;
using System.Globalization;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

/// <summary>
/// Where a token (or a reduced non-terminal) started in the input. <see cref="Line"/>
/// and <see cref="Column"/> are 1-based; <see cref="Line"/>==0 means the position is
/// unknown (reductions on empty input, default-constructed values, <see cref="Item.EOF"/>,
/// or items built without a position by callers that don't supply one).
/// </summary>
/// <remarks>
/// <see cref="Column"/> is **byte-based** within the line, not codepoint-based — the
/// lexer never decodes UTF-8 in the hot path, so converting to a codepoint column
/// would be lossy work for the common case where ASCII columns are sufficient. If
/// you need codepoint columns for diagnostics, decode the matched bytes yourself
/// from the source text.
/// </remarks>
public readonly struct SourcePosition(int line, int column, long byteOffset) : IEquatable<SourcePosition>
{
    /// <summary>1-based line number; 0 if unknown.</summary>
    public int Line { get; } = line;

    /// <summary>1-based byte column within the line; 0 if unknown.</summary>
    public int Column { get; } = column;

    /// <summary>Absolute byte offset from start of input. 0 == first byte; -1 if the position is synthetic.</summary>
    public long ByteOffset { get; } = byteOffset;

    /// <summary>Sentinel for "I don't know where this came from" — same as <c>default</c>.</summary>
    public static readonly SourcePosition Unknown = default;

    /// <summary>True if a real lexer wrote this position; false for default / EOF / synthetic items.</summary>
    public bool IsKnown => Line > 0;

    public bool Equals(SourcePosition other)
        => Line == other.Line && Column == other.Column && ByteOffset == other.ByteOffset;

    public override bool Equals(object obj) => obj is SourcePosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Line, Column, ByteOffset);

    public static bool operator ==(SourcePosition a, SourcePosition b) => a.Equals(b);
    public static bool operator !=(SourcePosition a, SourcePosition b) => !a.Equals(b);

    public override string ToString()
        => IsKnown
            ? string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column}")
            : "?:?";
}
