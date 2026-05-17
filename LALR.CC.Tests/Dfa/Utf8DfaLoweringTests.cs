using System.Text;
using LALR.CC.LexicalGrammar;
using LALR.CC.LexicalGrammar.Dfa;
using Xunit;

namespace LALR.CC.Tests.Dfa;

public class Utf8DfaLoweringTests
{
    private static (int PatternId, int Length) Match(IRx pattern, string input, int patternId = 7)
    {
        var codepointDfa = DfaCompiler.Compile(pattern, patternId);
        var byteDfa = Utf8DfaLowering.Lower(codepointDfa);
        var bytes = Encoding.UTF8.GetBytes(input);
        return byteDfa.LongestMatchBytes(bytes);
    }

    [Fact]
    public void AsciiLiteral_MatchesSingleByte()
    {
        // 'a' is 0x61 — the lowered DFA should consume one byte and accept.
        Assert.Equal((1, 1), Match(new CharRx('a'), "a", 1));
        Assert.Equal((1, 1), Match(new CharRx('a'), "ab", 1));
        Assert.Equal((-1, 0), Match(new CharRx('a'), "b", 1));
    }

    [Fact]
    public void AsciiSequence_ConsumesEachByte()
    {
        // "foo" → 0x66 0x6F 0x6F; matches three bytes.
        Assert.Equal((7, 3), Match(new CharSequenceRx("foo"), "foo"));
        Assert.Equal((7, 3), Match(new CharSequenceRx("foo"), "foobar"));
        Assert.Equal((-1, 0), Match(new CharSequenceRx("foo"), "fox"));
    }

    [Fact]
    public void TwoByteCodepoint_LiteralMatch()
    {
        // € (U+20AC) is E2 82 AC — a 3-byte UTF-8 sequence; length should reflect bytes.
        var pattern = new CharRx(0x20AC);
        Assert.Equal((7, 3), Match(pattern, "€"));
        Assert.Equal((7, 3), Match(pattern, "€a")); // matches the 3-byte prefix only
        Assert.Equal((-1, 0), Match(pattern, "a"));
    }

    [Fact]
    public void SupplementaryPlaneCodepoint_LiteralMatch()
    {
        // 𝒜 (U+1D49C) encodes to F0 9D 92 9C — 4 bytes.
        var pattern = new CharRx(0x1D49C);
        Assert.Equal((7, 4), Match(pattern, "𝒜"));
        Assert.Equal((-1, 0), Match(pattern, "a"));
    }

    [Fact]
    public void MixedAsciiAndMultibyte_SequenceMatchesByteLength()
    {
        // "a€" → 0x61 E2 82 AC = 4 bytes; codepoint length is 2.
        var pattern = new CharSequenceRx("a€");
        Assert.Equal((7, 4), Match(pattern, "a€"));
        Assert.Equal((7, 4), Match(pattern, "a€xx"));
        Assert.Equal((-1, 0), Match(pattern, "ab"));
    }

    [Fact]
    public void AsciiCharClassRange_MatchesAnyMember()
    {
        // [0-9] over ASCII — purely 1-byte category.
        var pattern = new CharClassRx(true, [new CharRangeRx('0', '9')]);
        Assert.Equal((7, 1), Match(pattern, "0"));
        Assert.Equal((7, 1), Match(pattern, "5"));
        Assert.Equal((7, 1), Match(pattern, "9"));
        Assert.Equal((-1, 0), Match(pattern, "/"));
        Assert.Equal((-1, 0), Match(pattern, ":"));
    }

    [Fact]
    public void IdentifierClass_OneOrMore_LongestPrefix()
    {
        // [A-Za-z_]+ — purely ASCII, but the byte DFA must still loop on the class.
        var classRx = new CharClassRx(true, [
            new CharRangeRx('A', 'Z'),
            new CharRangeRx('a', 'z'),
            (CharRx)'_',
        ]);
        var pattern = new GroupRx(Multiplicity.OneOrMore, classRx);
        Assert.Equal((7, 9), Match(pattern, "rule_name42")); // stops at '4' — class excludes digits
        Assert.Equal((7, 3), Match(pattern, "abc 42"));
        Assert.Equal((-1, 0), Match(pattern, "9abc"));
    }

    [Fact]
    public void NegativeAsciiClass_MatchesNonMember()
    {
        // [^abc] — accepts anything except a, b, c. Includes multi-byte codepoints.
        var pattern = new CharClassRx(false, 'a', 'b', 'c');
        Assert.Equal((-1, 0), Match(pattern, "a"));
        Assert.Equal((-1, 0), Match(pattern, "b"));
        Assert.Equal((7, 1), Match(pattern, "d"));
        Assert.Equal((7, 3), Match(pattern, "€")); // 3 bytes, full multi-byte char
        Assert.Equal((7, 4), Match(pattern, "𝒜")); // 4 bytes
    }

    [Fact]
    public void EolPattern_OptionalCrThenLf()
    {
        // Bootstrap-style `[\r]?[\n]` — verifies optional + literal in byte DFA.
        var pattern = new GroupRx(
            Multiplicity.Once,
            new GroupRx(Multiplicity.ZeroOrOnce, new CharRx('\r')),
            new CharRx('\n'));

        Assert.Equal((7, 1), Match(pattern, "\n"));
        Assert.Equal((7, 2), Match(pattern, "\r\n"));
        Assert.Equal((-1, 0), Match(pattern, "\r"));
        Assert.Equal((-1, 0), Match(pattern, "abc"));
    }

    [Fact]
    public void MultiPattern_FirstWinsOnTies_LongestOnDifferentLengths()
    {
        var keyword = new CharSequenceRx("if");
        var ident = new GroupRx(Multiplicity.OneOrMore, new CharClassRx(true, [new CharRangeRx('a', 'z')]));
        var codepointDfa = DfaCompiler.CompileMany([(keyword, 0), (ident, 1)]);
        var byteDfa = Utf8DfaLowering.Lower(codepointDfa);

        Assert.Equal((0, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("if")));
        Assert.Equal((1, 3), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("ifx")));
        Assert.Equal((1, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("xy")));
        Assert.Equal((-1, 0), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("9")));
    }

    [Fact]
    public void InvalidUtf8Bytes_NoMatch()
    {
        // A bare 0xFF can never appear in valid UTF-8 — the byte DFA should reject it.
        var pattern = new GroupRx(Multiplicity.OneOrMore,
            new CharClassRx(false, 'x'));   // matches anything except 'x'
        var codepointDfa = DfaCompiler.Compile(pattern, 1);
        var byteDfa = Utf8DfaLowering.Lower(codepointDfa);

        // 0xFF is a valid byte for the codepoint pattern (0xFF is a real Unicode
        // scalar), but the lowered DFA only accepts UTF-8 byte sequences. 0xFF as a
        // standalone byte is not a valid UTF-8 leading byte for any codepoint.
        Assert.Equal((-1, 0), byteDfa.LongestMatchBytes([0xFF]));
    }

    [Fact]
    public void TwoByteRangeCrossingLeadingByteBoundary()
    {
        // Range [0x80..0x100] crosses leading-byte boundary inside 2-byte category:
        //   0x80..0xBF → C2 [80..BF]
        //   0xC0..0xFF → C3 [80..BF]
        //   0x100..0x100 → C4 80
        // The lowered DFA should accept all three byte sequences.
        var pattern = new CharClassRx(true, [new CharRangeRx(0x80, 0x100)]);
        var byteDfa = Utf8DfaLowering.Lower(DfaCompiler.Compile(pattern, 0));

        Assert.Equal((0, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("\u0080")));
        Assert.Equal((0, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("\u00BF")));
        Assert.Equal((0, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("\u00FF")));
        Assert.Equal((0, 2), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("\u0100")));
        // Codepoint just outside the range still encodes as 2 bytes (0xC4 0x81) but
        // shouldn't accept.
        Assert.Equal((-1, 0), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("\u0101")));
        // ASCII codepoint 'A' (0x41) is below the range — also no match.
        Assert.Equal((-1, 0), byteDfa.LongestMatchBytes(Encoding.UTF8.GetBytes("A")));
    }
}
