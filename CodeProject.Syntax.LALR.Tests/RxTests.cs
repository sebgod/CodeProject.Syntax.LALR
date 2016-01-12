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

        [TestCase('a', 'b', 'c', Result = "abc")]
        [TestCase('\\', 's', Result = @"\\s")]
        [TestCase('.', '[', Result = @"\.\[")]
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

        [TestCase('a', 'b', Result = false)]
        [TestCase('a', 'a', Result = true)]
        public bool TestCharRxObjectEquality(int a, int b)
        {
            return new CharRx(a).Equals((object)(CharRx)b);
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

        [TestCase('a', 'b', Result = "a-b")]
        [TestCase('a', 'a', Result = "a")]
        [TestCase('b', 'a', ExpectedException = typeof(ArgumentException))]
        public string TestCharRangeRx(int a, int b)
        {
            return new CharRangeRx(a, b).PatternInsideClass;
        }

        [TestCase('a', 'b', ExpectedException = typeof(InvalidOperationException))]
        [TestCase('a', 'a', ExpectedException = typeof(InvalidOperationException))]
        [TestCase('b', 'a', ExpectedException = typeof(ArgumentException))]
        public string TestCharRangeRxPatternEx(int a, int b)
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

        [Test]
        public void TestMultiplicityEquality()
        {
            var setConstants = new HashSet<Multiplicity>(new[] { Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
                Multiplicity.Once, Multiplicity.ZeroOrOnce });
            var unionWithEquivalent = new HashSet<Multiplicity>(setConstants).Union(new[] { new Multiplicity(1, -1),
                new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1) });

            Assert.That(setConstants, Is.EquivalentTo(unionWithEquivalent));
        }

        [Test]
        public void TestMultiplicityEqualityObject()
        {
            var dummy = new object();
            var setConstants = new HashSet<object>(new [] { Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
                Multiplicity.Once, Multiplicity.ZeroOrOnce, dummy });
            var unionWithEquivalent = new HashSet<object>(setConstants).Union(new [] { new Multiplicity(1, -1),
                new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1), dummy });

            Assert.That(setConstants, Is.EquivalentTo(unionWithEquivalent));
        }

        [Test]
        public void TestMultiplicityInEqualityObject()
        {
            Assert.That(Multiplicity.OneOrMore.Equals(new object()), Is.False);
        }

        [Test]
        public void TestMultiplicityInEquality()
        {
            Assert.That(Multiplicity.Once, Is.Not.EqualTo(Multiplicity.ZeroOrMore));
        }

        [Test]
        public void TestMultiplicityInEqualityOp()
        {
            Assert.That(Multiplicity.Once != Multiplicity.ZeroOrMore, Is.True);
        }

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
            Assert.That(() => new CharClassRx(false, null as ISingleCharRx[]), Throws.TypeOf(typeof(ArgumentNullException)));
        }

        [Test]
        public void TestCharClassRxPreconditionNullRestArray()
        {
            Assert.That(() => new CharClassRx(false, 0, null as int[]), Throws.TypeOf(typeof(ArgumentNullException)));
        }

        [Test]
        public void TestCharClassRxPreconditionEmptyArray()
        {
            Assert.That(() => new CharClassRx(false, new ISingleCharRx[0]), Throws.ArgumentException);
        }

        [TestCase(null, ExpectedException = typeof(ArgumentNullException))]
        public void TestGroupRxPreconditions(IRx[] items)
        {
            GC.KeepAlive(new GroupRx(Multiplicity.Once, items));
        }

        [Test]
        public void TestGroupRxPreconditions()
        {
            Assert.That(() => new GroupRx(Multiplicity.Once), Throws.ArgumentException);
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

        [TestCaseSource("_groupSource")]
        public string TestGroupMultiplicity(Multiplicity multiplicity, IList<IRx> exprs)
        {
            return new GroupRx(multiplicity, exprs.ToArray()).Pattern;
        }

        [TestCase(0, 'a',  Result = "a{0}")]
        [TestCase(3, '\\', Result = @"\\{3}")]
        [TestCase(7, 0x1D400, Result = @"\U0001D400{7}")]
        public string TestCharMultiplicity(int times, int codepoint)
        {
            return (((CharRx)codepoint) * times).Pattern;
        }

        [TestCase(0, 'a', 'b', Result = "[ab]{0}")]
        [TestCase(3, '\\', 's', Result = @"[\\s]{3}")]
        [TestCase(7, 0x1D400, '-', Result = @"[\U0001D400\-]{7}")]
        public string TestCharGroupMultiplicity(int times, int first, params int[] rest)
        {
            return (new CharClassRx(first, rest) * times).Pattern;
        }

        [TestCaseSource("_charClassSource")]
        public string TestCharClassRxFromCharsIList(bool positive, IList<ISingleCharRx> chars)
        {
            return new CharClassRx(positive, chars.ToArray()).Pattern;
        }

        private readonly object[] _charClassSource = new object[]
            {
                new TestCaseData(true, Chars('a', 'b', 'c')).Returns("[abc]"),
                new TestCaseData(true, Chars('\\', 's')).Returns(@"[\\s]"),
                new TestCaseData(true, Chars('.', '[')).Returns("[.[]"),
                new TestCaseData(true, Chars('.', ']')).Returns("[.]]"),
                new TestCaseData(true, Chars('^')).Returns(@"[\^]"),
                new TestCaseData(true, Chars('-')).Returns(@"[\-]"),
                new TestCaseData(true, Chars('-', '\\', '^')).Returns(@"[\-\\\^]"),
                new TestCaseData(true, Chars('^', '-', '\\')).Returns(@"[\^\-\\]"),
                new TestCaseData(true, Chars(new CharClassRx('\\', 's'))).Returns(@"[\\s]"),
                new TestCaseData(true, Chars(new CharClassRx(false, '\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(false, Chars(new CharClassRx(false, '\\', 's')))
                    .Returns(@"[^\\s]"),
                new TestCaseData(false, Chars(new CharClassRx('\\', 's')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, Chars(new CharClassRx(false, 'a'), new CharClassRx('b')))
                    .Throws(typeof (ArgumentException)),
                new TestCaseData(true, Chars(new CharRangeRx('A', 'Z'), new CharRangeRx('a', 'z')))
                    .Returns("[A-Za-z]")
            };

        private readonly object[] _groupSource = new object[]
            {
                new TestCaseData(Multiplicity.OneOrMore, Items(new CharRx('\\'), new CharRx('s')))
                    .Returns(@"(\\s)+"),
                new TestCaseData(Multiplicity.ZeroOrOnce, Items(new CharClassRx('a', 'b')))
                    .Returns(@"[ab]?"),
                new TestCaseData(new Multiplicity(1, 2), Items(new CharClassRx('a', 'b')))
                    .Returns(@"[ab]{1,2}"),
                new TestCaseData(Multiplicity.Once, Items(new CharRx('a'))).Returns("a"),
                new TestCaseData(Multiplicity.ZeroOrMore, Items(new CharRx('a'))).Returns("a*"),
                new TestCaseData(new Multiplicity(1, 2), Items(new CharRx('a'))).Returns("a{1,2}"),
                new TestCaseData(new Multiplicity(5), Items(new CharRx('a'))).Returns("a{5}"),
                new TestCaseData(new Multiplicity(5, -1), Items(new CharRx('a'))).Returns("a{5,}")
            };

        private static object Items(params IRx[] exprs)
        {
            return new PrintableList<IRx>(exprs);
        }

        private static object Chars(params ISingleCharRx[] charExprs)
        {
            return new PrintableList<ISingleCharRx>(charExprs);
        }

        private static object Chars(params int[] chars)
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