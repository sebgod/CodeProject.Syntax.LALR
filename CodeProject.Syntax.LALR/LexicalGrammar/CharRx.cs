using System;
using System.Text.RegularExpressions;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharRx : IEquatable<CharRx>, ISingleCharRx
    {
        private readonly int _codepoint;

        public CharRx(int codepoint)
        {
            _codepoint = codepoint;
        }

        public static implicit operator CharRx(int codepoint)
        {
            return new CharRx(codepoint);
        }
        
        public static CharSequenceRx operator +(CharRx a, CharRx b)
        {
            return new CharSequenceRx(a, b);
        }

        public bool Equals(CharRx other)
        {
            return _codepoint == other._codepoint;
        }

        public override bool Equals(object obj)
        {
            return obj is CharRx && Equals((CharRx) obj);
        }

        public override int GetHashCode()
        {
            return _codepoint;
        }

        public override string ToString()
        {
            return Pattern;
        }

        public string Pattern
        {
            get
            {
                return _codepoint > char.MaxValue
                      ? string.Format(@"\U{0}", _codepoint.ToString("X8"))
                      : Regex.Escape(new string((char)_codepoint, 1));
            }
        }
    }
}
