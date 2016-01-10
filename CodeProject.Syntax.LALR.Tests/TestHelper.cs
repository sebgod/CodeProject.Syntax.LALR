using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Tests
{
    public static class TestHelper
    {
        internal static void CurrentAndLA(this string input, ref int i, out int current, out int la)
        {
            var next = input.Expected(i, out current);
            i += next;
            input.Expected(i, out la);
        }

        private static int Expected(this string input, int i, out int codePoint)
        {
            var next = 0;
            do
            {
                codePoint = i + next >= input.Length
                                ? AsyncLACharIterator.EOF
                                : char.ConvertToUtf32(input, i + next);

                if (codePoint > char.MaxValue)
                {
                    next++;
                }

                if (codePoint == '\r')
                {
                    next++;
                }
                else
                {
                    break;
                }
            } while (true);

            return ++next;
        }

        public static string DisplayUTF8(this int @this)
        {
            switch (@this)
            {
                case '\t':
                    return @"\t";
                case '\r':
                    return @"\r";
                case '\n':
                    return @"\n";
                case -1:
                    return @"EOF";
                case AsyncLACharIterator.ReplacementCodepoint:
                    return @"???";
                default:
                    var asString = char.ConvertFromUtf32(@this);
                    return asString.Length == 2
                               ? string.Format(@"\u{0,-4:x}\u{1,-4:x}", (int)asString[0], (int)asString[1])
                               : asString;
            }
        }
    }
}