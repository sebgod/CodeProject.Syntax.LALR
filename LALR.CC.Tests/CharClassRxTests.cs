using System;
using System.Collections.Generic;
using System.Linq;
using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class CharClassRxTests
{
    [Theory]
    [InlineData(+0, -1, true, "[ab]*", new int[] { 'a', 'b' })]
    [InlineData(+0, +1, false, "[^.]?", new int[] { '.' })]
    [InlineData(+1, -1, true, @"[\\]+", new int[] { '\\' })]
    [InlineData(+1, +1, true, "[x]", new int[] { 'x' })]
    [InlineData(+1, +2, true, "[a]{1,2}", new int[] { 'a' })]
    [InlineData(+2, -1, false, "[^ab]{2,}", new int[] { 'a', 'b' })]
    public void TestCharClassRxMultiplicity(int from, int to, bool positive, string expected, int[] codepoints)
    {
        var first = codepoints[0];
        var rest = codepoints.Skip(1).ToArray();
        Assert.Equal(expected, (new CharClassRx(positive, first, rest) * new Multiplicity(from, to)).Pattern);
    }

    [Theory]
    [InlineData(-1, +0, true, 'a')]
    [InlineData(-1, -2, true, 'b')]
    public void TestCharClassRxMultiplicityThrows(int from, int to, bool positive, int first)
    {
        Assert.Throws<ArgumentException>(() =>
            new CharClassRx(positive, first) * new Multiplicity(from, to));
    }

    [Fact]
    public void TestCharClassRxPreconditionNullArray()
    {
        Assert.Throws<ArgumentNullException>(() => new CharClassRx(false, null));
    }

    [Fact]
    public void TestCharClassRxPreconditionNullRestArray()
    {
        Assert.Throws<ArgumentNullException>(() => new CharClassRx(false, 0, null));
    }

    [Fact]
    public void TestCharClassRxPreconditionEmptyArray()
    {
        Assert.Throws<ArgumentException>(() => new CharClassRx(false, []));
    }

    public static IEnumerable<object[]> CharClassSource() =>
    [
        [true, Helper.Chars('a', 'b', 'c'), "[abc]"],
        [true, Helper.Chars('\\', 's'), @"[\\s]"],
        [true, Helper.Chars('.', '['), "[.[]"],
        [true, Helper.Chars('.', ']'), "[.]]"],
        [true, Helper.Chars('^'), @"[\^]"],
        [true, Helper.Chars('-'), @"[\-]"],
        [true, Helper.Chars('-', '\\', '^'), @"[\-\\\^]"],
        [true, Helper.Chars('^', '-', '\\'), @"[\^\-\\]"],
        [true, Helper.Chars(new CharClassRx('\\', 's')), @"[\\s]"],
        [false, Helper.Chars(new CharClassRx(false, '\\', 's')), @"[^\\s]"],
        [true, Helper.Chars(new CharRangeRx('A', 'Z'), new CharRangeRx('a', 'z')), "[A-Za-z]"],
    ];

    [Theory]
    [MemberData(nameof(CharClassSource))]
    public void TestCharClassRxFromCharsIList(bool positive, IList<ISingleCharRx> chars, string expected)
    {
        Assert.Equal(expected, new CharClassRx(positive, chars.ToArray()).Pattern);
    }

    public static IEnumerable<object[]> CharClassThrowSource() =>
    [
        [true, Helper.Chars(new CharClassRx(false, '\\', 's'))],
        [false, Helper.Chars(new CharClassRx('\\', 's'))],
        [true, Helper.Chars(new CharClassRx(false, 'a'), new CharClassRx('b'))],
    ];

    [Theory]
    [MemberData(nameof(CharClassThrowSource))]
    public void TestCharClassRxFromCharsIListThrows(bool positive, IList<ISingleCharRx> chars)
    {
        Assert.Throws<ArgumentException>(() =>
            new CharClassRx(positive, chars.ToArray()).Pattern);
    }
}
