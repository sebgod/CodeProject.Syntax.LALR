using System;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Tests
{
    public static class Helper
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
                    var asString = Char.ConvertFromUtf32(@this);
                    return asString.Length == 2
                               ? String.Format(@"\u{0,-4:x}\u{1,-4:x}", (int)asString[0], (int)asString[1])
                               : asString;
            }
        }

        public static object Items(params IRx[] exprs)
        {
            return new PrintableList<IRx>(exprs);
        }

        public static object Chars(params ISingleCharRx[] charExprs)
        {
            return new PrintableList<ISingleCharRx>(charExprs);
        }

        public static object Chars(params int[] chars)
        {
            var count = chars.Length;
            var array = new PrintableList<ISingleCharRx>(count);

            for (var i = 0; i < count; i++)
            {
                array.Add(new CharRx(chars[i]));
            }
            return array;
        }
    }
}