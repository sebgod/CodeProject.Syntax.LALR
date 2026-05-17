using System;
using System.Collections.Generic;
using System.Linq;

namespace LALR.CC.LexicalGrammar;

public readonly struct CharClassRx : IClassRx
{
    private readonly ISingleCharRx[] _chars;

    public CharClassRx(int first, params int[] rest)
        : this(true, first, rest)
    {
        // calls CharClassRx(bool positive, int first, params int[] rest)
    }

    public CharClassRx(bool positive, int first, params int[] rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        Positive = positive;
        _chars = new ISingleCharRx[1 + rest.Length];
        _chars[0] = (CharRx)first;
        for (var i = 0; i < rest.Length; i++)
        {
            _chars[i + 1] = (CharRx)rest[i];
        }
    }

    public CharClassRx(bool positive, params ISingleCharRx[] chars)
    {
        ArgumentNullException.ThrowIfNull(chars);
        if (chars.Length == 0)
        {
            throw new ArgumentException("Cannot create an empty set", nameof(chars));
        }

        Positive = positive;
        _chars = chars;
    }

    public bool Positive { get; }

    internal IReadOnlyList<ISingleCharRx> Chars => _chars;

    private string ItemToPattern(ISingleCharRx expr)
    {
        if (expr is IClassRx subClass && Positive != subClass.Positive)
        {
            throw new ArgumentException("Cannot mix positive and negative character classes", nameof(expr));
        }
        return expr.PatternInsideClass;
    }

    public static GroupRx operator *(CharClassRx @this, Multiplicity multiplicity) => new(multiplicity, @this);

    public static GroupRx operator *(CharClassRx @this, int times) => new(new Multiplicity(times), @this);

    public string Pattern => $"[{(Positive ? "" : "^")}{PatternInsideClass}]";

    public string PatternInsideClass => string.Concat(_chars.Select(ItemToPattern));

    public override string ToString() => Pattern;
}
