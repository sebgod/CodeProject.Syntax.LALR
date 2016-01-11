using System;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharClassRx : IClassRx
    {
        private readonly ISingleCharRx[] _chars;
        private readonly bool _positive;

        public CharClassRx(params int[] chars)
            : this(true, chars.Select(p => (ISingleCharRx)(CharRx)p).ToArray())
        {
            // calls CharClassRx(bool positive, params CharRx[] chars)
        }

        public CharClassRx(bool positive, params int[] chars)
            : this(positive, chars.Select(p => (ISingleCharRx)(CharRx)p).ToArray())
        {
            // calls CharClassRx(bool positive, params CharRx[] chars)
        }

        public CharClassRx(bool positive, params ISingleCharRx[] chars)
        {
            _positive = positive;
            _chars = chars;
        }

        public bool Positive { get { return _positive; } }

        public string PatternWithoutBrackets
        {
            get
            {
                return string.Concat(_chars.Select(ItemToPattern));
            }
        }

        private string ItemToPattern(ISingleCharRx expr)
        {
            var subClass = expr as IClassRx;
            if (subClass != null)
            {
                if (Positive != subClass.Positive)
                {
                    throw new ArgumentException("Cannot mix positive and negative character classes", "expr");
                }
                return subClass.PatternWithoutBrackets;
            }
            return expr.Pattern;
        }

        public static GroupRx operator *(CharClassRx @this, Multiplicity multiplicity)
        {
            return new GroupRx(multiplicity, @this);
        }

        public string Pattern
        {
            get { return string.Format("[{0}{1}]", _positive ? "" : "^", PatternWithoutBrackets); }
        }

        public override string ToString()
        {
            return Pattern;
        }
    }
}
