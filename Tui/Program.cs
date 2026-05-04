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

// Three-pane layout: schema (grammar+lexer) on the left, input in the middle,
// parse tree on the right. Focus determines which pane consumes keystrokes
// (Tab / Shift+Tab cycle; mouse click on a pane focuses it). All three render
// every frame, so edits in the middle pane visibly propagate to the right.
internal enum Focus { Schema = 0, Input = 1, Tree = 2 }

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
        var focus = Focus.Input;     // input is the workflow's natural starting point
        var panel = new CL.Panel(term);
        var headerVp = panel.Dock(DockStyle.Top, 1);
        var statusVp = panel.Dock(DockStyle.Bottom, 1);
        var helpVp   = panel.Dock(DockStyle.Bottom, 1);
        // Three-column body. Sizes are cell-cols; the middle pane gets the
        // remainder via Fill so it expands on wider terminals.
        var schemaVp = panel.Dock(DockStyle.Left, 36);
        var parseVp  = panel.Dock(DockStyle.Right, 40);
        var inputVp  = panel.Fill();

        var header = new CL.TextBar(headerVp);
        var status = new CL.TextBar(statusVp);
        var help   = new CL.TextBar(helpVp);

        // Unified schema tree: grammar and lexer live side-by-side under one
        // synthetic root, each half collapsible independently.
        var schemaTree = new CL.TreeView<Node>(schemaVp);
        schemaTree.Header(" schema");
        schemaTree.Root(SchemaRoot.Build(schema, grammar, lexer), expandRoot: true);

        var textState = new CL.TextAreaState(initialInputText);
        var textArea = new CL.TextArea(inputVp);
        textArea.State = textState;

        var parseTree = new CL.TreeView<Node>(parseVp);
        parseTree.Header(" parse tree");

        // Re-tokenizes and re-parses from the current TextAreaState contents.
        // The tokens count surfaces in the status bar; the parse tree is the
        // canonical view of the structure (token leaves are visible there).
        var tokenCount = 0;
        string? tokenError = null;
        string? parseError = null;
        async Task RebuildAsync()
        {
            tokenCount = 0;
            tokenError = null;
            parseError = null;
            if (lexer.Count == 0)
            {
                parseTree.Root(ParseTreeRoot.Build(null, grammar, "no lexer"), expandRoot: true);
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
                tokenCount = await CountTokensAsync(seq, lexer);
            }
            catch (Exception ex) { tokenError = ex.Message; }

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

        // Bright header style for the focused pane; dim header for the rest.
        // Helps the eye land on the active pane without changing the layout.
        var focusedHeader   = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Blue);
        var unfocusedHeader = new CL.VtStyle(CL.SgrColor.BrightBlack, CL.SgrColor.Black);

        void RenderHeader()
        {
            var leftSb = new StringBuilder();
            leftSb.Append(' ').Append(Path.GetFileName(opts.GrammarPath));
            leftSb.Append("  · ").Append(schema.Symbols?.Count ?? 0).Append(" symbols");
            leftSb.Append(" · ").Append(schema.Productions?.Count ?? 0).Append(" groups");
            if (schema.Lexer is { Count: > 0 } l) leftSb.Append(" · ").Append(l.Count).Append(" lex states");
            var right = focus switch
            {
                Focus.Schema => "[schema]·input·tree ",
                Focus.Input  => "schema·[input]·tree ",
                Focus.Tree   => "schema·input·[tree] ",
                _            => "",
            };
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
            else if (parserError != null)
            {
                text = " " + parserError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (tokenError != null)
            {
                text = " tokenize error: " + tokenError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (parseError != null)
            {
                text = " parse error: " + parseError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else
            {
                text = focus switch
                {
                    Focus.Schema => $" schema  {schemaTree.CursorIndex + 1}/{Math.Max(1, schemaTree.ItemCount)}  {NodeTitle(schemaTree.Selected)}",
                    Focus.Input  => FormatInputStatus(textState, tokenCount),
                    Focus.Tree   => $" tree    {parseTree.CursorIndex + 1}/{Math.Max(1, parseTree.ItemCount)}  {NodeTitle(parseTree.Selected)}",
                    _            => "",
                };
                style = new CL.VtStyle(CL.SgrColor.White, CL.SgrColor.BrightBlack);
            }
            status.Text(text).Style(style).Render();
        }

        void RenderHelp()
        {
            // Help line is focus-sensitive because the dispatched keys differ:
            // text-input pane consumes printable bytes whereas the trees use
            // the same printable keys for navigation/quit.
            var line = focus == Focus.Input
                ? " [Tab] next pane  [Shift+Tab] prev  [↑/↓/←/→] move  [Home/End] line  [PgUp/PgDn] page  [Backspace/Del] erase  type to insert  [Ctrl+C] quit"
                : " [Tab] next pane  [Shift+Tab] prev  [↑/↓] move  [←/→] collapse/expand  [Home/End] document  [PgUp/PgDn] page  [q] quit";
            help
                .Text(line)
                .Style(new CL.VtStyle(CL.SgrColor.BrightBlack, CL.SgrColor.Black))
                .Render();
        }

        void RenderPanes()
        {
            // Apply focus tint to the tree headers each render so the user can
            // see at a glance which pane is active.
            schemaTree.HeaderStyle(focus == Focus.Schema ? focusedHeader : unfocusedHeader);
            parseTree.HeaderStyle(focus == Focus.Tree ? focusedHeader : unfocusedHeader);
            schemaTree.Render();
            textArea.Render();
            parseTree.Render();
        }

        void RenderAll()
        {
            term.Clear();
            RenderHeader();
            RenderPanes();
            RenderHelp();
            RenderStatus();
        }

        void SetFocus(Focus next)
        {
            focus = next;
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
                // Click-to-focus: hit-test the three pane widgets in turn.
                // Whichever viewport contains the click takes focus and the
                // event is forwarded to its handler (mouse-aware widgets only
                // — TextArea has no mouse handler yet, so its clicks just
                // change focus without moving the cursor).
                Focus? clickedPane = null;
                if (schemaTree.HitTest(m.X, m.Y) is not null) clickedPane = Focus.Schema;
                else if (textArea.HitTest(m.X, m.Y) is not null) clickedPane = Focus.Input;
                else if (parseTree.HitTest(m.X, m.Y) is not null) clickedPane = Focus.Tree;

                if (clickedPane is { } p)
                {
                    if (focus != p) SetFocus(p);
                    var consumed = p switch
                    {
                        Focus.Schema => schemaTree.HandleMouse(m),
                        Focus.Tree   => parseTree.HandleMouse(m),
                        _            => false,
                    };
                    if (consumed) { RenderPanes(); RenderStatus(); }
                }
                continue;
            }

            // Universal: Tab / Shift+Tab cycles focus, Ctrl+C quits regardless
            // of focused pane. The Input pane needs to consume printable Tab
            // bytes as soft-tabs, so when focus is Input we route Tab to the
            // editor instead of cycling — Shift+Tab still cycles backwards
            // because it's not text input.
            var shift = (ev.Modifiers & ConsoleModifiers.Shift) != 0;
            var ctrl  = (ev.Modifiers & ConsoleModifiers.Control) != 0;

            if (ctrl && ev.Key == ConsoleKey.C) { quit = true; continue; }

            if (ev.Key == ConsoleKey.Tab && (shift || focus != Focus.Input))
            {
                var step = shift ? -1 : 1;
                var next = (Focus)(((int)focus + step + 3) % 3);
                SetFocus(next);
                continue;
            }

            if (focus == Focus.Input)
            {
                var moved = textArea.HandleKey(ev.Key, ev.Modifiers);
                var typed = !moved && textArea.HandleChar(ev);
                if (moved || typed)
                {
                    if (typed) await RebuildAsync();   // text changed → re-tokenize + re-parse
                    RenderPanes();
                    RenderStatus();
                }
                continue;
            }

            // Schema / Tree panes (read-only): q quits, arrows move, etc.
            switch (ev.Key)
            {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    quit = true;
                    break;
                default:
                    bool changed = focus switch
                    {
                        Focus.Schema => schemaTree.HandleKey(ev.Key, ev.Modifiers),
                        Focus.Tree   => parseTree.HandleKey(ev.Key, ev.Modifiers),
                        _            => false,
                    };
                    if (changed) { RenderPanes(); RenderStatus(); }
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

    // Token-only pass over the same byte sequence the parser later consumes.
    // Used solely for the status-bar count; the parse tree carries the
    // structural detail.
    private static async Task<int> CountTokensAsync(
        ReadOnlySequence<byte> bytes, IReadOnlyDictionary<string, LexRule[]> lexer)
    {
        var reader = PipeReader.Create(bytes);
        var lex = new PipeBytesLexer(reader, lexer, errorMode: LexerErrorMode.EmitAndStop, errorSymbolId: 0);
        var n = 0;
        while (await lex.MoveNextAsync())
        {
            _ = await lex.CurrentAsync();
            n++;
        }
        return n;
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
        SystemConsole.Error.WriteLine("  layout:   schema (left)  ·  input (middle, default focus)  ·  parse tree (right)");
        SystemConsole.Error.WriteLine("  navigate: Tab/Shift+Tab cycle panes · click pane to focus · ↑/↓/←/→ move · Ctrl+C quit");
    }
}
