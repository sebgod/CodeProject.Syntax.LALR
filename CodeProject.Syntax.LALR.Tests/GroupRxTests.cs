using System;
using System.Collections.Generic;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

public class GroupRxTests
{
    [Fact]
    public void TestGroupRxPreconditionsNullArray()
    {
        Assert.Throws<ArgumentNullException>(() => new GroupRx(Multiplicity.Once, null));
    }

    [Fact]
    public void TestGroupRxPreconditionsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new GroupRx(Multiplicity.Once));
    }

    public static IEnumerable<object[]> GroupSource() =>
    [
        [Multiplicity.OneOrMore, Helper.Items(new CharRx('\\'), new CharRx('s')), @"(\\s)+"],
        [Multiplicity.ZeroOrOnce, Helper.Items(new CharClassRx('a', 'b')), @"[ab]?"],
        [new Multiplicity(1, 2), Helper.Items(new CharClassRx('a', 'b')), @"[ab]{1,2}"],
        [Multiplicity.Once, Helper.Items(new CharRx('a')), "a"],
        [Multiplicity.ZeroOrMore, Helper.Items(new CharRx('a')), "a*"],
        [new Multiplicity(1, 2), Helper.Items(new CharRx('a')), "a{1,2}"],
        [new Multiplicity(5), Helper.Items(new CharRx('a')), "a{5}"],
        [new Multiplicity(5, -1), Helper.Items(new CharRx('a')), "a{5,}"],
    ];

    [Theory]
    [MemberData(nameof(GroupSource))]
    public void TestGroupMultiplicity(Multiplicity multiplicity, IList<IRx> exprs, string expected)
    {
        Assert.Equal(expected, new GroupRx(multiplicity, exprs.ToArray()).Pattern);
    }

    [Theory]
    [InlineData(0, "[ab]{0}", new int[] { 'a', 'b' })]
    [InlineData(3, @"[\\s]{3}", new int[] { '\\', 's' })]
    [InlineData(7, @"[\U0001D400\-]{7}", new int[] { 0x1D400, '-' })]
    public void TestCharGroupMultiplicity(int times, string expected, int[] codepoints)
    {
        var first = codepoints[0];
        var rest = codepoints.Skip(1).ToArray();
        Assert.Equal(expected, (new CharClassRx(first, rest) * times).Pattern);
    }
}
