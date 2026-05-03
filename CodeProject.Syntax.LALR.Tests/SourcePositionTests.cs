using System.Collections.Generic;
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
