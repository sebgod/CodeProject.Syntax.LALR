using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharSequenceRx : IEquatable<CharSequenceRx>, IRx
    {
        private readonly IList<CharRx> _chars;

        public CharSequenceRx(params int[] chars)
            : this(chars != null ? chars.Select(p => (CharRx) p).ToArray() : null as CharRx[])
        {
            // calls CharSequenceRx(params CharRx[] chars)
        }

        public CharSequenceRx(params CharRx[] chars)
        {
            _chars = chars ?? new CharRx[0];
        }

        public CharSequenceRx(string sequence)
        {
            var length = sequence != null ? sequence.Length : 0;
            _chars = new List<CharRx>(length);

            var i = 0;
            while (i < length)
            {
                var c = char.ConvertToUtf32(sequence, i++);
                _chars.Add(c);
                if (c > char.MaxValue)
                {
                    i++;
                }
            }
        }

        public bool Equals(CharSequenceRx other)
        {
            return _chars.Count == other._chars.Count && _chars.SequenceEqual(other._chars);
        }

        public static GroupRx operator *(CharSequenceRx @this, Multiplicity multiplicity)
        {
            return new GroupRx(multiplicity, @this);
        }
        public static GroupRx operator *(CharSequenceRx @this, int times)
        {
            return new GroupRx(new Multiplicity(times), @this);
        }

        public static implicit operator CharSequenceRx(string sequence)
        {
            return new CharSequenceRx(sequence);
        }

        public override string ToString()
        {
            return Pattern;
        }

        public string Pattern
        {
            get { return string.Concat(_chars.Select(p => p.Pattern)); }
        }
    }
}