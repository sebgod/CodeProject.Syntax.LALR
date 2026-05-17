using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class CharSequenceRxTests
{
    [Theory]
    [InlineData("abc", new int[] { 'a', 'b', 'c' })]
    [InlineData(@"\\s", new int[] { '\\', 's' })]
    [InlineData(@"\.\[", new int[] { '.', '[' })]
    public void TestCharSequenceRx(string expected, int[] chars)
    {
        Assert.Equal(expected, new CharSequenceRx(chars).Pattern);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "")]
    [InlineData(1, "\0")]
    public void TestCharSequenceRxNullOrEmptyArray(int? size, string expected)
    {
        Assert.Equal(expected, new CharSequenceRx(size.HasValue ? new CharRx[size.Value] : null as CharRx[]).Pattern);
    }

    [Theory]
    [InlineData(+0, -1, "ab", "(ab)*")]
    [InlineData(+0, +1, "^𝒜𝓑$", @"(\^\U0001D49C\U0001D4D1\$)?")]
    [InlineData(+1, -1, @"\", @"(\\)+")]
    [InlineData(+1, +1, "x", "x")]
    [InlineData(+1, +2, "a", "(a){1,2}")]
    [InlineData(+1, +1, "", "")]
    [InlineData(+1, +2, "", "(){1,2}")]
    [InlineData(+1, +1, null, "")]
    [InlineData(+1, +2, null, "(){1,2}")]
    public void TestCharSequenceRxMultiplicity(int from, int to, string sequence, string expected)
    {
        Assert.Equal(expected, (((CharSequenceRx)sequence) * new Multiplicity(from, to)).Pattern);
    }
}
