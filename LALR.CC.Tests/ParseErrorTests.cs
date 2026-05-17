using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LALR.CC;
using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class ParseErrorTests
{
    // Tiny grammar:
    //   S' -> N         (id 0)
    //   N  -> "n"       (id 1)
    // Symbols: 0=S', 1=N, 2=n
    private const int Sp = 0;
    private const int Nt = 1;
    private const int N_lit = 2;

    private static Grammar SimpleGrammar() => new(
        ["S'", "N", "n"],
        new PrecedenceGroup(Derivation.None,
            new Production(Sp, Nt),
            new Production(Nt, N_lit)));

    private static IReadOnlyDictionary<string, LexRule[]> SimpleLexer() => new Dictionary<string, LexRule[]>
    {
        { PipeBytesLexer.RootState, [
            new(N_lit, new CharRx('n')),
            new(99, new CharRx('x')),  // 99 isn't in the grammar's symbol table → unexpected token
        ] },
    };

    [Fact]
    public async Task ValidInput_ReturnsParseTreeUnderEitherMode()
    {
        var parser = new Parser(SimpleGrammar());
        var debug = new Debug(parser, null, null);

        // Throw mode (default) — happy path doesn't throw.
        using var lexer1 = PipeBytesLexer.FromString("n", SimpleLexer(), cancellationToken: TestContext.Current.CancellationToken);
        using var la1 = new AsyncLATokenIterator(lexer1);
        var r1 = await parser.ParseInputAsync(la1, debug,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(r1.IsError);

        // Return mode — happy path also doesn't throw and returns a clean tree.
        using var lexer2 = PipeBytesLexer.FromString("n", SimpleLexer(), cancellationToken: TestContext.Current.CancellationToken);
        using var la2 = new AsyncLATokenIterator(lexer2);
        var r2 = await parser.ParseInputAsync(la2, debug,
            errorMode: ParserErrorMode.Return,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(r2.IsError);
    }

    [Fact]
    public async Task BadInput_DefaultThrows_WithExpectedSet()
    {
        var parser = new Parser(SimpleGrammar());
        var debug = new Debug(parser, null, null);

        // Input "x" lexes to a token with id=99 (not in grammar's symbol table). The
        // parser's table lookup at column 100 will fall in range (table is sized for the
        // full symbol set), but no Shift/Reduce action will be defined for state 0 on
        // that lookahead → ActionType.Error → throw.
        // Actually, with a 3-symbol grammar the table has 4 columns (0=EOF, 1..3 for
        // ids 0..2), so id=99 lookup is out of range. To stay in range we use a real
        // token id that's just *unexpected* at state 0 — there's no such id in this
        // tiny grammar. Use a different grammar variant that has more symbols.

        // Switch to a 4-symbol grammar where we can produce a wrong-but-in-range id.
        var biggerGrammar = new Grammar(
            ["S'", "N", "n", "m"],
            new PrecedenceGroup(Derivation.None,
                new Production(Sp, Nt),
                new Production(Nt, N_lit)));   // 'm' is reachable in the table but never the right lookahead at state 0
        var biggerParser = new Parser(biggerGrammar);
        var biggerDebug = new Debug(biggerParser, null, null);

        var biggerLexer = new Dictionary<string, LexRule[]>
        {
            { PipeBytesLexer.RootState, [
                new(N_lit, new CharRx('n')),
                new(3,    new CharRx('m')),   // id=3 is in range but unexpected as start
            ] },
        };

        using var lexer = PipeBytesLexer.FromString("m", biggerLexer, cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);

        var ex = await Assert.ThrowsAsync<ParseErrorException>(async () =>
            await biggerParser.ParseInputAsync(la, biggerDebug,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(ex.OffendingToken);
        Assert.Equal(3, ex.OffendingToken.ID);
        Assert.True(ex.OffendingToken.Position.IsKnown);
        // Expected set at state 0 should contain 'n' (id=2), not 'm' (id=3).
        Assert.Contains(N_lit, ex.ExpectedSymbolIds);
        Assert.DoesNotContain(3, ex.ExpectedSymbolIds);

        // Message should include the unexpected symbol's name and at least one expected.
        Assert.Contains("'m'", ex.Message);
        Assert.Contains("'n'", ex.Message);
    }

    [Fact]
    public async Task BadInput_ReturnMode_ReturnsErrorItem()
    {
        var biggerGrammar = new Grammar(
            ["S'", "N", "n", "m"],
            new PrecedenceGroup(Derivation.None,
                new Production(Sp, Nt),
                new Production(Nt, N_lit)));
        var parser = new Parser(biggerGrammar);
        var debug = new Debug(parser, null, null);

        var biggerLexer = new Dictionary<string, LexRule[]>
        {
            { PipeBytesLexer.RootState, [
                new(N_lit, new CharRx('n')),
                new(3,    new CharRx('m')),
            ] },
        };

        using var lexer = PipeBytesLexer.FromString("m", biggerLexer, cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);

        var result = await parser.ParseInputAsync(la, debug,
            errorMode: ParserErrorMode.Return,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(3, result.ID); // the offending token id
    }

    [Fact]
    public async Task PreCancelled_ThrowsBeforeParsing()
    {
        var parser = new Parser(SimpleGrammar());
        var debug = new Debug(parser, null, null);

        using var lexer = PipeBytesLexer.FromString("n", SimpleLexer(), cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
            await parser.ParseInputAsync(la, debug, cancellationToken: cts.Token));
    }
}
