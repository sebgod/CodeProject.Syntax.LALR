using System;
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

        [TestCase('a', 'b', Result = false)]
        [TestCase('a', 'a', Result = true)]
        [TestCase('b', 'a', Result = false)]
        public bool TestCharRxEquality(int a, int b)
        {
            return new CharRx(a) == b;
        }

        [TestCase('a', 'b', Result = true)]
        [TestCase('a', 'a', Result = false)]
        [TestCase('b', 'a', Result = true)]
        public bool TestCharRxInEquality(int a, int b)
        {
            return new CharRx(a) != b;
        }

        [TestCase('a', 'b', Result = "a-b")]
        [TestCase('a', 'a', Result = "a")]
        [TestCase('b', 'a', ExpectedException = typeof(ArgumentException))]
        public string TestCharRangeRx(int a, int b)
        {
            return new CharRangeRx(a, b).Pattern;
        }

        [TestCase(-1, -1, ExpectedException = typeof(ArgumentException))]
        [TestCase(-1, +0, ExpectedException = typeof(ArgumentException))]
        [TestCase(-1, -2, ExpectedException = typeof(ArgumentException))]
        [TestCase(+3, -2, ExpectedException = typeof(ArgumentException))]
        [TestCase(+0, -1, Result = "*")]
        [TestCase(+0, +1, Result = "?")]
        [TestCase(+1, -1, Result = "+")]
        [TestCase(+1, +1, Result = "")]
        [TestCase(+1, +2, Result = "{1,2}")]
        [TestCase(+2, -1, Result = "{2,}")]
        [TestCase(+2, +4, Result = "{2,4}")]
        [TestCase(+3, +3, Result = "{3}")]
        public string TestMultiplicity(int from, int to)
        {
            return new Multiplicity(from, to).Pattern;
        }

        [TestCaseSource("_charClassSource")]
        public string TestCharClassRxFromCharsIList(bool positive, IList<ISingleCharRx> chars)
        {
            return new CharClassRx(positive, chars.ToArray()).Pattern;
        }

        private readonly object[] _charClassSource = new object[]
            {
                new TestCaseData(true, MakeRxArray('a', 'b', 'c')).Returns(@"[abc]"),
                new TestCaseData(true, MakeRxArray('\\', 's')).Returns(@"[\\s]"),
                new TestCaseData(true, MakeRxArray(new CharClassRx('\\', 's'))).Returns(@"[\\s]"),
                new TestCaseData(true, MakeRxArray(new CharClassRx(false, '\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(false, MakeRxArray(new CharClassRx(false, '\\', 's'))).Returns(@"[^\\s]"),
                new TestCaseData(false, MakeRxArray(new CharClassRx('\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, MakeRxArray(new CharClassRx(false, 'a'), new CharClassRx('b')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, MakeRxArray(new CharRangeRx('A', 'Z'), new CharRangeRx('a', 'z')))
                    .Returns("[A-Za-z]")
            };

        private static object MakeRxArray(params ISingleCharRx[] charExprs)
        {
            return new PrintableList<ISingleCharRx>(charExprs);
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