using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class CharRxTests
{
    [Theory]
    [InlineData('a', "a")]
    [InlineData('s', "s")]
    [InlineData('\\', @"\\")]
    [InlineData('.', @"\.")]
    [InlineData('^', @"\^")]
    [InlineData('$', @"\$")]
    [InlineData('*', @"\*")]
    [InlineData('+', @"\+")]
    [InlineData('-', "-")]
    [InlineData('?', @"\?")]
    [InlineData('(', @"\(")]
    [InlineData(')', @"\)")]
    [InlineData('{', @"\{")]
    [InlineData('}', "}")]
    [InlineData('[', @"\[")]
    [InlineData(']', "]")]
    [InlineData('|', @"\|")]
    [InlineData(0x1D400, @"\U0001D400")]
    public void TestCharRx(int cp, string expected)
    {
        Assert.Equal(expected, new CharRx(cp).Pattern);
    }

    [Theory]
    [InlineData('a', 'b', false)]
    [InlineData('a', 'a', true)]
    [InlineData('b', 'a', false)]
    public void TestCharRxEquality(int a, int b, bool expected)
    {
        Assert.Equal(expected, new CharRx(a) == b);
    }

    [Theory]
    [InlineData('a', 'b', false)]
    [InlineData('a', 'a', true)]
    public void TestCharRxObjectEquality(int a, int b, bool expected)
    {
        Assert.Equal(expected, new CharRx(a).Equals((object)(CharRx)b));
    }

    [Theory]
    [InlineData('a', 'b', true)]
    // Circumvent implicit conversion from int — comparing CharRx against a char (boxed as int).
    [InlineData('a', 'a', true)]
    // string can not be implicitly cast to a codepoint, so equality is false.
    [InlineData('=', "=", true)]
    [InlineData('b', null, true)]
    public void TestCharRxObjectInEquality(int a, object b, bool expected)
    {
        Assert.Equal(expected, !new CharRx(a).Equals(b));
    }

    [Theory]
    [InlineData('a', 'b', true)]
    [InlineData('a', 'a', false)]
    [InlineData('b', 'a', true)]
    public void TestCharRxInEquality(int a, int b, bool expected)
    {
        Assert.Equal(expected, new CharRx(a) != b);
    }

    [Theory]
    [InlineData(0, 'a', "a{0}")]
    [InlineData(3, '\\', @"\\{3}")]
    [InlineData(7, 0x1D400, @"\U0001D400{7}")]
    public void TestCharMultiplicity(int times, int codepoint, string expected)
    {
        Assert.Equal(expected, (((CharRx)codepoint) * times).Pattern);
    }
}
