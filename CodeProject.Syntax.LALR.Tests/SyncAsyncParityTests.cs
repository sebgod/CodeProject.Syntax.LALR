using System.Collections.Generic;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

/// <summary>
/// Sync ↔ async path parity. The two parse loops are intentionally code-
/// duplicated (extracting a shared body would force ValueTask boxing or
/// generic gymnastics that erode the savings). These tests are the safety
/// net that keeps them in lockstep — same grammar, same input, both paths,
/// assert byte-identical Item trees.
/// </summary>
public class SyncAsyncParityTests
{
    // Symbol ids match the grammar (0=S', 1=S, 2=+, 3=n). Whitespace gets a
    // distinct id and is dropped via the #ignore instruction so it never
    // reaches the parser.
    private const int Plus = 2;
    private const int Number = 3;
    private const int Whitespace = 4;

    private static readonly LexRule[] _arithRules =
    [
        new(Number, new GroupRx(Multiplicity.OneOrMore, new CharClassRx(true, [new CharRangeRx('0', '9')]))),
        new(Plus, new CharRx('+')),
        new(Whitespace, new GroupRx(Multiplicity.OneOrMore,
            new CharClassRx(true, [(CharRx)' ', (CharRx)'\t', (CharRx)'\n'])),
            PipeBytesLexer.Ignore),
    ];

    private static IReadOnlyDictionary<string, LexRule[]> Table() => new Dictionary<string, LexRule[]>
    {
        { PipeBytesLexer.RootState, _arithRules },
    };

    /// <summary>
    /// Trivial grammar: S -> n | S + S. Parsing "1 + 2 + 3" goes through
    /// reduce, shift, lookahead a few times each — touches every branch of
    /// the parse loop.
    /// </summary>
    private static Grammar BuildGrammar() => new(
        new[] { "S'", "S", "+", "n", "WS" },
        new PrecedenceGroup(Derivation.None,
            new Production(0, 1)),
        new PrecedenceGroup(Derivation.LeftMost,
            new Production(1, 3),
            new Production(1, 1, 2, 1)));

    [Theory]
    [InlineData("1")]
    [InlineData("12")]
    [InlineData("1 + 2")]
    [InlineData("1 + 2 + 3 + 4")]
    [InlineData("100 + 200")]
    public async Task SameInput_SyncAndAsyncProduceEqualResult(string input)
    {
        var grammar = BuildGrammar();
        var parser = new Parser(grammar);

        // Async path.
        using var asyncLexer = PipeBytesLexer.FromString(input, Table(),
            cancellationToken: TestContext.Current.CancellationToken);
        using var asyncTokens = new AsyncLATokenIterator(asyncLexer);
        var asyncResult = await parser.ParseInputAsync(asyncTokens,
            cancellationToken: TestContext.Current.CancellationToken);

        // Sync path. Parser instance reuse is fine — the parse loop carries no
        // state across calls.
        using var syncLexer = BytesLexer.FromString(input, Table());
        using var syncTokens = new SyncLATokenIterator(syncLexer);
        var syncResult = parser.ParseInput(syncTokens,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(asyncResult.IsError);
        Assert.False(syncResult.IsError);
        Assert.Equal(asyncResult.ID, syncResult.ID);
        // Reduction trees should be structurally identical. ToString is a
        // sufficient observation point — it walks the tree and labels every
        // node with its ID + content.
        Assert.Equal(asyncResult.ToString(), syncResult.ToString());
    }
}
