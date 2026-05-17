using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LALR.CC.LexicalGrammar;

public readonly struct CharRx(int codepoint) : IEquatable<CharRx>, ISingleCharRx, IComparable<CharRx>
{
    private readonly int _codepoint = codepoint;

    internal int Codepoint => _codepoint;

    public static implicit operator CharRx(int codepoint) => new(codepoint);

    public static CharSequenceRx operator +(CharRx a, CharRx b) => new(a, b);

    public bool Equals(CharRx other) => _codepoint == other._codepoint;

    public int CompareTo(CharRx other) => _codepoint.CompareTo(other._codepoint);

    public override bool Equals(object obj) => obj is CharRx other && Equals(other);

    public override int GetHashCode() => _codepoint;

    public static bool operator ==(CharRx a, CharRx b) => a.Equals(b);

    public static bool operator !=(CharRx a, CharRx b) => !(a == b);

    public static bool operator <(CharRx a, CharRx b) => a.CompareTo(b) < 0;

    public static bool operator >(CharRx a, CharRx b) => a.CompareTo(b) > 0;

    public static bool operator <=(CharRx a, CharRx b) => a.CompareTo(b) <= 0;

    public static bool operator >=(CharRx a, CharRx b) => a.CompareTo(b) >= 0;

    public static GroupRx operator *(CharRx @this, Multiplicity multiplicity) => new(multiplicity, @this);

    public static GroupRx operator *(CharRx @this, int times) => new(new Multiplicity(times), @this);

    public override string ToString() => Pattern;

    public string Pattern => _codepoint > char.MaxValue
        ? $@"\U{_codepoint.ToString("X8", CultureInfo.InvariantCulture)}"
        : Regex.Escape(char.ConvertFromUtf32(_codepoint));

    public string PatternInsideClass
    {
        get
        {
            if (_codepoint > char.MaxValue)
            {
                return $@"\U{_codepoint.ToString("X8", CultureInfo.InvariantCulture)}";
            }
            return _codepoint switch
            {
                '^' => @"\^",
                '-' => @"\-",
                '\\' => @"\\",
                _ => char.ConvertFromUtf32(_codepoint),
            };
        }
    }
}
