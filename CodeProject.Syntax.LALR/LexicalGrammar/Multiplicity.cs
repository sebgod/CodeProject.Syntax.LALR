using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct Multiplicity : IEquatable<Multiplicity>, IRx
    {
        public static readonly Multiplicity ZeroOrMore = new Multiplicity(0, -1);
        public static readonly Multiplicity ZeroOrOnce = new Multiplicity(0, 1);
        public static readonly Multiplicity OneOrMore = new Multiplicity(1, -1);
        public static readonly Multiplicity Once = new Multiplicity(1);

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
                throw new ArgumentException("From must be >= 0: " + from, "from");
            }
            if (to != -1 && to < from)
            {
                throw new ArgumentException("To must be >= to, if not unbound: " + to, "to");   
            }

            _from = from;
            _to = to;
        }

        public bool Equals(Multiplicity other)
        {
            return _from == other._from && _to == other._to;
        }

        public override bool Equals(object obj)
        {
            return obj is Multiplicity && Equals((Multiplicity) obj);
        }

        public override int GetHashCode()
        {
            return (_to.GetHashCode() << 11) ^ _from.GetHashCode();
        }

        public static bool operator ==(Multiplicity a, Multiplicity b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Multiplicity a, Multiplicity b)
        {
            return !(a == b);
        }

        public string Pattern
        {
            get {
                if (_from == _to)
                {
                    switch (_from)
                    {
                        case 1:
                            return "";
                        default:
                            return string.Format("{{{0}}}", _from);
                    }
                }

                switch (_to)
                {
                    case -1:
                        switch (_from)
                        {
                            case 0:
                                return "*";
                            case 1:
                                return "+";
                            default:
                                return string.Format("{{{0},}}", _from);
                        }
                    case 1:
                        return "?";

                    default:
                        return string.Format("{{{0},{1}}}", _from, _to);
                }
            }
        }

        public override string ToString()
        {
            return Pattern;
        }
    }
}
