using System;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharClassRx : IClassRx
    {
        private readonly ISingleCharRx[] _chars;
        private readonly bool _positive;

        public CharClassRx(int first, params int[] rest)
            : this(true, first, rest)
        {
            // calls CharClassRx(bool positive, int first, params int[] rest)
        }

        public CharClassRx(bool positive, int first, params int[] rest)
        {
            if (rest == null)
            {
                throw new ArgumentNullException("rest");
            }
            _positive = positive;
            _chars = new ISingleCharRx[1 + rest.Length];
            _chars[0] = (CharRx)first;
            for (var i = 0; i < rest.Length; i++)
            {
                _chars[i + 1] = (CharRx)rest[i];
            }
        }

        public CharClassRx(bool positive, params ISingleCharRx[] chars)
        {
            if (chars == null)
            {
                throw new ArgumentNullException("chars");
            }
            if (chars.Length == 0)
            {
                throw new ArgumentException("Cannot create an empty set", "chars");
            }

            _positive = positive;
            _chars = chars;
        }

        public bool Positive { get { return _positive; } }

        private string ItemToPattern(ISingleCharRx expr)
        {
            var subClass = expr as IClassRx;
            if (subClass != null && Positive != subClass.Positive)
            {
                throw new ArgumentException("Cannot mix positive and negative character classes", "expr");
            }
            return expr.PatternInsideClass;
        }

        public static GroupRx operator *(CharClassRx @this, Multiplicity multiplicity)
        {
            return new GroupRx(multiplicity, @this);
        }

        public static GroupRx operator *(CharClassRx @this, int times)
        {
            return new GroupRx(new Multiplicity(times), @this);
        }

        public string Pattern
        {
            get { return string.Format("[{0}{1}]", _positive ? "" : "^", PatternInsideClass); }
        }

        public string PatternInsideClass
        {
            get { return string.Concat(_chars.Select(ItemToPattern)); }
        }

        public override string ToString()
        {
            return Pattern;
        }
    }
}
