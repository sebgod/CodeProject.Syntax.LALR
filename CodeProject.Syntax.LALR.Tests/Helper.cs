using System;
using System.Collections.Generic;
using System.Globalization;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Tests;

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
                ? PipeRuneIterator.EOF
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

    public static string DisplayUTF8(this int @this) => @this switch
    {
        '\t' => @"\t",
        '\r' => @"\r",
        '\n' => @"\n",
        -1 => "EOF",
        PipeRuneIterator.ReplacementCodepoint => "???",
        _ => DisplayUtf8Codepoint(@this),
    };

    private static string DisplayUtf8Codepoint(int codepoint)
    {
        var asString = char.ConvertFromUtf32(codepoint);
        return asString.Length == 2
            ? string.Format(CultureInfo.InvariantCulture, @"\u{0,-4:x}\u{1,-4:x}", (int)asString[0], (int)asString[1])
            : asString;
    }

    public static IList<IRx> Items(params IRx[] exprs) => new PrintableList<IRx>(exprs);

    public static IList<ISingleCharRx> Chars(params ISingleCharRx[] charExprs) => new PrintableList<ISingleCharRx>(charExprs);

    public static IList<ISingleCharRx> Chars(params int[] chars)
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
