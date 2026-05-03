using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

public class SourcePositionTests
{
    private const int Number = 10;
    private const int Plus = 11;
    private const int Whitespace = 30;
    private const int Newline = 31;

    private static IReadOnlyDictionary<string, LexRule[]> ArithTable() => new Dictionary<string, LexRule[]>
    {
        { PipeBytesLexer.RootState, [
            new(Number, new GroupRx(Multiplicity.OneOrMore, new CharClassRx(true, [new CharRangeRx('0', '9')]))),
            new(Plus, new CharRx('+')),
            new(Whitespace, new GroupRx(Multiplicity.OneOrMore,
                new CharClassRx(true, ' ', '\t')),
                PipeBytesLexer.Ignore),
            new(Newline, new CharRx('\n'), PipeBytesLexer.Ignore),
        ] },
    };

    private static async Task<List<Item>> CollectAsync(PipeBytesLexer lexer)
    {
        var tokens = new List<Item>();
        while (await lexer.MoveNextAsync())
        {
            tokens.Add(await lexer.CurrentAsync());
        }
        return tokens;
    }

    [Fact]
    public void DefaultSourcePosition_IsUnknown()
    {
        var p = default(SourcePosition);
        Assert.False(p.IsKnown);
        Assert.Equal(0, p.Line);
        Assert.Equal(0, p.Column);
        Assert.Equal(0L, p.ByteOffset);
        Assert.Equal("?:?", p.ToString());
    }

    [Fact]
    public void KnownSourcePosition_RoundTrips()
    {
        var p = new SourcePosition(line: 3, column: 7, byteOffset: 42);
        Assert.True(p.IsKnown);
        Assert.Equal(3, p.Line);
        Assert.Equal(7, p.Column);
        Assert.Equal(42L, p.ByteOffset);
        Assert.Equal("3:7", p.ToString());
    }

    [Fact]
    public void ItemEof_HasUnknownPosition()
    {
        Assert.False(Item.EOF.Position.IsKnown);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetCodepointColumn — byte → codepoint conversion for diagnostics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCodepointColumn_AsciiSource_MatchesByteColumn()
    {
        // Pure ASCII: codepoint column == byte column for every position. "abc"
        // line, position at 'b' is byte column 2 == codepoint column 2.
        var bytes = Encoding.UTF8.GetBytes("abc");
        var pos = new SourcePosition(line: 1, column: 2, byteOffset: 1);
        Assert.Equal(2, pos.GetCodepointColumn(bytes));
    }

    [Fact]
    public void GetCodepointColumn_MultiByteSource_ReturnsCodepointIndex()
    {
        // "éb" — é is U+00E9, encoded as 0xC3 0xA9 (2 bytes). The 'b' starts at
        // byte 2 (column 3 byte-based) but is the 2nd codepoint of the line.
        var bytes = Encoding.UTF8.GetBytes("éb");
        Assert.Equal(3, bytes.Length);

        var posOfB = new SourcePosition(line: 1, column: 3, byteOffset: 2);
        Assert.Equal(2, posOfB.GetCodepointColumn(bytes));
    }

    [Fact]
    public void GetCodepointColumn_FourByteCodepoint_StillCountsAsOne()
    {
        // U+1F600 (😀) encodes to 4 bytes (F0 9F 98 80). A 'b' immediately
        // after sits at byte column 5 / codepoint column 2.
        var bytes = Encoding.UTF8.GetBytes("😀b");
        Assert.Equal(5, bytes.Length);

        var posOfB = new SourcePosition(line: 1, column: 5, byteOffset: 4);
        Assert.Equal(2, posOfB.GetCodepointColumn(bytes));
    }

    [Fact]
    public void GetCodepointColumn_StartOfLine_IsOne()
    {
        var bytes = Encoding.UTF8.GetBytes("any source");
        var pos = new SourcePosition(line: 1, column: 1, byteOffset: 0);
        Assert.Equal(1, pos.GetCodepointColumn(bytes));
    }

    [Fact]
    public void GetCodepointColumn_OnSecondLine_StartsCountFromLineStart()
    {
        // Two lines separated by '\n'. Position is on line 2, byte column 3.
        // The line-start computation lineStart = ByteOffset - (Column - 1) means
        // we don't actually scan back through line 1 — just from the start of
        // line 2 forward.
        var source = "abc\ndéf";
        var bytes = Encoding.UTF8.GetBytes(source);
        // 'é' is at byte index 5 (after "abc\nd"); line 2, byte column 2.
        var posOfE = new SourcePosition(line: 2, column: 2, byteOffset: 5);
        Assert.Equal(2, posOfE.GetCodepointColumn(bytes));

        // 'f' is at byte index 7 (after "abc\ndé" where é is 2 bytes); line 2,
        // byte column 4 (1-based: d=1, é-byte0=2, é-byte1=3, f=4) but
        // codepoint column 3 (d=1, é=2, f=3).
        var posOfF = new SourcePosition(line: 2, column: 4, byteOffset: 7);
        Assert.Equal(3, posOfF.GetCodepointColumn(bytes));
    }

    [Theory]
    [InlineData(0, 0, 0)]    // Line=0 → unknown
    [InlineData(1, 0, 0)]    // Column=0 → unknown
    public void GetCodepointColumn_UnknownPositions_ReturnZero(int line, int column, int byteOffset)
    {
        var pos = new SourcePosition(line, column, byteOffset);
        Assert.Equal(0, pos.GetCodepointColumn(Array.Empty<byte>()));
    }

    [Fact]
    public void GetCodepointColumn_NegativeByteOffset_ReturnsZero()
    {
        // ByteOffset < 0 marks a synthetic item (e.g. EOF, hand-constructed). No
        // sensible codepoint column to compute.
        var pos = new SourcePosition(line: 1, column: 1, byteOffset: -1);
        Assert.Equal(0, pos.GetCodepointColumn(Array.Empty<byte>()));
    }

    [Fact]
    public void GetCodepointColumn_SourceTooShort_Throws()
    {
        // Defensive: if a caller passes a clipped source, error out clearly
        // rather than IndexOutOfRange-ing on the byte access.
        var pos = new SourcePosition(line: 1, column: 5, byteOffset: 100);
        Assert.Throws<ArgumentException>(() => pos.GetCodepointColumn(new byte[10]));
    }

    [Fact]
    public void ItemConstructedWithoutPosition_DefaultsToUnknown()
    {
        var item = new Item(7, "x");
        Assert.False(item.Position.IsKnown);
    }

    [Fact]
    public async Task LexerEmitsTokens_WithLineColumnAndOffset()
    {
        using var lexer = PipeBytesLexer.FromString("12+34", ArithTable(), cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await CollectAsync(lexer);

        Assert.Equal(3, tokens.Count);

        // "12" starts at line 1, col 1, offset 0
        Assert.Equal(new SourcePosition(1, 1, 0), tokens[0].Position);
        // "+" starts at line 1, col 3, offset 2
        Assert.Equal(new SourcePosition(1, 3, 2), tokens[1].Position);
        // "34" starts at line 1, col 4, offset 3
        Assert.Equal(new SourcePosition(1, 4, 3), tokens[2].Position);
    }

    [Fact]
    public async Task IgnoredWhitespace_AdvancesPosition()
    {
        using var lexer = PipeBytesLexer.FromString("12   +   34", ArithTable(), cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await CollectAsync(lexer);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(new SourcePosition(1, 1, 0), tokens[0].Position);
        // "+" is at col 6 (after "12" + 3 spaces)
        Assert.Equal(new SourcePosition(1, 6, 5), tokens[1].Position);
        // "34" is at col 10 (after "+" + 3 more spaces)
        Assert.Equal(new SourcePosition(1, 10, 9), tokens[2].Position);
    }

    [Fact]
    public async Task NewlinesIncrementLine_AndResetColumn()
    {
        using var lexer = PipeBytesLexer.FromString("12\n+\n34", ArithTable(), cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await CollectAsync(lexer);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(new SourcePosition(1, 1, 0), tokens[0].Position);
        // "+" sits on line 2 at column 1, offset 3 (past "12\n")
        Assert.Equal(new SourcePosition(2, 1, 3), tokens[1].Position);
        // "34" on line 3 at column 1, offset 5 (past "12\n+\n")
        Assert.Equal(new SourcePosition(3, 1, 5), tokens[2].Position);
    }

    [Fact]
    public async Task MultipleTokensOnLine_TrackColumnsCorrectly()
    {
        using var lexer = PipeBytesLexer.FromString("1+2\n3+4", ArithTable(), cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await CollectAsync(lexer);

        Assert.Equal(6, tokens.Count);
        Assert.Equal(new SourcePosition(1, 1, 0), tokens[0].Position); // "1"
        Assert.Equal(new SourcePosition(1, 2, 1), tokens[1].Position); // "+"
        Assert.Equal(new SourcePosition(1, 3, 2), tokens[2].Position); // "2"
        Assert.Equal(new SourcePosition(2, 1, 4), tokens[3].Position); // "3"
        Assert.Equal(new SourcePosition(2, 2, 5), tokens[4].Position); // "+"
        Assert.Equal(new SourcePosition(2, 3, 6), tokens[5].Position); // "4"
    }

    [Fact]
    public async Task MultiByteUtf8_AdvancesByByteCount()
    {
        // Pattern accepts "€" (3 bytes) once, then a digit. Verify the digit's column
        // is past the multi-byte sequence's bytes, not past 1 codepoint.
        var rules = new LexRule[]
        {
            new(1, new CharRx(0x20AC)),   // €
            new(2, new CharClassRx(true, [new CharRangeRx('0', '9')])),
        };
        var table = new Dictionary<string, LexRule[]>
        {
            { PipeBytesLexer.RootState, rules },
        };

        using var lexer = PipeBytesLexer.FromString("€7", table, cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await CollectAsync(lexer);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(new SourcePosition(1, 1, 0), tokens[0].Position); // €
        // After consuming the 3 bytes of €, the next token sits at byte column 4.
        Assert.Equal(new SourcePosition(1, 4, 3), tokens[1].Position);
    }

    [Fact]
    public async Task ParserReduction_InheritsFirstChildPosition()
    {
        // Tiny grammar: S' -> N (where N is just a Number token).
        // After reducing, the resulting Item should have the Number's position.
        var grammar = new Grammar(
            ["S'", "N"],
            new PrecedenceGroup(Derivation.None,
                new Production(0, 1)));
        var parser = new Parser(grammar);

        var rules = new LexRule[]
        {
            new(1, new GroupRx(Multiplicity.OneOrMore, new CharClassRx(true, [new CharRangeRx('0', '9')]))),
        };
        var table = new Dictionary<string, LexRule[]>
        {
            { PipeBytesLexer.RootState, rules },
        };

        using var lexer = PipeBytesLexer.FromString("   42", table, cancellationToken: TestContext.Current.CancellationToken);
        // wrap with LA iterator (parser needs it)
        using var la = new AsyncLATokenIterator(lexer);

        // Parser will fail on "   42" because we have no whitespace rule — let's use only "42".
        using var lexer2 = PipeBytesLexer.FromString("42", table, cancellationToken: TestContext.Current.CancellationToken);
        using var la2 = new AsyncLATokenIterator(lexer2);
        var debug = new Debug(parser, null, null);
        var result = await parser.ParseInputAsync(la2, debug,
            cancellationToken: TestContext.Current.CancellationToken);

        // Reduction inherits from the leftmost (and only) child — the "42" token at (1,1,0).
        Assert.Equal(new SourcePosition(1, 1, 0), result.Position);
    }
}
