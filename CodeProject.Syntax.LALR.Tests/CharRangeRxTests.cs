using System;
using CodeProject.Syntax.LALR.LexicalGrammar;
using NUnit.Framework;

namespace CodeProject.Syntax.LALR.Tests
{
    public class CharRangeRxTests
    {
        [TestCase('a', 'b', Result = "a-b")]
        [TestCase('a', 'a', Result = "a")]
        [TestCase('b', 'a', ExpectedException = typeof (ArgumentException))]
        public string TestCharRangeRx(int a, int b)
        {
            return new CharRangeRx(a, b).PatternInsideClass;
        }

        [TestCase('a', 'b', ExpectedException = typeof (InvalidOperationException))]
        [TestCase('a', 'a', ExpectedException = typeof (InvalidOperationException))]
        [TestCase('b', 'a', ExpectedException = typeof (ArgumentException))]
        public string TestCharRangeRxPatternException(int a, int b)
        {
            return new CharRangeRx(a, b).Pattern;
        }

    }
}