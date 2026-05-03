using System;
using System.Globalization;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

/// <summary>
/// What <see cref="PipeBytesLexer"/> should do when it encounters bytes that
/// don't match any rule in the current lexer state.
/// </summary>
public enum LexerErrorMode : byte
{
    /// <summary>
    /// Throw <see cref="LexerException"/>. Default — matches the fail-fast philosophy
    /// of <see cref="GrammarConflictException"/>: surface bugs at the boundary instead
    /// of silently truncating input.
    /// </summary>
    Throw,

    /// <summary>
    /// Emit one error <see cref="Item"/> at the failing position, then return false
    /// from subsequent <see cref="PipeBytesLexer.MoveNextAsync"/> calls. The error
    /// item has <see cref="Item.IsError"/>==true and <see cref="Item.Content"/>
    /// equal to the hex representation of the offending byte (e.g. <c>"\x7E"</c>).
    /// </summary>
    EmitAndStop,

    /// <summary>
    /// Emit an error <see cref="Item"/> at the failing position, advance the cursor
    /// by **one byte**, and continue scanning. Skipping one byte (not one codepoint)
    /// is deliberate: bare invalid bytes vs. mid-sequence UTF-8 corruption both
    /// surface, and the caller can post-process if they need codepoint-aligned
    /// recovery.
    /// </summary>
    EmitAndSkip,
}

/// <summary>
/// Thrown by <see cref="PipeBytesLexer"/> when input bytes don't match any rule in
/// the current lexer state and <see cref="LexerErrorMode.Throw"/> is in effect.
/// </summary>
public sealed class LexerException : InvalidOperationException
{
    /// <summary>Where the failing scan started (the cursor before any byte was consumed).</summary>
    public SourcePosition Position { get; }

    /// <summary>The byte that the byte-DFA failed to step on, or 0 if the buffer was empty.</summary>
    public byte OffendingByte { get; }

    /// <summary>Name of the lexer state (key into the pattern table) that was active.</summary>
    public string LexerStateName { get; }

    public LexerException(SourcePosition position, byte offendingByte, string lexerStateName)
        : base(BuildMessage(position, offendingByte, lexerStateName))
    {
        Position = position;
        OffendingByte = offendingByte;
        LexerStateName = lexerStateName;
    }

    private static string BuildMessage(SourcePosition position, byte offendingByte, string lexerStateName)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"unrecognized byte 0x{offendingByte:X2} at line {position.Line}, column {position.Column} (byte offset {position.ByteOffset}) in lexer state '{lexerStateName}'");
    }
}
