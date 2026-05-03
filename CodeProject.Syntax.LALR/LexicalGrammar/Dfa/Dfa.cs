using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar.Dfa;

/// <summary>
/// One transition out of a <see cref="DfaState"/>: input codepoints in the inclusive
/// range [<see cref="Lo"/>..<see cref="Hi"/>] move the matcher to <see cref="Next"/>.
/// Transitions are sorted ascending by <see cref="Lo"/> and never overlap inside a state.
/// </summary>
public readonly struct DfaTransition(int lo, int hi, int next) : IEquatable<DfaTransition>
{
    public int Lo { get; } = lo;
    public int Hi { get; } = hi;
    public int Next { get; } = next;

    public bool Equals(DfaTransition other) => Lo == other.Lo && Hi == other.Hi && Next == other.Next;
    public override bool Equals(object obj) => obj is DfaTransition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lo, Hi, Next);
    public static bool operator ==(DfaTransition a, DfaTransition b) => a.Equals(b);
    public static bool operator !=(DfaTransition a, DfaTransition b) => !a.Equals(b);
}

/// <summary>
/// One DFA state. <see cref="Accept"/> is the smallest pattern ID that accepts here,
/// or -1 if the state is non-accepting.
/// </summary>
public readonly struct DfaState(int accept, DfaTransition[] transitions)
{
    public int Accept { get; } = accept;
    public DfaTransition[] Transitions { get; } = transitions;
    public bool IsAccept => Accept >= 0;
}

/// <summary>
/// Deterministic finite automaton over Unicode codepoints. State 0 is the start state.
/// Build with <see cref="DfaCompiler.Compile(IRx, int)"/> or
/// <see cref="DfaCompiler.CompileMany"/>. Read-only and safe to share between threads.
/// </summary>
public readonly struct Dfa
{
    public DfaState[] States { get; }

    /// <summary>Always 0 by construction; exposed for clarity.</summary>
    public int Start => 0;

    internal Dfa(DfaState[] states)
    {
        States = states;
    }

    /// <summary>
    /// Step one codepoint forward. Returns the destination state, or -1 if there is no
    /// transition (the matcher should treat -1 as "stuck" and stop extending the match).
    /// </summary>
    public int Step(int state, int codepoint)
    {
        var transitions = States[state].Transitions;
        // Linear scan; transitions are short for typical lexer patterns. A binary search
        // would be a strict win only past ~16 transitions per state.
        for (var i = 0; i < transitions.Length; i++)
        {
            var t = transitions[i];
            if (codepoint >= t.Lo && codepoint <= t.Hi)
            {
                return t.Next;
            }
            // Transitions are sorted by Lo; bail once we've passed the codepoint.
            if (codepoint < t.Lo)
            {
                return -1;
            }
        }
        return -1;
    }

    /// <summary>
    /// Run the DFA over <paramref name="input"/> and return the longest accepting prefix.
    /// On no match returns (-1, 0). On a zero-length accept (e.g., the pattern can match
    /// empty) returns (patternId, 0) — the caller decides whether empty matches are
    /// acceptable for its lexer model.
    /// </summary>
    public (int PatternId, int Length) LongestMatch(ReadOnlySpan<int> input)
    {
        var state = Start;
        var bestPattern = States[state].IsAccept ? States[state].Accept : -1;
        var bestLength = 0;

        for (var i = 0; i < input.Length; i++)
        {
            state = Step(state, input[i]);
            if (state < 0)
            {
                break;
            }
            if (States[state].IsAccept)
            {
                bestPattern = States[state].Accept;
                bestLength = i + 1;
            }
        }

        return (bestPattern, bestLength);
    }

    /// <summary>
    /// Run a byte-alphabet DFA (typically produced by <see cref="Utf8DfaLowering.Lower"/>)
    /// over UTF-8 input and return the longest accepting prefix. Length is in **bytes**,
    /// not codepoints — the caller can slice the input span directly using it.
    /// </summary>
    public (int PatternId, int Length) LongestMatchBytes(ReadOnlySpan<byte> input)
    {
        var state = Start;
        var bestPattern = States[state].IsAccept ? States[state].Accept : -1;
        var bestLength = 0;

        for (var i = 0; i < input.Length; i++)
        {
            state = Step(state, input[i]);
            if (state < 0)
            {
                break;
            }
            if (States[state].IsAccept)
            {
                bestPattern = States[state].Accept;
                bestLength = i + 1;
            }
        }

        return (bestPattern, bestLength);
    }
}
