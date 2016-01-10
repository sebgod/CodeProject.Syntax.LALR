using System;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharSequenceRx : IEquatable<CharSequenceRx>, IRx
    {
        private readonly CharRx[] _chars;

        public CharSequenceRx(params int[] chars)
            : this(chars.Select(p => (CharRx)p).ToArray())
        {
            // calls CharSequenceRx(params CharRx[] chars)
        }

        public CharSequenceRx(params CharRx[] chars)
        {
            _chars = chars;
        }

        public bool Equals(CharSequenceRx other)
        {
            return _chars.Length == other._chars.Length && _chars.SequenceEqual(other._chars);
        }

        public override string ToString()
        {
            return Pattern;
        }

        public string Pattern { get { return string.Concat(_chars.Select(p => p.ToString())); } }
    }
}