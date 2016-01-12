using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharRangeRx : IEquatable<CharRangeRx>, ISingleCharRx
    {
        private readonly CharRx _from;
        private readonly CharRx _to;

        public CharRangeRx(CharRx from, CharRx to)
        {
            if (to < from)
            {
                throw new ArgumentException("To is smaller than from: " + from + "-" + to, "to");
            }

            _from = from;
            _to = to;
        }

        public bool Equals(CharRangeRx other)
        {
            return _from == other._from && _to == other._to;
        }

        public override bool Equals(object obj)
        {
            return obj is CharRangeRx && Equals((CharRangeRx) obj);
        }

        public override int GetHashCode()
        {
            return (_to.GetHashCode() << 11) ^ _from.GetHashCode();
        }

        public static bool operator ==(CharRangeRx a, CharRangeRx b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CharRangeRx a, CharRangeRx b)
        {
            return !(a == b);
        }

        public string Pattern
        {
            get { throw new InvalidOperationException("Char ranges can only be used inside classes"); }
        }

        public string PatternInsideClass
        {
            get { return _from == _to ? _from.Pattern : string.Format("{0}-{1}", _from.Pattern, _to.Pattern); }
        }

        public override string ToString()
        {
            return PatternInsideClass;
        }
    }
}
