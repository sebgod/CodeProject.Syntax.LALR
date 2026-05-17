using System;
using System.Linq;
using LALR.CC.LexicalGrammar;
using LALR.CC.LexicalGrammar.Dfa;
using Xunit;

namespace LALR.CC.Tests.Dfa;

public class DfaCompilerTests
{
    private static int[] CodepointsOf(string s)
    {
        var list = new System.Collections.Generic.List<int>(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var cp = char.ConvertToUtf32(s, i);
            list.Add(cp);
            i += cp > char.MaxValue ? 2 : 1;
        }
        return [.. list];
    }

    private static (int PatternId, int Length) Match(IRx pattern, string input, int patternId = 7)
    {
        var dfa = DfaCompiler.Compile(pattern, patternId);
        return dfa.LongestMatch(CodepointsOf(input));
    }

    [Fact]
    public void SingleChar_MatchesItself()
    {
        var dfa = DfaCompiler.Compile(new CharRx('a'), 1);
        Assert.Equal((1, 1), dfa.LongestMatch(CodepointsOf("a")));
        Assert.Equal((1, 1), dfa.LongestMatch(CodepointsOf("ab")));
        Assert.Equal((-1, 0), dfa.LongestMatch(CodepointsOf("b")));
        Assert.Equal((-1, 0), dfa.LongestMatch(CodepointsOf("")));
    }

    [Fact]
    public void SequenceOfChars_RequiresAllInOrder()
    {
        var pattern = new CharSequenceRx("foo");
        Assert.Equal((7, 3), Match(pattern, "foo"));
        Assert.Equal((7, 3), Match(pattern, "foobar"));
        Assert.Equal((-1, 0), Match(pattern, "fo"));
        Assert.Equal((-1, 0), Match(pattern, "fox"));
    }

    [Fact]
    public void PositiveCharClass_MatchesAnyInClass()
    {
        var pattern = new CharClassRx(true, [(CharRx)'a', (CharRx)'b', (CharRx)'c']);
        Assert.Equal((7, 1), Match(pattern, "a"));
        Assert.Equal((7, 1), Match(pattern, "b"));
        Assert.Equal((7, 1), Match(pattern, "c"));
        Assert.Equal((-1, 0), Match(pattern, "d"));
    }

    [Fact]
    public void CharClassWithRange_AcceptsRangeMembers()
    {
        var pattern = new CharClassRx(true, [new CharRangeRx('0', '9')]);
        Assert.Equal((7, 1), Match(pattern, "0"));
        Assert.Equal((7, 1), Match(pattern, "5"));
        Assert.Equal((7, 1), Match(pattern, "9"));
        Assert.Equal((-1, 0), Match(pattern, "/")); // just before '0'
        Assert.Equal((-1, 0), Match(pattern, ":")); // just after '9'
        Assert.Equal((-1, 0), Match(pattern, "a"));
    }

    [Fact]
    public void NegativeCharClass_RejectsMembers()
    {
        var pattern = new CharClassRx(false, 'a', 'b');
        Assert.Equal((-1, 0), Match(pattern, "a"));
        Assert.Equal((-1, 0), Match(pattern, "b"));
        Assert.Equal((7, 1), Match(pattern, "c"));
        Assert.Equal((7, 1), Match(pattern, "z"));
        Assert.Equal((7, 1), Match(pattern, "0"));
    }

    [Fact]
    public void Optional_MatchesZeroOrOne()
    {
        var pattern = new GroupRx(Multiplicity.ZeroOrOnce, new CharRx('a'));
        // {0,1}: empty input matches with length 0; "a" matches with length 1.
        Assert.Equal((7, 0), Match(pattern, ""));
        Assert.Equal((7, 1), Match(pattern, "a"));
        Assert.Equal((7, 1), Match(pattern, "aa"));
        // No 'a' prefix: still accepts length 0 (the optional was bypassed).
        Assert.Equal((7, 0), Match(pattern, "b"));
    }

    [Fact]
    public void Star_MatchesZeroOrMore()
    {
        var pattern = new GroupRx(Multiplicity.ZeroOrMore, new CharRx('a'));
        Assert.Equal((7, 0), Match(pattern, ""));
        Assert.Equal((7, 1), Match(pattern, "a"));
        Assert.Equal((7, 4), Match(pattern, "aaaa"));
        Assert.Equal((7, 3), Match(pattern, "aaab"));
    }

    [Fact]
    public void Plus_RequiresAtLeastOne()
    {
        var pattern = new GroupRx(Multiplicity.OneOrMore, new CharRx('a'));
        Assert.Equal((-1, 0), Match(pattern, ""));
        Assert.Equal((7, 1), Match(pattern, "a"));
        Assert.Equal((7, 4), Match(pattern, "aaaa"));
        Assert.Equal((7, 3), Match(pattern, "aaab"));
        Assert.Equal((-1, 0), Match(pattern, "b"));
    }

    [Fact]
    public void ExactRepetition_RequiresExactCount()
    {
        var pattern = new GroupRx(new Multiplicity(3), new CharRx('a'));
        Assert.Equal((-1, 0), Match(pattern, "aa"));
        Assert.Equal((7, 3), Match(pattern, "aaa"));
        Assert.Equal((7, 3), Match(pattern, "aaaa"));
    }

    [Fact]
    public void RangedRepetition_AcceptsAnyCountInRange()
    {
        var pattern = new GroupRx(new Multiplicity(2, 4), new CharRx('a'));
        Assert.Equal((-1, 0), Match(pattern, "a"));
        Assert.Equal((7, 2), Match(pattern, "aa"));
        Assert.Equal((7, 3), Match(pattern, "aaa"));
        Assert.Equal((7, 4), Match(pattern, "aaaa"));
        Assert.Equal((7, 4), Match(pattern, "aaaaa")); // longest = 4
    }

    [Fact]
    public void OpenEndedRepetition_RepeatsBeyondMin()
    {
        var pattern = new GroupRx(new Multiplicity(2, -1), new CharRx('a'));
        Assert.Equal((-1, 0), Match(pattern, "a"));
        Assert.Equal((7, 2), Match(pattern, "aa"));
        Assert.Equal((7, 6), Match(pattern, "aaaaaa"));
    }

    [Fact]
    public void SupplementaryPlaneCodepoint_DecodesAndMatches()
    {
        // 𝒜 (U+1D49C) requires two UTF-16 code units; LongestMatch consumes one codepoint.
        var pattern = new CharRx(0x1D49C);
        Assert.Equal((7, 1), Match(pattern, "𝒜"));
        Assert.Equal((-1, 0), Match(pattern, "a"));
    }

    [Fact]
    public void OptionalThenLiteral_BootstrapEolPattern()
    {
        // Mirrors Bootstrap's `[\r]?[\n]` — optional CR followed by required LF.
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
    public void IdentifierLikeClass_MatchesLongestPrefix()
    {
        // [-A-Za-z0-9_]+
        var classRx = new CharClassRx(true, [
            (CharRx)'-',
            new CharRangeRx('A', 'Z'),
            new CharRangeRx('a', 'z'),
            new CharRangeRx('0', '9'),
            (CharRx)'_',
        ]);
        var pattern = new GroupRx(Multiplicity.OneOrMore, classRx);

        Assert.Equal((7, 11), Match(pattern, "rule-name42"));
        Assert.Equal((7, 3), Match(pattern, "_x_"));
        Assert.Equal((7, 2), Match(pattern, "ab cd")); // stops at space
        Assert.Equal((-1, 0), Match(pattern, " "));
    }

    [Fact]
    public void MultiPattern_FirstWinsOnEqualLength_LongestWinsOtherwise()
    {
        // Pattern 0: "if" exactly. Pattern 1: identifier ([a-z]+).
        // On input "if" both match length 2 → tie → pattern 0 wins (lower id).
        // On input "ifx" pattern 1 matches 3 vs pattern 0's 2 → pattern 1 wins.
        var keyword = new CharSequenceRx("if");
        var ident = new GroupRx(Multiplicity.OneOrMore, new CharClassRx(true, [new CharRangeRx('a', 'z')]));
        var dfa = DfaCompiler.CompileMany([(keyword, 0), (ident, 1)]);

        Assert.Equal((0, 2), dfa.LongestMatch(CodepointsOf("if")));
        Assert.Equal((1, 3), dfa.LongestMatch(CodepointsOf("ifx")));
        Assert.Equal((1, 2), dfa.LongestMatch(CodepointsOf("xy")));
        Assert.Equal((-1, 0), dfa.LongestMatch(CodepointsOf("9")));
    }

    [Fact]
    public void Compile_RejectsNullPattern()
    {
        Assert.Throws<ArgumentNullException>(() => DfaCompiler.Compile(null));
    }

    [Fact]
    public void CompileMany_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => DfaCompiler.CompileMany([]));
    }
}
