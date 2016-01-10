using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR.Tests
{
    public class RxTests
    {
        [TestCase('a', Result = "a")]
        [TestCase('s', Result = @"s")]
        [TestCase('\\', Result = @"\\")]
        [TestCase(0x1D400, Result = @"\U0001D400")]
        public string TestCharRx(int cp)
        {
            return new CharRx(cp).Pattern;
        }

        [TestCase('a', 'b', 'c', Result = "abc")]
        [TestCase('\\', 's', Result = @"\\s")]
        public string TestCharSequenceRx(params int[] chars)
        {
            return new CharSequenceRx(chars).Pattern;
        }

        [TestCaseSource("_charClassSource")]
        public string TestCharClassRxFromCharsIList(IList<ISingleCharRx> chars)
        {
            return new CharClassRx(chars.ToArray()).Pattern;
        }

        private readonly object[] _charClassSource = new object[]
            {
                new TestCaseData(MakeRxArray('a', 'b', 'c')).Returns(@"[abc]"),
                new TestCaseData(MakeRxArray('\\', 's')).Returns(@"[\\s]"),
                new TestCaseData(MakeRxArray(new CharClassRx('\\', 's'))).Returns(@"[\\s]")
            };

        private static object MakeRxArray(params ISingleCharRx[] charExprs)
        {
            return charExprs;
        }

        private static object MakeRxArray(params int[] chars)
        {
            var count = chars.Length;
            var array = new PrintableList<ISingleCharRx>(count);

            for (var i = 0; i < count; i++)
            {
                array.Add(new CharRx(chars[i]));
            }
            return array;
        }
    }
}