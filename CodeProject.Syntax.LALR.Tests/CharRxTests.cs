using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;

namespace CodeProject.Syntax.LALR.Tests
{
    public class CharRxTests
    {
        [TestCase('a', Result = "a")]
        [TestCase('s', Result = @"s")]
        [TestCase('\\', Result = @"\\")]
        [TestCase('.', Result = @"\.")]
        [TestCase('^', Result = @"\^")]
        [TestCase('$', Result = @"\$")]
        [TestCase('*', Result = @"\*")]
        [TestCase('+', Result = @"\+")]
        [TestCase('-', Result = "-")]
        [TestCase('?', Result = @"\?")]
        [TestCase('(', Result = @"\(")]
        [TestCase(')', Result = @"\)")]
        [TestCase('{', Result = @"\{")]
        [TestCase('}', Result = "}")]
        [TestCase('[', Result = @"\[")]
        [TestCase(']', Result = "]")]
        [TestCase('|', Result = @"\|")]
        [TestCase(0x1D400, Result = @"\U0001D400")]
        public string TestCharRx(int cp)
        {
            return new CharRx(cp).Pattern;
        }

        [TestCase('a', 'b', Result = false)]
        [TestCase('a', 'a', Result = true)]
        [TestCase('b', 'a', Result = false)]
        public bool TestCharRxEquality(int a, int b)
        {
            return new CharRx(a) == b;
        }

        [TestCase('a', 'b', Result = false)]
        [TestCase('a', 'a', Result = true)]
        public bool TestCharRxObjectEquality(int a, int b)
        {
// ReSharper disable SuspiciousTypeConversion.Global
            return new CharRx(a).Equals((object) (CharRx) b);
// ReSharper restore SuspiciousTypeConversion.Global
        }

        [TestCase('a', 'b', Result = true)]
        [TestCase('a', 'a', Result = true, Description = "Circumvent implicit conversation from int")]
        [TestCase('=', "=", Result = true, Description = "string can not be implicitly cast to a codepoint")]
        [TestCase('b', null, Result = true)]
        public bool TestCharRxObjectInEquality(int a, object b)
        {
            return !new CharRx(a).Equals(b);
        }

        [TestCase('a', 'b', Result = true)]
        [TestCase('a', 'a', Result = false)]
        [TestCase('b', 'a', Result = true)]
        public bool TestCharRxInEquality(int a, int b)
        {
            return new CharRx(a) != b;
        }

        [TestCase(0, 'a', Result = "a{0}")]
        [TestCase(3, '\\', Result = @"\\{3}")]
        [TestCase(7, 0x1D400, Result = @"\U0001D400{7}")]
        public string TestCharMultiplicity(int times, int codepoint)
        {
            return (((CharRx)codepoint) * times).Pattern;
        }
    }
}