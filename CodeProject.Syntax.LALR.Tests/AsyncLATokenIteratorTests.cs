using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.Tests
{
    public class AsyncLATokenIteratorTests
    {
        [Test, TestCaseSource("_tokenSource")]
        public async Task TestTokenIteration(IList<Item> expectedTokens)
        {
            using (var tokenIterator = new AsyncLATokenIterator(expectedTokens.AsAsync()))
            {
                for (var i = 0; i < expectedTokens.Count; i++)
                {
                    Assert.That(await tokenIterator.MoveNextAsync(), Is.True, "Move next i={0}", i);
                    var current = await tokenIterator.CurrentAsync();
                    Assert.That(current, Is.EqualTo(expectedTokens[i]), "Current i={0} expected={1} is={2}",
                        i, expectedTokens[i], current);

                    var la = await tokenIterator.LookAheadAsync();
                    var expectedLA = i + 1 < expectedTokens.Count ? expectedTokens[i + 1] : Item.EOF;
                    Assert.That(la, Is.EqualTo(expectedLA), "Lookahead i={0} expected={1} is={2}",
                        i, expectedLA, la);
                }
            }
        }

        [TestCaseSource("_tokenSource")]
        [Test(Description = "Test optional iterator resetting functionality")]
        public async Task TestSupportResetting(IList<Item> expectedTokens)
        {
            using (var tokenIterator = new AsyncLATokenIterator(expectedTokens.AsAsync()))
            {
                if (tokenIterator.SupportsResetting)
                {
                    var isEmpty = true;
                    while (await tokenIterator.MoveNextAsync())
                    {
                        isEmpty = false;
                    }
                    tokenIterator.Reset();
                    Assert.That(tokenIterator.MoveNextAsync(), Is.EqualTo(isEmpty), "Has element after resetting is not empty");
                }
                else
                {
                    Assert.That(tokenIterator.Reset, Throws.InvalidOperationException);
                }
            }
        }

        private readonly object[] _tokenSource = new object[]
        {
            new TestCaseData(new PrintableList<Item>
            {
                new Item(6, 2),
                new Item(2, "+"),
                new Item(7, "("),
                new Item(6, 0),
                new Item(4, "*"),
                new Item(6, int.MaxValue),
                new Item(8, ")")
            }),
            new TestCaseData(new PrintableList<Item>())
        };
    }
}