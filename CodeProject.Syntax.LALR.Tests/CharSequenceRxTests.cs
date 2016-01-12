using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;

namespace CodeProject.Syntax.LALR.Tests
{
    public class CharSequenceRxTests
    {
        [TestCase('a', 'b', 'c', Result = "abc")]
        [TestCase('\\', 's', Result = @"\\s")]
        [TestCase('.', '[', Result = @"\.\[")]
        public string TestCharSequenceRx(params int[] chars)
        {
            return new CharSequenceRx(chars).Pattern;
        }

        [TestCase(null, Result = "")]
        [TestCase(0, Result = "")]
        [TestCase(1, Result = "\0")]
        public string TestCharSequenceRxNullOrEmptyArray(int? size)
        {
            return new CharSequenceRx(size.HasValue ? new CharRx[size.Value] : null as CharRx[]).Pattern;
        }

        [TestCase(+0, -1, "ab", Result = "(ab)*")]
        [TestCase(+0, +1, "^𝒜𝓑$", Result = @"(\^\U0001D49C\U0001D4D1\$)?")]
        [TestCase(+1, -1, @"\", Result = @"(\\)+")]
        [TestCase(+1, +1, "x", Result = "x")]
        [TestCase(+1, +2, "a", Result = "(a){1,2}")]
        [TestCase(+1, +1, "", Result = "")]
        [TestCase(+1, +2, "", Result = "(){1,2}")]
        [TestCase(+1, +1, null, Result = "")]
        [TestCase(+1, +2, null, Result = "(){1,2}")]
        public string TestCharSequenceRxMultiplicity(int from, int to, string sequence)
        {
            return (((CharSequenceRx) sequence)*new Multiplicity(from, to)).Pattern;
        }
    }
}