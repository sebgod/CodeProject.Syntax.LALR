using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct CharClassRx : IClassRx
    {
        private readonly ISingleCharRx[] _chars;

        public CharClassRx(params int[] chars)
            : this(chars.Select(p => (ISingleCharRx)(CharRx)p).ToArray())
        {
            // calls CharClassRx(params CharRx[] chars)
        }

        public CharClassRx(params ISingleCharRx[] chars)
        {
            _chars = chars;
        }

        public string PatternWithoutBrackets
        {
            get
            {
                return
                    string.Concat(
                        _chars.Select(p =>
                            {
                                var @class = p as IClassRx;
                                return @class != null ? @class.PatternWithoutBrackets : p.Pattern;
                            }));
            }
        }

        public string Pattern
        {
            get { return string.Format("[{0}]", PatternWithoutBrackets); }
        }

        public override string ToString()
        {
            return Pattern;
        }
    }
}
