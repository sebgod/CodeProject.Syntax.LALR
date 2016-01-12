using System;
using System.Collections.Generic;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;

namespace CodeProject.Syntax.LALR.Tests
{
    public class CharClassRxTests
    {
        [TestCase(-1, +0, true, 'a', ExpectedException = typeof(ArgumentException))]
        [TestCase(-1, -2, true, 'b', ExpectedException = typeof(ArgumentException))]
        [TestCase(+0, -1, true, 'a', 'b', Result = "[ab]*")]
        [TestCase(+0, +1, false, '.', Result = "[^.]?")]
        [TestCase(+1, -1, true, '\\', Result = @"[\\]+")]
        [TestCase(+1, +1, true, 'x', Result = "[x]")]
        [TestCase(+1, +2, true, 'a', Result = "[a]{1,2}")]
        [TestCase(+2, -1, false, 'a', 'b', Result = "[^ab]{2,}")]
        public string TestCharClassRxMultiplicity(int from, int to, bool positive, int first, params int[] rest)
        {
            return (new CharClassRx(positive, first, rest) * new Multiplicity(from, to)).Pattern;
        }

        [Test]
        public void TestCharClassRxPreconditionNullArray()
        {
            Assert.That(() => new CharClassRx(false, null), Throws.TypeOf(typeof(ArgumentNullException)));
        }

        [Test]
        public void TestCharClassRxPreconditionNullRestArray()
        {
            Assert.That(() => new CharClassRx(false, 0, null), Throws.TypeOf(typeof(ArgumentNullException)));
        }

        [Test]
        public void TestCharClassRxPreconditionEmptyArray()
        {
            Assert.That(() => new CharClassRx(false, new ISingleCharRx[0]), Throws.ArgumentException);
        }

        [TestCaseSource("_charClassSource")]
        public string TestCharClassRxFromCharsIList(bool positive, IList<ISingleCharRx> chars)
        {
            return new CharClassRx(positive, chars.ToArray()).Pattern;
        }

        private readonly object[] _charClassSource = new object[]
            {
                new TestCaseData(true, Helper.Chars('a', 'b', 'c')).Returns("[abc]"),
                new TestCaseData(true, Helper.Chars('\\', 's')).Returns(@"[\\s]"),
                new TestCaseData(true, Helper.Chars('.', '[')).Returns("[.[]"),
                new TestCaseData(true, Helper.Chars('.', ']')).Returns("[.]]"),
                new TestCaseData(true, Helper.Chars('^')).Returns(@"[\^]"),
                new TestCaseData(true, Helper.Chars('-')).Returns(@"[\-]"),
                new TestCaseData(true, Helper.Chars('-', '\\', '^')).Returns(@"[\-\\\^]"),
                new TestCaseData(true, Helper.Chars('^', '-', '\\')).Returns(@"[\^\-\\]"),
                new TestCaseData(true, Helper.Chars(new CharClassRx('\\', 's'))).Returns(@"[\\s]"),
                new TestCaseData(true, Helper.Chars(new CharClassRx(false, '\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(false, Helper.Chars(new CharClassRx(false, '\\', 's')))
                    .Returns(@"[^\\s]"),
                new TestCaseData(false, Helper.Chars(new CharClassRx('\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, Helper.Chars(new CharClassRx(false, 'a'), new CharClassRx('b')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, Helper.Chars(new CharRangeRx('A', 'Z'), new CharRangeRx('a', 'z')))
                    .Returns("[A-Za-z]")
            };
    }
}