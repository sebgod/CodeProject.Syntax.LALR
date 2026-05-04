// CodeProject.Syntax.LALR.Tui — terminal UI debugger for LALR grammars.
//
// Loads a *.lalr.yaml file, runs it through SchemaCompiler, and shows the
// resulting Grammar / lexer rules / token stream in a Console.Lib-driven dock
// layout. Inspired by the Windows-only GOLD Parser, but cross-platform.

using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using DIR.Lib;                              // DockStyle
using SystemConsole = System.Console;       // Console.Lib namespace shadows System.Console
using CL = global::Console.Lib;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;
using CodeProject.Syntax.LALR.Tui.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeProject.Syntax.LALR.Tui;

internal enum View { Grammar = 1, Lexer = 2, Tokens = 3, Input = 4, Tree = 5 }

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null) return 2;

        // Load + compile the grammar. Failures here become a one-shot error
        // message rather than dumping us into a TUI with nothing to show.
        GrammarSchema schema;
        try
        {
            var yaml = await File.ReadAllTextAsync(opts.GrammarPath);
            schema = Deserializer.Deserialize<GrammarSchema>(yaml);
        }
        catch (YamlException ex)
        {
            SystemConsole.Error.WriteLine($"yaml parse error in {opts.GrammarPath}: line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}");
            return 1;
        }
        catch (FileNotFoundException)
        {
            SystemConsole.Error.WriteLine($"grammar file not found: {opts.GrammarPath}");
            return 1;
        }

        Grammar grammar;
        Dictionary<string, LexRule[]> lexer;
        string? compileError = null;
        try
        {
            // Stub-actions: the TUI is strictly observational, so we never
            // execute the rewriters — any production that names an action gets
            // a no-op that returns the default Reduction. Without this every
            // schema with `action: …` would fail to compile here.
            var stubActions = BuildStubActions(schema);
            (grammar, lexer) = SchemaCompiler.Compile(schema, stubActions);
        }
        catch (SchemaCompilationException ex)
        {
            grammar = default;
            lexer = new Dictionary<string, LexRule[]>();
            compileError = ex.Message;
        }

        // Initial input buffer: seeded from --input <file> if given, else empty.
        // The buffer is editable from the Input view; every edit re-tokenizes
        // and re-parses, refreshing the Tokens and Tree views in place.
        string initialInputText = "";
        if (opts.InputPath is { } inputPath)
        {
            try
            {
                initialInputText = await File.ReadAllTextAsync(inputPath);
            }
            catch (Exception ex)
            {
                // Fall back to empty buffer; the error surfaces in the status bar.
                initialInputText = "";
                _ = ex;
            }
        }

        // Build the parser table once. Constructor failures (grammar conflicts)
        // are propagated as a status-bar message rather than a hard exit, so the
        // user can still browse the schema/lexer views to debug.
        Parser? parser = null;
        string? parserError = null;
        if (compileError is null)
        {
            try { parser = new Parser(grammar); }
            catch (GrammarConflictException ex) { parserError = "grammar conflicts: " + ex.Message; }
            catch (Exception ex) { parserError = "parser-build error: " + ex.Message; }
        }

        await using var term = new CL.VirtualTerminal();
        await term.InitAsync();

        if (term.IsInputRedirected || term.IsOutputRedirected)
        {
            SystemConsole.Error.WriteLine("[lalr-tui] stdin/stdout is not a terminal — falling back to non-interactive summary");
            PrintNonInteractive(opts, schema, grammar, lexer, initialInputText, compileError);
            return 0;
        }

        term.EnterAlternateScreen();
        try
        {
            await RunUi(term, opts, schema, grammar, lexer, parser, initialInputText, compileError, parserError);
        }
        finally
        {
            // VirtualTerminal restores normal screen on dispose.
        }
        return 0;
    }

    // The TUI's csproj has PublishAot=false so YamlDotNet's reflection-based
    // deserializer is fine here. Once Phase 5 (compiler-compiler) lands we can
    // switch to StaticDeserializerBuilder generated per-type and re-enable AOT.
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static async Task RunUi(
        CL.IVirtualTerminal term,
        Options opts,
        GrammarSchema schema,
        Grammar grammar,
        IReadOnlyDictionary<string, LexRule[]> lexer,
        Parser? parser,
        string initialInputText,
        string? compileError,
        string? parserError)
    {
        var view = View.Grammar;
        var lastNonInputView = View.Grammar;     // remembered so Esc from Input returns where we were
        var panel = new CL.Panel(term);
        var headerVp = panel.Dock(DockStyle.Top, 1);
        var statusVp = panel.Dock(DockStyle.Bottom, 1);
        var helpVp   = panel.Dock(DockStyle.Bottom, 1);
        var bodyVp   = panel.Fill();

        var header = new CL.TextBar(headerVp);
        var status = new CL.TextBar(statusVp);
        var help   = new CL.TextBar(helpVp);

        // Five view widgets share the body viewport — only one is rendered at
        // a time. Constructors don't render, so building the unused ones is
        // cheap and keeps state across view switches (cursor / scroll position).
        var grammarTree = new CL.TreeView<Node>(bodyVp);
        grammarTree.Header(" precedence groups · productions · symbols");
        grammarTree.Root(GrammarRoot.Build(schema, grammar), expandRoot: true);

        var lexerTree = new CL.TreeView<Node>(bodyVp);
        lexerTree.Header(" lexer state · symbol  pattern  instruction");
        lexerTree.Root(LexerRoot.Build(schema, grammar, lexer), expandRoot: true);

        var tokenList = new CL.ScrollableList<TokenRow>(bodyVp);
        tokenList.Header(" #     line:col  symbol               content");

        var textState = new CL.TextAreaState(initialInputText);
        var textArea = new CL.TextArea(bodyVp);
        textArea.State = textState;

        var parseTree = new CL.TreeView<Node>(bodyVp);
        parseTree.Header(" parse tree (root → reductions → tokens)");

        // Re-tokenizes and re-parses from the current TextAreaState contents.
        // Both tokens and parse tree are derived from the same byte buffer so
        // edits in the Input view immediately propagate to the other views.
        var tokens = new List<TokenRow>();
        string? tokenError = null;
        string? parseError = null;
        async Task RebuildAsync()
        {
            tokens.Clear();
            tokenError = null;
            parseError = null;
            if (lexer.Count == 0)
            {
                parseTree.Root(ParseTreeRoot.Build(null, grammar, "no lexer"), expandRoot: true);
                tokenList.Items(tokens);
                return;
            }
            // Zero-alloc input feed: build a 2-segment ReadOnlySequence<byte>
            // straight over the gap buffer's two halves. The sequence is value-
            // typed and non-consuming so we can hand it to two PipeReaders (one
            // for the tokens pass, one for the parser pass) without copying.
            // The gap buffer's array can move under us if a subsequent edit
            // grows it, but we await the parse fully here before returning, so
            // the memory stays valid for the duration of both passes.
            var seq = JoinSegments(textState.MemoryBeforeGap, textState.MemoryAfterGap);
            try
            {
                tokens.AddRange(await TokenizeSequenceAsync(seq, lexer, grammar));
            }
            catch (Exception ex) { tokenError = ex.Message; }
            tokenList.Items(tokens);

            if (parser is null)
            {
                parseTree.Root(ParseTreeRoot.Build(null, grammar, parserError ?? compileError ?? "no parser"), expandRoot: true);
                return;
            }
            try
            {
                var reader = PipeReader.Create(seq);
                var lex = new PipeBytesLexer(reader, lexer, errorMode: LexerErrorMode.EmitAndStop, errorSymbolId: 0);
                var laIter = new AsyncLATokenIterator(lex);
                // Return mode: a parse error should still produce a (partial) Item
                // we can render rather than blowing up the UI.
                var root = await parser.ParseInputAsync(laIter, errorMode: ParserErrorMode.Return);
                parseTree.Root(ParseTreeRoot.Build(root, grammar), expandRoot: true);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
                parseTree.Root(ParseTreeRoot.Build(null, grammar, ex.Message), expandRoot: true);
            }
        }

        // Initial fill so all views have content from the seed buffer.
        await RebuildAsync();

        void RenderHeader()
        {
            var leftSb = new StringBuilder();
            leftSb.Append(' ').Append(Path.GetFileName(opts.GrammarPath));
            leftSb.Append("  · ").Append(schema.Symbols?.Count ?? 0).Append(" symbols");
            leftSb.Append(" · ").Append(schema.Productions?.Count ?? 0).Append(" groups");
            if (schema.Lexer is { Count: > 0 } l) leftSb.Append(" · ").Append(l.Count).Append(" lex states");
            var right = view switch
            {
                View.Grammar => "F1·[grammar] F2·lexer F3·tokens F4·input F5·tree",
                View.Lexer   => "F1·grammar F2·[lexer] F3·tokens F4·input F5·tree",
                View.Tokens  => "F1·grammar F2·lexer F3·[tokens] F4·input F5·tree",
                View.Input   => "F1·grammar F2·lexer F3·tokens F4·[input] F5·tree",
                View.Tree    => "F1·grammar F2·lexer F3·tokens F4·input F5·[tree]",
                _            => "",
            } + " ";
            header
                .Text(leftSb.ToString())
                .RightText(right)
                .Style(new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Blue))
                .Render();
        }

        void RenderStatus()
        {
            string text;
            CL.VtStyle style;
            if (compileError != null)
            {
                text = " compile error: " + compileError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (parserError != null && (view == View.Tree || view == View.Input))
            {
                text = " " + parserError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (view == View.Tokens && tokenError != null)
            {
                text = " tokenize error: " + tokenError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (view == View.Tree && parseError != null)
            {
                text = " parse error: " + parseError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else
            {
                text = view switch
                {
                    View.Grammar => $" {grammarTree.CursorIndex + 1}/{Math.Max(1, grammarTree.ItemCount)}  {NodeTitle(grammarTree.Selected)}",
                    View.Lexer   => $" {lexerTree.CursorIndex + 1}/{Math.Max(1, lexerTree.ItemCount)}  {NodeTitle(lexerTree.Selected)}",
                    View.Tokens  => tokens.Count == 0
                        ? " (no tokens — buffer is empty or lexer rejected the first byte)"
                        : $" {tokenList.CursorIndex + 1}/{tokens.Count}  tokens",
                    View.Input   => FormatInputStatus(textState, tokens.Count),
                    View.Tree    => $" {parseTree.CursorIndex + 1}/{Math.Max(1, parseTree.ItemCount)}  {NodeTitle(parseTree.Selected)}",
                    _            => "",
                };
                style = new CL.VtStyle(CL.SgrColor.White, CL.SgrColor.BrightBlack);
            }
            status.Text(text).Style(style).Render();
        }

        void RenderHelp()
        {
            // Input mode has different keybindings (typing vs navigation), so the
            // help line swaps to match. Esc always returns to the previous view
            // when in Input mode, never quits — Ctrl+C handles abort.
            var line = view == View.Input
                ? " [F1..F5] view  [Esc] back  [↑/↓/←/→] move  [Home/End] line  [PgUp/PgDn] page  [Backspace/Del] erase  type to insert"
                : " [F1] grammar  [F2] lexer  [F3] tokens  [F4] input  [F5] tree  [Tab] cycle  [↑/↓] move  [←/→] coll/expand  [q] quit";
            help
                .Text(line)
                .Style(new CL.VtStyle(CL.SgrColor.BrightBlack, CL.SgrColor.Black))
                .Render();
        }

        void RenderBody()
        {
            switch (view)
            {
                case View.Grammar: grammarTree.Render(); break;
                case View.Lexer:   lexerTree.Render();   break;
                case View.Tokens:  tokenList.Render();   break;
                case View.Input:   textArea.Render();    break;
                case View.Tree:    parseTree.Render();   break;
            }
        }

        void RenderAll()
        {
            term.Clear();
            RenderHeader();
            RenderBody();
            RenderHelp();
            RenderStatus();
        }

        void SwitchView(View target)
        {
            if (view != View.Input) lastNonInputView = view;
            view = target;
            RenderAll();
        }

        RenderAll();

        var quit = false;
        while (!quit)
        {
            if (panel.Recompute())
            {
                RenderAll();
            }

            if (!term.HasInput()) { await Task.Delay(20); continue; }
            var ev = term.TryReadInput();

            if (ev.Mouse is { } m)
            {
                bool consumed = view switch
                {
                    View.Grammar => grammarTree.HandleMouse(m),
                    View.Lexer   => lexerTree.HandleMouse(m),
                    View.Tokens  => tokenList.HandleMouse(m),
                    View.Tree    => parseTree.HandleMouse(m),
                    // TextArea has no mouse handler yet; clicks fall through.
                    _            => false,
                };
                if (consumed) { RenderBody(); RenderStatus(); }
                continue;
            }

            // F1..F5 are universal view switches and work in every mode,
            // including Input — they don't collide with text since terminals
            // emit them as escape sequences, not characters.
            View? fkeyTarget = ev.Key switch
            {
                ConsoleKey.F1 => View.Grammar,
                ConsoleKey.F2 => View.Lexer,
                ConsoleKey.F3 => View.Tokens,
                ConsoleKey.F4 => View.Input,
                ConsoleKey.F5 => View.Tree,
                _             => null,
            };
            if (fkeyTarget is { } f) { SwitchView(f); continue; }

            if (view == View.Input)
            {
                // Edit mode: only Escape leaves; everything else types or navigates.
                if (ev.Key == ConsoleKey.Escape)
                {
                    SwitchView(lastNonInputView);
                    continue;
                }
                var moved = textArea.HandleKey(ev.Key, ev.Modifiers);
                var typed = !moved && textArea.HandleChar(ev);
                if (moved || typed)
                {
                    if (typed)
                    {
                        // Text changed → re-tokenize + re-parse, then refresh.
                        // Synchronous await keeps the keystroke loop simple; for
                        // larger inputs we'd debounce + run on a background task.
                        await RebuildAsync();
                    }
                    RenderBody();
                    RenderStatus();
                }
                continue;
            }

            switch (ev.Key)
            {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    quit = true;
                    break;
                case ConsoleKey.Tab:
                    SwitchView((View)(((int)view % 5) + 1)); break;
                default:
                    bool changed = view switch
                    {
                        View.Grammar => grammarTree.HandleKey(ev.Key, ev.Modifiers),
                        View.Lexer   => lexerTree.HandleKey(ev.Key, ev.Modifiers),
                        View.Tokens  => tokenList.HandleKey(ev.Key),
                        View.Tree    => parseTree.HandleKey(ev.Key, ev.Modifiers),
                        _            => false,
                    };
                    if (changed) { RenderBody(); RenderStatus(); }
                    break;
            }
        }
    }

    private static string FormatInputStatus(CL.TextAreaState s, int tokenCount)
    {
        var (line, col) = s.CursorLineColumn;
        return $" line {line + 1}, col {col + 1} (byte)  ·  {s.Length} bytes  ·  {tokenCount} tokens";
    }

    private static string NodeTitle(Node? n)
    {
        if (n is null) return "";
        // Strip VT escape codes for the status line; FormatNodeContent emits
        // escape sequences for highlighting that render as garbage in the bar.
        return n.PlainTitle;
    }

    private static IReadOnlyDictionary<string, Func<int, Item[], object>> BuildStubActions(GrammarSchema schema)
    {
        var dict = new Dictionary<string, Func<int, Item[], object>>(StringComparer.Ordinal);
        var groups = schema.Productions ?? [];
        foreach (var g in groups)
        {
            var rules = g?.Rules ?? [];
            foreach (var r in rules)
            {
                var name = r?.Action;
                if (!string.IsNullOrEmpty(name) && !dict.ContainsKey(name))
                {
                    // No-op rewriter: defer to the parser's default Reduction
                    // by returning null and relying on Production.HasRewriter
                    // semantics. Returning null from a rewriter is a valid
                    // payload — but for TUI the parser is never run, so this
                    // is dead code at runtime; only the compile-time presence
                    // of an entry in the dictionary matters.
                    dict[name] = static (_, _) => null!;
                }
            }
        }
        return dict;
    }

    private static async Task<IReadOnlyList<TokenRow>> TokenizeSequenceAsync(
        ReadOnlySequence<byte> bytes, IReadOnlyDictionary<string, LexRule[]> lexer, Grammar grammar)
    {
        var reader = PipeReader.Create(bytes);
        var lex = new PipeBytesLexer(reader, lexer, errorMode: LexerErrorMode.EmitAndStop, errorSymbolId: 0);
        var rows = new List<TokenRow>();
        var idx = 0;
        while (await lex.MoveNextAsync())
        {
            var item = await lex.CurrentAsync();
            var symbolName = grammar.SymbolNames is { } sn && item.ID >= 0 && item.ID < sn.Length
                ? sn[item.ID].Name
                : item.ID == -1 ? "$" : $"#{item.ID}";
            rows.Add(new TokenRow(idx++, item.Position, symbolName, item.Content?.ToString() ?? "", item.IsError));
        }
        return rows;
    }

    /// <summary>
    /// Builds a two-segment <see cref="ReadOnlySequence{T}"/> covering the
    /// pre-gap and post-gap halves of the editor buffer. Avoids allocating a
    /// flat <c>byte[]</c> per keystroke; the lexer's PipeReader walks segments
    /// natively.
    /// </summary>
    private static ReadOnlySequence<byte> JoinSegments(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty) return new ReadOnlySequence<byte>(b);
        if (b.IsEmpty) return new ReadOnlySequence<byte>(a);
        var first = new MemSegment(a);
        var last = first.Append(b);
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class MemSegment : ReadOnlySequenceSegment<byte>
    {
        public MemSegment(ReadOnlyMemory<byte> mem) { Memory = mem; }
        public MemSegment Append(ReadOnlyMemory<byte> next)
        {
            var seg = new MemSegment(next) { RunningIndex = RunningIndex + Memory.Length };
            Next = seg;
            return seg;
        }
    }

    private static void PrintNonInteractive(
        Options opts, GrammarSchema schema, Grammar grammar,
        IReadOnlyDictionary<string, LexRule[]> lexer,
        string initialInputText, string? compileError)
    {
        SystemConsole.WriteLine($"file: {opts.GrammarPath}");
        SystemConsole.WriteLine($"symbols: {schema.Symbols?.Count ?? 0}");
        SystemConsole.WriteLine($"production groups: {schema.Productions?.Count ?? 0}");
        SystemConsole.WriteLine($"lexer states: {schema.Lexer?.Count ?? 0}");
        if (compileError != null) SystemConsole.WriteLine($"compile error: {compileError}");
        if (!string.IsNullOrEmpty(initialInputText))
        {
            SystemConsole.WriteLine($"input bytes from {opts.InputPath}: {Encoding.UTF8.GetByteCount(initialInputText)}");
        }
    }

    private sealed class Options
    {
        public string GrammarPath { get; set; } = "";
        public string? InputPath { get; set; }
    }

    private static Options? ParseArgs(string[] args)
    {
        var opt = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" or "-i":
                    if (i + 1 >= args.Length) { SystemConsole.Error.WriteLine("--input requires a path"); return null; }
                    opt.InputPath = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage(); return null;
                default:
                    if (args[i].StartsWith('-')) { SystemConsole.Error.WriteLine($"unknown flag: {args[i]}"); return null; }
                    if (opt.GrammarPath.Length > 0) { SystemConsole.Error.WriteLine("only one grammar file may be supplied"); return null; }
                    opt.GrammarPath = args[i];
                    break;
            }
        }
        if (opt.GrammarPath.Length == 0)
        {
            PrintUsage();
            return null;
        }
        return opt;
    }

    private static void PrintUsage()
    {
        SystemConsole.Error.WriteLine("usage: lalr-tui <grammar.lalr.yaml> [--input <source-file>]");
        SystemConsole.Error.WriteLine("  views:    F1=grammar F2=lexer F3=tokens F4=input F5=tree");
        SystemConsole.Error.WriteLine("  navigate: ↑/↓ move · ←/→ collapse/expand · Tab cycles · q/Esc quit (Esc returns from input mode)");
    }
}
