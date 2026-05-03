using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar.Dfa;

/// <summary>
/// Lowers a codepoint-alphabet DFA produced by <see cref="DfaCompiler"/> into an
/// equivalent byte-alphabet DFA whose transitions consume single UTF-8 bytes.
/// Each codepoint range <c>[lo..hi] → t</c> is expanded into one or more byte chains
/// that follow the UTF-8 encoding boundaries (1/2/3/4-byte categories) and three-way
/// split when leading bytes differ. Surrogates D800..DFFF are excluded — they can't
/// appear in valid UTF-8 input, so the lowered DFA simply has no path through them.
/// </summary>
public static class Utf8DfaLowering
{
    public static Dfa Lower(Dfa codepointDfa)
    {
        if (codepointDfa.States is null)
        {
            throw new ArgumentException("DFA has no states; pass a value compiled by DfaCompiler", nameof(codepointDfa));
        }

        var nfa = new NfaBuilder();
        var stateMap = new int[codepointDfa.States.Length];
        for (var i = 0; i < codepointDfa.States.Length; i++)
        {
            stateMap[i] = nfa.AddState();
            // copy accept marker so the byte DFA accepts at the structurally equivalent
            // states; intermediate byte states added below stay non-accepting.
            nfa.States[stateMap[i]].Accept = codepointDfa.States[i].Accept;
        }

        for (var i = 0; i < codepointDfa.States.Length; i++)
        {
            foreach (var t in codepointDfa.States[i].Transitions)
            {
                LowerCodepointRange(nfa, stateMap[i], t.Lo, t.Hi, stateMap[t.Next]);
            }
        }

        return SubsetConstruction.Build(nfa, stateMap[codepointDfa.Start]);
    }

    private static void LowerCodepointRange(NfaBuilder nfa, int from, int lo, int hi, int to)
    {
        // strip surrogate codepoints before splitting — they have no valid UTF-8 encoding
        if (lo <= 0xD7FF)
        {
            var sub = Math.Min(hi, 0xD7FF);
            SplitByByteCategory(nfa, from, lo, sub, to);
            if (sub == hi)
            {
                return;
            }
            lo = 0xE000;
        }
        if (lo >= 0xD800 && lo <= 0xDFFF)
        {
            lo = 0xE000;
        }
        if (lo <= hi)
        {
            SplitByByteCategory(nfa, from, lo, hi, to);
        }
    }

    private static void SplitByByteCategory(NfaBuilder nfa, int from, int lo, int hi, int to)
    {
        // walk the four UTF-8 length categories (1/2/3/4 bytes) and emit chains for each
        // sub-range. We mutate `lo` as we go so the next category picks up where the
        // previous left off.
        if (lo > hi)
        {
            return;
        }
        if (lo <= 0x7F)
        {
            var sub = Math.Min(hi, 0x7F);
            EmitFromCategory(nfa, from, lo, sub, to, byteLen: 1);
            if (sub == hi)
            {
                return;
            }
            lo = 0x80;
        }
        if (lo <= 0x7FF)
        {
            var sub = Math.Min(hi, 0x7FF);
            EmitFromCategory(nfa, from, lo, sub, to, byteLen: 2);
            if (sub == hi)
            {
                return;
            }
            lo = 0x800;
        }
        if (lo <= 0xFFFF)
        {
            var sub = Math.Min(hi, 0xFFFF);
            EmitFromCategory(nfa, from, lo, sub, to, byteLen: 3);
            if (sub == hi)
            {
                return;
            }
            lo = 0x10000;
        }
        if (lo <= 0x10FFFF)
        {
            var sub = Math.Min(hi, 0x10FFFF);
            EmitFromCategory(nfa, from, lo, sub, to, byteLen: 4);
        }
    }

    private static void EmitFromCategory(NfaBuilder nfa, int from, int lo, int hi, int to, int byteLen)
    {
        Span<byte> loBytes = stackalloc byte[byteLen];
        Span<byte> hiBytes = stackalloc byte[byteLen];
        Encode(lo, loBytes);
        Encode(hi, hiBytes);
        EmitRange(nfa, from, loBytes, hiBytes, to);
    }

    /// <summary>
    /// Recursively emits byte transitions for codepoints whose UTF-8 encoding lies in
    /// <c>[lo..hi]</c> (both spans equal length). When the leading bytes diverge we
    /// fan out into three sub-ranges (Russ Cox's standard UTF-8 lowering split):
    /// <list type="number">
    /// <item>leading byte = lo[0], suffix in [lo[1..], 0xBF...]</item>
    /// <item>leading byte in (lo[0]+1..hi[0]-1), suffix any continuation</item>
    /// <item>leading byte = hi[0], suffix in [0x80..., hi[1..]]</item>
    /// </list>
    /// Subset construction then dedupes the redundant intermediate states.
    /// </summary>
    private static void EmitRange(NfaBuilder nfa, int from, ReadOnlySpan<byte> lo, ReadOnlySpan<byte> hi, int to)
    {
        if (lo.Length == 1)
        {
            nfa.AddRange(from, lo[0], hi[0], to);
            return;
        }
        if (lo[0] == hi[0])
        {
            var s = nfa.AddState();
            nfa.AddRange(from, lo[0], lo[0], s);
            EmitRange(nfa, s, lo[1..], hi[1..], to);
            return;
        }

        // leading bytes differ — three-way split.
        var suffixLen = lo.Length - 1;
        Span<byte> floor = stackalloc byte[suffixLen];
        Span<byte> ceiling = stackalloc byte[suffixLen];
        floor.Fill(0x80);
        ceiling.Fill(0xBF);

        // part 1: lo[0] then [lo[1..], 0xBF...]
        {
            var s = nfa.AddState();
            nfa.AddRange(from, lo[0], lo[0], s);
            EmitRange(nfa, s, lo[1..], ceiling, to);
        }
        // part 2: any leading byte strictly between lo[0] and hi[0]
        if (hi[0] - lo[0] > 1)
        {
            var s = nfa.AddState();
            nfa.AddRange(from, (byte)(lo[0] + 1), (byte)(hi[0] - 1), s);
            EmitRange(nfa, s, floor, ceiling, to);
        }
        // part 3: hi[0] then [0x80..., hi[1..]]
        {
            var s = nfa.AddState();
            nfa.AddRange(from, hi[0], hi[0], s);
            EmitRange(nfa, s, floor, hi[1..], to);
        }
    }

    private static void Encode(int codepoint, Span<byte> dest)
    {
        // Inline UTF-8 encoder; we can't go through Rune because surrogates have already
        // been filtered upstream and Rune asserts on out-of-range scalars.
        if (codepoint < 0x80)
        {
            dest[0] = (byte)codepoint;
        }
        else if (codepoint < 0x800)
        {
            dest[0] = (byte)(0xC0 | (codepoint >> 6));
            dest[1] = (byte)(0x80 | (codepoint & 0x3F));
        }
        else if (codepoint < 0x10000)
        {
            dest[0] = (byte)(0xE0 | (codepoint >> 12));
            dest[1] = (byte)(0x80 | ((codepoint >> 6) & 0x3F));
            dest[2] = (byte)(0x80 | (codepoint & 0x3F));
        }
        else
        {
            dest[0] = (byte)(0xF0 | (codepoint >> 18));
            dest[1] = (byte)(0x80 | ((codepoint >> 12) & 0x3F));
            dest[2] = (byte)(0x80 | ((codepoint >> 6) & 0x3F));
            dest[3] = (byte)(0x80 | (codepoint & 0x3F));
        }
    }
}
