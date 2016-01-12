using System;
using System.Collections.Generic;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;

namespace CodeProject.Syntax.LALR.Tests
{
    public class GroupRxTests
    {
        [TestCase(null, ExpectedException = typeof (ArgumentNullException))]
        public void TestGroupRxPreconditions(IRx[] items)
        {
            GC.KeepAlive(new GroupRx(Multiplicity.Once, items));
        }

        [Test]
        public void TestGroupRxPreconditions()
        {
            Assert.That(() => new GroupRx(Multiplicity.Once), Throws.ArgumentException);
        }

        [TestCaseSource("_groupSource")]
        public string TestGroupMultiplicity(Multiplicity multiplicity, IList<IRx> exprs)
        {
            return new GroupRx(multiplicity, exprs.ToArray()).Pattern;
        }

        [TestCase(0, 'a', 'b', Result = "[ab]{0}")]
        [TestCase(3, '\\', 's', Result = @"[\\s]{3}")]
        [TestCase(7, 0x1D400, '-', Result = @"[\U0001D400\-]{7}")]
        public string TestCharGroupMultiplicity(int times, int first, params int[] rest)
        {
            return (new CharClassRx(first, rest) * times).Pattern;
        }

        private readonly object[] _groupSource = new object[]
            {
                new TestCaseData(Multiplicity.OneOrMore, Helper.Items(new CharRx('\\'), new CharRx('s')))
                    .Returns(@"(\\s)+"),
                new TestCaseData(Multiplicity.ZeroOrOnce, Helper.Items(new CharClassRx('a', 'b')))
                    .Returns(@"[ab]?"),
                new TestCaseData(new Multiplicity(1, 2), Helper.Items(new CharClassRx('a', 'b')))
                    .Returns(@"[ab]{1,2}"),
                new TestCaseData(Multiplicity.Once, Helper.Items(new CharRx('a'))).Returns("a"),
                new TestCaseData(Multiplicity.ZeroOrMore, Helper.Items(new CharRx('a'))).Returns("a*"),
                new TestCaseData(new Multiplicity(1, 2), Helper.Items(new CharRx('a'))).Returns("a{1,2}"),
                new TestCaseData(new Multiplicity(5), Helper.Items(new CharRx('a'))).Returns("a{5}"),
                new TestCaseData(new Multiplicity(5, -1), Helper.Items(new CharRx('a'))).Returns("a{5,}")
            };
    }
}