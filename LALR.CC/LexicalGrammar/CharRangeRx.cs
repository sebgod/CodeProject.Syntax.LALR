using System;

namespace LALR.CC.LexicalGrammar;

public readonly struct CharRangeRx : IEquatable<CharRangeRx>, ISingleCharRx
{
    private readonly CharRx _from;
    private readonly CharRx _to;

    public CharRangeRx(CharRx from, CharRx to)
    {
        if (to < from)
        {
            throw new ArgumentException("To is smaller than from: " + from + "-" + to, nameof(to));
        }

        _from = from;
        _to = to;
    }

    internal int From => _from.Codepoint;
    internal int To => _to.Codepoint;

    public bool Equals(CharRangeRx other) => _from == other._from && _to == other._to;

    public override bool Equals(object obj) => obj is CharRangeRx other && Equals(other);

    public override int GetHashCode() => (_to.GetHashCode() << 11) ^ _from.GetHashCode();

    public static bool operator ==(CharRangeRx a, CharRangeRx b) => a.Equals(b);

    public static bool operator !=(CharRangeRx a, CharRangeRx b) => !(a == b);

    public string Pattern => throw new InvalidOperationException("Char ranges can only be used inside classes");

    public string PatternInsideClass => _from == _to
        ? _from.Pattern
        : $"{_from.Pattern}-{_to.Pattern}";

    public override string ToString() => PatternInsideClass;
}
