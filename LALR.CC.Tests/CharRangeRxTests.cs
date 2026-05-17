using System;
using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class CharRangeRxTests
{
    [Theory]
    [InlineData('a', 'b', "a-b")]
    [InlineData('a', 'a', "a")]
    public void TestCharRangeRx(int a, int b, string expected)
    {
        Assert.Equal(expected, new CharRangeRx(a, b).PatternInsideClass);
    }

    [Fact]
    public void TestCharRangeRxThrowsWhenToSmallerThanFrom()
    {
        Assert.Throws<ArgumentException>(() => new CharRangeRx('b', 'a'));
    }

    [Theory]
    [InlineData('a', 'b')]
    [InlineData('a', 'a')]
    public void TestCharRangeRxPatternThrows(int a, int b)
    {
        var range = new CharRangeRx(a, b);
        Assert.Throws<InvalidOperationException>(() => range.Pattern);
    }
}
