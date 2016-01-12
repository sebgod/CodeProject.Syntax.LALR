using System;
using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR.Tests
{
    public class MultiplicityTests
    {
        [TestCase(-1, -1, ExpectedException = typeof (ArgumentException))]
        [TestCase(-1, +0, ExpectedException = typeof (ArgumentException))]
        [TestCase(-1, -2, ExpectedException = typeof (ArgumentException))]
        [TestCase(+3, -2, ExpectedException = typeof (ArgumentException))]
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
            var setConstants = new HashSet<Multiplicity>(new[]
                {
                    Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
                    Multiplicity.Once, Multiplicity.ZeroOrOnce
                });
            var unionWithEquivalent = new HashSet<Multiplicity>(setConstants).Union(new[]
                {
                    new Multiplicity(1, -1),
                    new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1)
                });

            Assert.That(setConstants, Is.EquivalentTo(unionWithEquivalent));
        }

        [Test]
        public void TestMultiplicityEqualityObject()
        {
            var dummy = new object();
            var setConstants = new HashSet<object>(new[]
                {
                    Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
                    Multiplicity.Once, Multiplicity.ZeroOrOnce, dummy
                });
            var unionWithEquivalent = new HashSet<object>(setConstants).Union(new[]
                {
                    new Multiplicity(1, -1),
                    new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1), dummy
                });

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
    }
}