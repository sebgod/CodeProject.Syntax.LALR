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

    /// <summary>
    /// Compute the 1-based <em>codepoint</em> column for this position given the
    /// raw UTF-8 source bytes the lexer consumed. <see cref="Column"/> is byte-based
    /// — for diagnostics in non-ASCII grammars (e.g. column 5 when an emoji is
    /// to the left of the token), this gives the human-friendly figure.
    /// </summary>
    /// <param name="source">
    /// The same UTF-8 byte sequence the lexer parsed. The method scans bytes from
    /// the start of <see cref="Line"/> to <see cref="ByteOffset"/>, counting
    /// codepoint starters (any byte whose top two bits aren't <c>10</c>); UTF-8
    /// continuation bytes don't contribute. ASCII input gives the same result as
    /// <see cref="Column"/>.
    /// </param>
    /// <returns>
    /// The 1-based codepoint column, or 0 when this position is <see cref="Unknown"/>
    /// or carries a synthetic <see cref="ByteOffset"/>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="source"/> is shorter than this position
    /// requires (i.e. the line start or the position itself falls outside the
    /// span). A defensive check — the lexer never produces a position past EOF,
    /// but a caller may pass a clipped source by accident.
    /// </exception>
    public int GetCodepointColumn(ReadOnlySpan<byte> source)
    {
        if (!IsKnown || ByteOffset < 0 || Column < 1)
        {
            return 0;
        }

        var lineStart = ByteOffset - (Column - 1);
        if (lineStart < 0 || ByteOffset > source.Length)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture,
                    $"source is shorter ({source.Length} bytes) than this position requires (line starts at byte {lineStart}, position at {ByteOffset})"),
                nameof(source));
        }

        // 1-based: a position at the very start of the line is column 1; each
        // codepoint starter we cross before reaching ByteOffset bumps the column.
        var col = 1;
        for (var i = lineStart; i < ByteOffset; i++)
        {
            if ((source[(int)i] & 0xC0) != 0x80)
            {
                col++;
            }
        }
        return col;
    }
}
