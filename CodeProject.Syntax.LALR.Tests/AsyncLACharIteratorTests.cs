using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.Tests
{
    public class AsyncLACharIteratorTests
    {
        private const int capacity = 1024 * 10;

        [TestCase("1+2*34+5/6\r\n")]
        [TestCase("\r\n")]
        [TestCase("\r\r")]
        [TestCase("\n\r")]
        [TestCase("𝒜\r\n𝓑")]
        [TestCase("")]
        public async Task TestNormalisationAsync(string input)
        {
            // var expectedCount = input.Count(p => !char.IsSurrogate(p) && p != '\r');
            using (var charReader = new AsyncLACharIterator(new StringReader(input), capacity))
            {
                var i = 0;
                while (i < input.Length)
                {
                    int expectedCurrent;
                    int expectedLA;
                    input.CurrentAndLA(ref i, out expectedCurrent, out expectedLA);
                    Assert.That(await charReader.MoveNextAsync(),
                        Is.EqualTo(expectedCurrent != AsyncLACharIterator.EOF),
                        "Move next i={0}", i);

                    var current = await charReader.CurrentAsync();
                    Assert.That(current, Is.EqualTo(expectedCurrent), "Current codepoint i={0}", i);

                    var la = await charReader.LookAheadAsync();

                    Assert.That(la, Is.EqualTo(expectedLA), "lookahead codepoint i={0}", i);
                }
            }
        }

        [TestCase('\r', capacity)]
        [TestCase('\n', capacity)]
        [TestCase('a', capacity)]
        [TestCase('\r', capacity + 1)]
        [TestCase('b', capacity + 1)]
        public async Task TestLargeStreamStringAsync(char input, int count)
        {
            await TestNormalisationAsync(new string(input, count));
        }

        [TestCase('a', capacity)]
        [TestCase('a', capacity + 1)]
        [TestCase(0x1D400, capacity)]
        [TestCase(0x1D400, capacity + 1)]
        public async Task TestLargeSequenceStringAsync(int utf8Char, int count)
        {
            var chars = new StringBuilder(count);
            for (var i = 0; i < count; i++)
            {
                var c = utf8Char + i;
                if (c >= char.MaxValue || !char.IsSurrogate((char)c))
                {
                    chars.Append(char.ConvertFromUtf32(Math.Min(0x10ffff, c)));
                }
            }
            await TestNormalisationAsync(chars.ToString());
        }

        [Test]
        public void TestUninitialised()
        {
            using (var charReader = new AsyncLACharIterator(new StringReader(""), capacity))
            {
                Assert.That(async () => await charReader.CurrentAsync(), Throws.InvalidOperationException);
            }
        }

        [TestCase("12")]
        [TestCase("")]
        public async Task TestSupportResetting(string input)
        {
            using (var charReader = new AsyncLACharIterator(new StringReader(input), capacity))
            {
                if (charReader.SupportsResetting)
                {
                    var isEmpty = true;
                    while (await charReader.MoveNextAsync())
                    {
                        isEmpty = false;
                    }
                    charReader.Reset();
                    Assert.That(charReader.MoveNextAsync(), Is.EqualTo(isEmpty), "Has element after resetting is not empty");
                }
                else
                {
                    Assert.That(() => charReader.Reset(), Throws.InvalidOperationException);
                }
            }
        }
    }

    public static class TestHelper
    {
        internal static void CurrentAndLA(this string input, ref int i, out int current, out int la)
        {
            var next = input.Expected(i, out current);
            i += next;
            input.Expected(i, out la);
        }

        private static int Expected(this string input, int i, out int codePoint)
        {
            var next = 0;
            do
            {
                codePoint = i + next >= input.Length
                                ? AsyncLACharIterator.EOF
                                : char.ConvertToUtf32(input, i + next);

                if (codePoint > char.MaxValue)
                {
                    next++;
                }

                if (codePoint == '\r')
                {
                    next++;
                }
                else
                {
                    break;
                }
            } while (true);

            return ++next;
        }
    }
}
