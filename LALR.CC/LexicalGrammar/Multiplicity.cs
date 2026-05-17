using System;

namespace LALR.CC.LexicalGrammar;

public readonly struct Multiplicity : IEquatable<Multiplicity>, IRx
{
    public static readonly Multiplicity ZeroOrMore = new(0, -1);
    public static readonly Multiplicity ZeroOrOnce = new(0, 1);
    public static readonly Multiplicity OneOrMore = new(1, -1);
    public static readonly Multiplicity Once = new(1);

    private readonly int _from;
    private readonly int _to;

    public Multiplicity(int times)
        : this(times, times)
    {
        // call Multiplicity(int from, int to)
    }

    public Multiplicity(int from, int to)
    {
        if (from < 0)
        {
            throw new ArgumentException("From must be >= 0: " + from, nameof(from));
        }
        if (to != -1 && to < from)
        {
            throw new ArgumentException("To must be >= to, if not unbound: " + to, nameof(to));
        }

        _from = from;
        _to = to;
    }

    internal int From => _from;
    internal int To => _to;

    public bool Equals(Multiplicity other)
    {
        return _from == other._from && _to == other._to;
    }

    public override bool Equals(object obj)
    {
        return obj is Multiplicity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (_to.GetHashCode() << 11) ^ _from.GetHashCode();
    }

    public static bool operator ==(Multiplicity a, Multiplicity b) => a.Equals(b);

    public static bool operator !=(Multiplicity a, Multiplicity b) => !(a == b);

    public string Pattern
    {
        get
        {
            if (_from == _to)
            {
                return _from switch
                {
                    1 => "",
                    _ => $"{{{_from}}}",
                };
            }

            return _to switch
            {
                -1 => _from switch
                {
                    0 => "*",
                    1 => "+",
                    _ => $"{{{_from},}}",
                },
                1 => "?",
                _ => $"{{{_from},{_to}}}",
            };
        }
    }

    public override string ToString()
    {
        return Pattern;
    }
}
