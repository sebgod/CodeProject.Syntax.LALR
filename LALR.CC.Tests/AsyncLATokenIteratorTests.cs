using System.Collections.Generic;
using System.Threading.Tasks;
using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class AsyncLATokenIteratorTests
{
    public static IEnumerable<object[]> TokenSource() =>
    [
        [new PrintableList<Item>
        {
            new(6, 2),
            new(2, "+"),
            new(7, "("),
            new(6, 0),
            new(4, "*"),
            new(6, int.MaxValue),
            new(8, ")"),
        }],
        [new PrintableList<Item>()],
    ];

    [Theory]
    [MemberData(nameof(TokenSource))]
    public async Task TestTokenIteration(IList<Item> expectedTokens)
    {
        using var tokenIterator = new AsyncLATokenIterator(expectedTokens.AsAsync());
        for (var i = 0; i < expectedTokens.Count; i++)
        {
            Assert.True(await tokenIterator.MoveNextAsync(), $"Move next i={i}");

            var current = await tokenIterator.CurrentAsync();
            Assert.True(current == expectedTokens[i],
                $"Current i={i} expected={expectedTokens[i]} is={current}");

            var la = await tokenIterator.LookAheadAsync();
            var expectedLA = i + 1 < expectedTokens.Count ? expectedTokens[i + 1] : Item.EOF;
            Assert.True(la == expectedLA,
                $"Lookahead i={i} expected={expectedLA} is={la}");
        }
    }

    [Theory]
    [MemberData(nameof(TokenSource))]
    // Test optional iterator resetting functionality.
    public async Task TestSupportResetting(IList<Item> expectedTokens)
    {
        using var tokenIterator = new AsyncLATokenIterator(expectedTokens.AsAsync());
        if (tokenIterator.SupportsResetting)
        {
            var isEmpty = true;
            while (await tokenIterator.MoveNextAsync())
            {
                isEmpty = false;
            }
            tokenIterator.Reset();
            Assert.Equal(isEmpty, await tokenIterator.MoveNextAsync());
        }
        else
        {
            Assert.Throws<System.InvalidOperationException>(tokenIterator.Reset);
        }
    }
}
