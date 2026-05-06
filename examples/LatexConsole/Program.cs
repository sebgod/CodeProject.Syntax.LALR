using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using DIR.Lib.MathLayout;
using LatexGrammar;
using static LatexGrammar.Latex;
using CL = global::Console.Lib;

// Console.Lib brings the `Console` namespace into scope, which would shadow
// System.Console. Alias around it so we can still call System.Console.WriteLine.
using SysConsole = System.Console;

namespace Examples.LatexConsole;

/// <summary>
/// Renders Wikipedia-style LaTeX math through the YAML grammar pipeline (the
/// shared Latex.Grammar library — same generated code Examples.Latex consumes,
/// just a different visitor) into an RGBA pixel buffer using a TeX-lite Box
/// engine, then ships pixels to the terminal as one of three encodings:
/// Sixel (best fidelity, requires sixel-capable terminal), Unicode sextant
/// blocks (2×3 sub-pixels per cell — modern terminals), or plain half-block
/// (universal but coarse).
///
/// CLI: optional flags
///   --auto           auto-detect via DA1 device-attribute query (default).
///                    Sixel if the terminal supports it, sextant otherwise.
///   --sixel          force sixel raster (best fidelity)
///   --sextant        force Unicode sextant-block rendering
///   --halfblock      force half-block fallback (coarsest, universal)
///   --font-size N    pixel size of the base atom font (default depends on
///                    mode: 24 for sixel, 12 for sextant, 10 for half-block)
///   --font PATH      override font path
///   --formula "..."  render only this formula instead of the demo corpus
/// </summary>
internal static class Program
{
    private static readonly string[] DefaultSamples =
    [
        "E = mc^2",
        "e^{i\\pi} + 1 = 0",
        "\\frac{1}{2} + \\frac{1}{3}",
        "\\sqrt{x^2 + y^2}",
        "\\sum_{i=0}^{n} i = \\frac{n(n+1)}{2}",
        "\\int_0^\\infty e^{-x^2} dx = \\frac{\\sqrt{\\pi}}{2}",
    ];

    public static async Task Main(string[] args)
    {
        SysConsole.OutputEncoding = Encoding.UTF8;

        var parsed = ParseArgs(args);
        var fontPath = parsed.FontPath ?? ResolveFont();
        if (string.IsNullOrEmpty(fontPath))
        {
            SysConsole.Error.WriteLine("No usable system font found. Pass --font <path> to a TTF/OTF.");
            Environment.ExitCode = 2;
            return;
        }

        var resolvedMode = parsed.Mode ?? await DetectModeAsync();
        // Per-mode default size: sixel uses 6 sub-pixels per cell so a 24px
        // font is ~4 cell rows tall (roughly the height of a fraction); the
        // Unicode-block paths are coarser and need smaller source pixels to
        // keep the formula proportional to surrounding text.
        float fontSize = parsed.FontSize ?? resolvedMode switch
        {
            CL.BoxRenderMode.Sixel    => 32f,
            CL.BoxRenderMode.Sextant  => 12f,
            CL.BoxRenderMode.HalfBlock=> 10f,
            _ => 12f,
        };

        var visitor = new BoxBuildingVisitor(new BoxStyle(fontPath, fontSize));
        // Phase 5 / slices 5 + 6: parser + lexer both pre-baked.
        var parser = BuildParser(visitor);
        var lexerTable = BuildLexer();

        var samples = parsed.Formula is not null ? [parsed.Formula] : DefaultSamples;

        foreach (var src in samples)
        {
            SysConsole.WriteLine($"  {src}");
            try
            {
                using var lexer = PipeBytesLexer.FromString(src, lexerTable);
                using var tokens = new AsyncLATokenIterator(lexer);

                var result = await parser.ParseInputAsync(tokens, debugger: null);
                if (result.IsError)
                {
                    SysConsole.WriteLine($"    <error: {result}>");
                    continue;
                }

                var box = (Box)result.Content;
                CL.BoxRenderer.Render(box, visitor.Style, resolvedMode, SysConsole.Out);
            }
            catch (Exception ex)
            {
                SysConsole.WriteLine($"    <{ex.GetType().Name}: {ex.Message.Split('\n')[0]}>");
            }
            SysConsole.WriteLine();
        }
    }

    private record ParsedArgs(CL.BoxRenderMode? Mode, float? FontSize, string? FontPath, string? Formula);

    private static ParsedArgs ParseArgs(string[] args)
    {
        CL.BoxRenderMode? mode = null;
        float? fontSize = null;
        string? fontPath = null;
        string? formula = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--auto":      mode = null;                     break;
                case "--ascii":
                case "--halfblock": mode = CL.BoxRenderMode.HalfBlock;  break;
                case "--sextant":   mode = CL.BoxRenderMode.Sextant;    break;
                case "--sixel":     mode = CL.BoxRenderMode.Sixel;      break;
                case "--font-size" when i + 1 < args.Length:
                    fontSize = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--font" when i + 1 < args.Length:
                    fontPath = args[++i];
                    break;
                case "--formula" when i + 1 < args.Length:
                    formula = args[++i];
                    break;
            }
        }
        return new ParsedArgs(mode, fontSize, fontPath, formula);
    }

    /// <summary>
    /// Pick a font with good math/Greek coverage. STIX Two Math is the
    /// gold-standard for math typography (full math glyphs + Greek, OpenType
    /// MATH tables); Cambria (TrueType Collection — works since Fonts.Lib
    /// 0.3.x added TTC support) and Consolas/Menlo/DejaVu are fallbacks
    /// when nothing better is installed. As a last-ditch fallback we defer
    /// to <see cref="DIR.Lib.FontResolver.ResolveSystemFont"/> for the
    /// platform's monospace font.
    /// </summary>
    private static string ResolveFont()
    {
        string[] candidates;
        if (OperatingSystem.IsWindows())
            candidates =
            [
                @"C:\Windows\Fonts\STIXTwoMath-Regular.otf",
                @"C:\Windows\Fonts\cambria.ttc",
                @"C:\Windows\Fonts\consola.ttf",
                @"C:\Windows\Fonts\cour.ttf",
            ];
        else if (OperatingSystem.IsMacOS())
            candidates =
            [
                "/Library/Fonts/STIXTwoMath-Regular.otf",
                "/System/Library/Fonts/Menlo.ttc",
                "/System/Library/Fonts/Monaco.dfont",
            ];
        else
            candidates =
            [
                "/usr/share/fonts/opentype/stix/STIXTwoMath-Regular.otf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
            ];

        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }
        return DIR.Lib.FontResolver.ResolveSystemFont();
    }

    /// <summary>
    /// Auto-detect the rendering mode. Constructs a Console.Lib
    /// <see cref="CL.VirtualTerminal"/> and calls <c>InitAsync</c>, which
    /// fires a DA1 (Device Attributes 1) query at the terminal and reads
    /// back its declared capabilities. If the terminal advertises Sixel,
    /// we use it; otherwise sextants — works on every modern terminal that
    /// supports Unicode 13 block glyphs (Windows Terminal, iTerm2, kitty,
    /// modern GNOME Terminal, mintty, etc.).
    ///
    /// Pipe-redirected stdout returns false from HasSixelSupport, so we
    /// also fall back to sextants in that case (still legible as raw text).
    /// If the DA1 query fails entirely (e.g. no TTY attached), we treat
    /// that as "no sixel" and return Sextant.
    /// </summary>
    private static async Task<CL.BoxRenderMode> DetectModeAsync()
    {
        try
        {
            await using var term = new CL.VirtualTerminal();
            await term.InitAsync();
            return term.HasSixelSupport ? CL.BoxRenderMode.Sixel : CL.BoxRenderMode.Sextant;
        }
        catch
        {
            return CL.BoxRenderMode.Sextant;
        }
    }
}
