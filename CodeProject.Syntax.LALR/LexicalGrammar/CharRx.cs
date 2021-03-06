﻿using System;
using System.Text.RegularExpressions;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharRx : IEquatable<CharRx>, ISingleCharRx, IComparable<CharRx>
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

        public int CompareTo(CharRx other)
        {
            return _codepoint < other._codepoint ? -1 : _codepoint > other._codepoint ? 1 : 0;
        }

        public override bool Equals(object obj)
        {
            return obj is CharRx && Equals((CharRx) obj);
        }

        public override int GetHashCode()
        {
            return _codepoint;
        }

        public static bool operator ==(CharRx a, CharRx b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CharRx a, CharRx b)
        {
            return !(a == b);
        }

        public static bool operator <(CharRx a, CharRx b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(CharRx a, CharRx b)
        {
            return a.CompareTo(b) > 0;
        }

        public static GroupRx operator *(CharRx @this, Multiplicity multiplicity)
        {
            return new GroupRx(multiplicity, @this);
        }

        public static GroupRx operator *(CharRx @this, int times)
        {
            return new GroupRx(new Multiplicity(times), @this);
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
                      : Regex.Escape(char.ConvertFromUtf32(_codepoint));
            }
        }

        public string PatternInsideClass
        {
            get
            {
                if (_codepoint > char.MaxValue)
                {
                    return string.Format(@"\U{0}", _codepoint.ToString("X8"));
                }
                switch (_codepoint)
                {
                    case '^': return @"\^";
                    case '-': return @"\-";
                    case '\\': return @"\\";
                    default: return char.ConvertFromUtf32(_codepoint);
                }
            }
        }
    }
}
