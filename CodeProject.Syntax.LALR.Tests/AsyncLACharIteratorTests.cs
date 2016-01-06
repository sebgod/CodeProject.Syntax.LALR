using System;
using System.Linq;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.Tests
{
    public class AsyncLACharIteratorTests
    {
        [TestCase("1+2*34+5/6\r\n")]
        [TestCase("\r\n")]
        [TestCase("\r\r")]
        [TestCase("\n\r")]
        [TestCase("𝒜\r\n𝓑")]
        [TestCase("")]
        public async Task TestNormalisationAsync(string input)
        {
            // var expectedCount = input.Count(p => !char.IsSurrogate(p) && p != '\r');
            using (var charReader = new AsyncLACharIterator(new StringReader(input)))
            {
                var i = 0;
                while (i < input.Length)
                {
                    int expectedCurrent;
                    int expectedLA;
                    input.CurrentAndLA(ref i, out expectedCurrent, out expectedLA);
                    Assert.That(await charReader.MoveNextAsync(),
                        Is.EqualTo(expectedCurrent != AsyncLACharIterator.EOF),
                        "Move next");
   
                    var current = await charReader.CurrentAsync();
                    Assert.That(current, Is.EqualTo(expectedCurrent), "Current codepoint");

                    var la = await charReader.LookAheadAsync();
                    
                    Assert.That(la, Is.EqualTo(expectedLA), "lookahead codepoint");
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
