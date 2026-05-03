using System;
using System.Collections.Generic;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

public class MultiplicityTests
{
    [Theory]
    [InlineData(+0, -1, "*")]
    [InlineData(+0, +1, "?")]
    [InlineData(+1, -1, "+")]
    [InlineData(+1, +1, "")]
    [InlineData(+1, +2, "{1,2}")]
    [InlineData(+2, -1, "{2,}")]
    [InlineData(+2, +4, "{2,4}")]
    [InlineData(+3, +3, "{3}")]
    public void TestMultiplicity(int from, int to, string expected)
    {
        Assert.Equal(expected, new Multiplicity(from, to).Pattern);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1, +0)]
    [InlineData(-1, -2)]
    [InlineData(+3, -2)]
    public void TestMultiplicityInvalidThrows(int from, int to)
    {
        Assert.Throws<ArgumentException>(() => new Multiplicity(from, to));
    }

    [Fact]
    public void TestMultiplicityEquality()
    {
        var setConstants = new HashSet<Multiplicity>(new[]
        {
            Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
            Multiplicity.Once, Multiplicity.ZeroOrOnce,
        });
        var unionWithEquivalent = new HashSet<Multiplicity>(setConstants).Union(new[]
        {
            new Multiplicity(1, -1),
            new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1),
        });

        Assert.Equal(setConstants.OrderBy(p => p.Pattern), unionWithEquivalent.OrderBy(p => p.Pattern));
    }

    [Fact]
    public void TestMultiplicityEqualityObject()
    {
        var dummy = new object();
        var setConstants = new HashSet<object>(new object[]
        {
            Multiplicity.OneOrMore, Multiplicity.ZeroOrMore,
            Multiplicity.Once, Multiplicity.ZeroOrOnce, dummy,
        });
        var unionWithEquivalent = new HashSet<object>(setConstants).Union(new object[]
        {
            new Multiplicity(1, -1),
            new Multiplicity(0, -1), new Multiplicity(1), new Multiplicity(1, -1), dummy,
        });

        Assert.Equal(setConstants.Count, unionWithEquivalent.Count());
        Assert.True(setConstants.SetEquals(unionWithEquivalent));
    }

    [Fact]
    public void TestMultiplicityInEqualityObject()
    {
        Assert.False(Multiplicity.OneOrMore.Equals(new object()));
    }

    [Fact]
    public void TestMultiplicityInEquality()
    {
        Assert.NotEqual(Multiplicity.Once, Multiplicity.ZeroOrMore);
    }

    [Fact]
    public void TestMultiplicityInEqualityOp()
    {
        Assert.True(Multiplicity.Once != Multiplicity.ZeroOrMore);
    }
}
