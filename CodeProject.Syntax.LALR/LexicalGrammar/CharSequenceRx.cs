using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

public readonly struct CharSequenceRx : IEquatable<CharSequenceRx>, IRx
{
    private readonly IReadOnlyList<CharRx> _chars;

    internal IReadOnlyList<CharRx> Chars => _chars ?? [];

    public CharSequenceRx(params int[] chars)
        : this(chars?.Select(p => (CharRx)p).ToArray())
    {
        // calls CharSequenceRx(params CharRx[] chars)
    }

    public CharSequenceRx(params CharRx[] chars)
    {
        _chars = chars ?? [];
    }

    public CharSequenceRx(string sequence)
    {
        var length = sequence?.Length ?? 0;
        var chars = new List<CharRx>(length);

        var i = 0;
        while (i < length)
        {
            var c = char.ConvertToUtf32(sequence, i++);
            chars.Add(c);
            if (c > char.MaxValue)
            {
                i++;
            }
        }
        _chars = chars;
    }

    public bool Equals(CharSequenceRx other)
    {
        return _chars.Count == other._chars.Count && _chars.SequenceEqual(other._chars);
    }

    public override bool Equals(object obj) => obj is CharSequenceRx other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var c in _chars)
        {
            hash.Add(c);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(CharSequenceRx a, CharSequenceRx b) => a.Equals(b);

    public static bool operator !=(CharSequenceRx a, CharSequenceRx b) => !(a == b);

    public static GroupRx operator *(CharSequenceRx @this, Multiplicity multiplicity) => new(multiplicity, @this);

    public static GroupRx operator *(CharSequenceRx @this, int times) => new(new Multiplicity(times), @this);

    public static implicit operator CharSequenceRx(string sequence) => new(sequence);

    public override string ToString() => Pattern;

    public string Pattern => string.Concat(_chars.Select(p => p.Pattern));
}
