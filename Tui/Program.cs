// LALR.CC.Tui — terminal UI debugger for LALR grammars.
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
using LALR.CC;
using LALR.CC.LexicalGrammar;
using LALR.CC.Schema;
using LALR.CC.Tui.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LALR.CC.Tui;

// Three-pane layout: schema (grammar+lexer) on the left, input in the middle,
// parse tree on the right. Focus determines which pane consumes keystrokes
// (Tab / Shift+Tab cycle; mouse click on a pane focuses it). All three render
// every frame, so edits in the middle pane visibly propagate to the right.
internal enum Focus { Schema = 0, Input = 1, Tree = 2 }

// The middle and right panes each have two interchangeable modes selected by
// F-keys. The middle pane swaps between the source-input editor (re-parses on
// every keystroke) and the grammar-YAML editor (rebuilds the parser on every
// keystroke). The right pane swaps between the parse-tree visualisation and
// the LALR(1) parse table rendered as a state×symbol grid.
internal enum MiddleMode { Input, GrammarEditor }
internal enum RightMode { ParseTree, ParseTable }

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null) return 2;

        // Load the grammar YAML once. The text is kept around so the grammar
        // editor pane can present it for live editing — every keystroke there
        // re-deserialises and re-compiles, so we never re-read from disk.
        string yamlText;
        try
        {
            yamlText = await File.ReadAllTextAsync(opts.GrammarPath);
        }
        catch (FileNotFoundException)
        {
            SystemConsole.Error.WriteLine($"grammar file not found: {opts.GrammarPath}");
            return 1;
        }

        var session = CompileSession(yamlText);
        if (session.YamlError != null)
        {
            SystemConsole.Error.WriteLine($"yaml parse error in {opts.GrammarPath}: {session.YamlError}");
            return 1;
        }

        // Initial input buffer: seeded from --input <file> if given, else empty.
        string initialInputText = "";
        if (opts.InputPath is { } inputPath)
        {
            try { initialInputText = await File.ReadAllTextAsync(inputPath); }
            catch (Exception) { initialInputText = ""; }
        }

        await using var term = new CL.VirtualTerminal();
        await term.InitAsync();

        if (term.IsInputRedirected || term.IsOutputRedirected)
        {
            SystemConsole.Error.WriteLine("[lalr-tui] stdin/stdout is not a terminal — falling back to non-interactive summary");
            PrintNonInteractive(opts, session, initialInputText);
            return 0;
        }

        term.EnterAlternateScreen();
        try
        {
            await RunUi(term, opts, session, initialInputText, yamlText);
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

    /// <summary>
    /// Loaded grammar pipeline state. Mutated whenever the grammar editor
    /// makes a change, never replaced wholesale (so all the cached refs in
    /// the UI keep pointing at the same instance).
    /// </summary>
    private sealed class Session
    {
        public GrammarSchema Schema = new();
        public Grammar Grammar;
        public IReadOnlyDictionary<string, LexRule[]> Lexer = new Dictionary<string, LexRule[]>();
        public Parser? Parser;
        public List<ParseTableRow> ParseRows = [];
        public string? YamlError;
        public string? CompileError;
        public string? ParserError;
    }

    private static Session CompileSession(string yamlText)
    {
        var s = new Session();
        try
        {
            // Empty YAML → empty schema. Use a `?? new()` fallback so downstream
            // null-checks don't have to special-case the "deleted everything"
            // case where the user blanked the buffer.
            s.Schema = Deserializer.Deserialize<GrammarSchema>(yamlText) ?? new GrammarSchema();
        }
        catch (YamlException ex)
        {
            s.YamlError = $"line {ex.Start.Line}, col {ex.Start.Column}: {ex.Message}";
            return s;
        }

        try
        {
            // Stub-actions: the TUI is observational. We pass actions=null when
            // there are no actions in the schema; otherwise we register a no-op
            // for every action so SchemaCompiler doesn't reject the schema. The
            // parser invocation later passes allowRewriting=false so these never
            // actually run.
            var stubActions = BuildStubActions(s.Schema);
            (s.Grammar, s.Lexer) = SchemaCompiler.Compile(s.Schema, stubActions);
        }
        catch (SchemaCompilationException ex)
        {
            s.Grammar = default;
            s.Lexer = new Dictionary<string, LexRule[]>();
            s.CompileError = ex.Message;
            return s;
        }
        catch (Exception ex)
        {
            // Defense in depth for the live grammar editor: any keystroke can
            // leave the schema mid-edit, and a stray null inside a partially-
            // typed YAML doc could surface as something other than a clean
            // SchemaCompilationException. Treat it as a compile error so the
            // status bar shows it instead of the TUI crashing.
            s.Grammar = default;
            s.Lexer = new Dictionary<string, LexRule[]>();
            s.CompileError = ex.GetType().Name + ": " + ex.Message;
            return s;
        }

        try
        {
            s.Parser = new Parser(s.Grammar);
            s.ParseRows = ParseTableView.BuildRows(s.Parser, s.Grammar, s.Schema);
        }
        catch (GrammarConflictException ex) { s.ParserError = "grammar conflicts: " + ex.Message; }
        catch (Exception ex) { s.ParserError = "parser-build error: " + ex.Message; }

        return s;
    }

    private static async Task RunUi(
        CL.IVirtualTerminal term,
        Options opts,
        Session session,
        string initialInputText,
        string initialYamlText)
    {
        var focus = Focus.Input;
        var middleMode = MiddleMode.Input;
        var rightMode = RightMode.ParseTree;
        // Last text we wrote to (or read from) disk. The grammar editor's
        // dirty flag is derived from `grammarState.GetText() != savedYamlText`,
        // and Ctrl+S replaces this with the freshly-saved buffer. Defining
        // dirty as a derived value (not a boolean we have to remember to flip)
        // means an undo back to the saved state automatically clears the
        // asterisk in the header.
        var savedYamlText = initialYamlText;
        // Status message shown briefly in the help bar after a save. Cleared
        // on the next keystroke so it doesn't linger.
        string? saveStatus = null;

        var panel = new CL.Panel(term);
        var headerVp = panel.Dock(DockStyle.Top, 1);
        var statusVp = panel.Dock(DockStyle.Bottom, 1);
        var helpVp   = panel.Dock(DockStyle.Bottom, 1);
        // Three-column body. Sizes are cell-cols; the middle pane gets the
        // remainder via Fill so it expands on wider terminals. The parse-table
        // pane is wider (60) than the parse-tree pane needs (40); we use 60 so
        // the table renders the full row of cells without truncation in the
        // common case.
        var schemaVp = panel.Dock(DockStyle.Left, 36);
        var rightVp  = panel.Dock(DockStyle.Right, 60);
        var middleVp = panel.Fill();

        var header = new CL.TextBar(headerVp);
        var status = new CL.TextBar(statusVp);
        var help   = new CL.TextBar(helpVp);

        // Unified schema tree: grammar and lexer live side-by-side under one
        // synthetic root, each half collapsible independently.
        var schemaTree = new CL.TreeView<Node>(schemaVp);
        schemaTree.Header(" schema");

        // Two text areas share the middle viewport — only one renders at a time
        // depending on middleMode. Keeping both around means the user's edit
        // state is preserved across mode switches.
        var inputState = new CL.TextAreaState(initialInputText);
        var inputArea = new CL.TextArea(middleVp);
        inputArea.State = inputState;

        // Seed the YAML editor from the in-memory text Main already read off
        // disk — cheaper than a second File.ReadAllText and guarantees the
        // editor matches `savedYamlText` byte-for-byte (otherwise a stray
        // newline difference would mark the buffer dirty on first paint).
        var grammarState = new CL.TextAreaState(initialYamlText);
        var grammarArea = new CL.TextArea(middleVp);
        grammarArea.State = grammarState;
        grammarState.MoveDocumentStart();

        // Parse-tree (right pane, default) and the parse-table list — same
        // viewport, switched by F-keys.
        var parseTree = new CL.TreeView<Node>(rightVp);
        parseTree.Header(" parse tree");

        var parseTable = new CL.ScrollableList<ParseTableRow>(rightVp);
        parseTable.Header(" parse table");

        // Tokens count and parse error are recomputed on every input or grammar
        // edit; the middle/right widgets re-render every loop iteration.
        var tokenCount = 0;
        string? tokenError = null;
        string? parseError = null;

        async Task ReparseAsync()
        {
            tokenCount = 0;
            tokenError = null;
            parseError = null;
            schemaTree.Root(SchemaRoot.Build(session.Schema, session.Grammar, session.Lexer), expandRoot: true);
            parseTable.Items(session.ParseRows);

            if (session.Lexer.Count == 0)
            {
                parseTree.Root(ParseTreeRoot.Build(null, session.Grammar, session.CompileError ?? "no lexer"), expandRoot: true);
                return;
            }
            // Zero-alloc input feed: build a 2-segment ReadOnlySequence<byte>
            // straight over the gap buffer's two halves. Both passes (token
            // count and parser) finish synchronously enough relative to the
            // backing array's lifetime that the memory stays valid.
            var seq = JoinSegments(inputState.MemoryBeforeGap, inputState.MemoryAfterGap);
            try
            {
                tokenCount = await CountTokensAsync(seq, session.Lexer);
            }
            catch (Exception ex)
            {
                // Include the originating call-site so a "Parameter 'position'"
                // ArgumentOutOfRangeException isn't a needle in a haystack.
                tokenError = ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace?.Split('\n', 2)[0]?.Trim() ?? "?");
            }

            if (session.Parser is null)
            {
                parseTree.Root(ParseTreeRoot.Build(null, session.Grammar, session.ParserError ?? session.CompileError ?? "no parser"), expandRoot: true);
                return;
            }
            try
            {
                var reader = PipeReader.Create(seq);
                var lex = new PipeBytesLexer(reader, session.Lexer, errorMode: LexerErrorMode.EmitAndStop, errorSymbolId: 0);
                var laIter = new AsyncLATokenIterator(lex);
                // allowRewriting:false skips the (no-op) stub actions and
                // forces the parser to emit fresh Reduction nodes from its
                // reduction frame — without this, the stub null-returning
                // actions collapse the entire tree into a Scalar Item with
                // null content, which renders as a single "S 1:1" leaf. See
                // Parser.cs around line 733 for the trim/rewrite branch.
                var root = await parser_ParseInputAsync(session.Parser, laIter);
                parseTree.Root(ParseTreeRoot.Build(root, session.Grammar), expandRoot: true);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
                parseTree.Root(ParseTreeRoot.Build(null, session.Grammar, ex.Message), expandRoot: true);
            }
        }

        // Wraps the parser invocation with the TUI-specific arguments so the
        // call site doesn't restate them each time we reparse.
        static Task<Item> parser_ParseInputAsync(Parser p, AsyncLATokenIterator laIter) =>
            p.ParseInputAsync(laIter, allowRewriting: false, errorMode: ParserErrorMode.Return);

        async Task RecompileGrammarAsync()
        {
            // Pull the latest YAML out of the editor buffer and feed the whole
            // pipeline through CompileSession, then refresh dependent views.
            // Driven by Ctrl+S only — per-keystroke recompile was too eager
            // (every transient mid-edit YAML state went through SchemaCompiler
            // + Parser table generation) and the lexer subtree visibly froze
            // when a partial edit broke compilation.
            var newYaml = grammarState.GetText();
            var newSession = CompileSession(newYaml);
            session.Schema = newSession.Schema;
            session.Grammar = newSession.Grammar;
            session.Lexer = newSession.Lexer;
            session.Parser = newSession.Parser;
            session.ParseRows = newSession.ParseRows;
            session.YamlError = newSession.YamlError;
            session.CompileError = newSession.CompileError;
            session.ParserError = newSession.ParserError;
            await ReparseAsync();
        }

        bool IsGrammarDirty() => !string.Equals(grammarState.GetText(), savedYamlText, StringComparison.Ordinal);

        async Task SaveAndRecompileAsync()
        {
            var buffer = grammarState.GetText();
            try
            {
                await File.WriteAllTextAsync(opts.GrammarPath, buffer);
                savedYamlText = buffer;
                saveStatus = $"saved {Path.GetFileName(opts.GrammarPath)}";
            }
            catch (Exception ex)
            {
                // Disk write failed — keep the buffer dirty and surface the
                // error in the status bar instead of silently losing edits.
                saveStatus = "save failed: " + ex.Message;
                return;
            }
            await RecompileGrammarAsync();
        }

        await ReparseAsync();

        // Bright header style for the focused pane; dim header for the rest.
        var focusedHeader   = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Blue);
        var unfocusedHeader = new CL.VtStyle(CL.SgrColor.BrightBlack, CL.SgrColor.Black);

        void RenderHeader()
        {
            var leftSb = new StringBuilder();
            leftSb.Append(' ').Append(Path.GetFileName(opts.GrammarPath));
            // Conventional editor convention: trailing asterisk on the
            // filename means unsaved changes. Cleared automatically once the
            // buffer matches the on-disk text again (e.g. after Ctrl+S, or
            // when the user undoes back to the saved state).
            if (IsGrammarDirty()) leftSb.Append('*');
            leftSb.Append("  · ").Append(session.Schema.Symbols?.Count ?? 0).Append(" symbols");
            leftSb.Append(" · ").Append(session.Schema.Productions?.Count ?? 0).Append(" groups");
            if (session.Schema.Lexer is { Count: > 0 } l) leftSb.Append(" · ").Append(l.Count).Append(" lex states");
            if (session.Parser is { } p) leftSb.Append(" · ").Append(p.ParseTable.States).Append(" states");
            var right = focus switch
            {
                Focus.Schema => "[schema]·middle·right ",
                Focus.Input  => "schema·[middle]·right ",
                Focus.Tree   => "schema·middle·[right] ",
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
            // Error precedence (highest first): YAML > schema-compile > parser-build
            // > lexer > parser. Each layer's failure hides downstream noise so the
            // user sees the root cause first.
            string text;
            CL.VtStyle style;
            if (session.YamlError != null)
            {
                text = " yaml error: " + session.YamlError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (session.CompileError != null)
            {
                text = " compile error: " + session.CompileError;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Red);
            }
            else if (session.ParserError != null)
            {
                text = " " + session.ParserError;
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
            else if (saveStatus is not null)
            {
                // Brief save confirmation. Shown until the next keystroke so a
                // successful save is acknowledged but doesn't permanently
                // occupy the status line.
                text = " " + saveStatus;
                style = new CL.VtStyle(CL.SgrColor.BrightWhite, CL.SgrColor.Green);
            }
            else
            {
                text = focus switch
                {
                    Focus.Schema => $" schema  {schemaTree.CursorIndex + 1}/{Math.Max(1, schemaTree.ItemCount)}  {NodeTitle(schemaTree.Selected)}",
                    Focus.Input  => middleMode == MiddleMode.Input
                        ? FormatInputStatus(inputState, tokenCount)
                        : FormatGrammarStatus(grammarState, IsGrammarDirty()),
                    Focus.Tree   => rightMode == RightMode.ParseTree
                        ? $" tree    {parseTree.CursorIndex + 1}/{Math.Max(1, parseTree.ItemCount)}  {NodeTitle(parseTree.Selected)}"
                        : $" table   state {parseTable.CursorIndex} of {session.ParseRows.Count}",
                    _            => "",
                };
                style = new CL.VtStyle(CL.SgrColor.White, CL.SgrColor.BrightBlack);
            }
            status.Text(text).Style(style).Render();
        }

        void RenderHelp()
        {
            // F-keys are universal: they switch the middle/right pane mode
            // regardless of which pane currently owns focus, so the user can
            // flip into the parse-table or the grammar editor without first
            // tabbing focus there.
            var fHints = $" [F1] {(rightMode == RightMode.ParseTree ? "·tree·" : "tree")}/{(rightMode == RightMode.ParseTable ? "·table·" : "table")}  [F2] {(middleMode == MiddleMode.Input ? "·input·" : "input")}/{(middleMode == MiddleMode.GrammarEditor ? "·yaml·" : "yaml")}";
            string keys;
            if (focus == Focus.Input)
            {
                keys = middleMode == MiddleMode.Input
                    ? "[Tab] next  [↑/↓/←/→] move  [Ctrl+←/→] word  type to insert  [Ctrl+C] quit"
                    : "[Tab] next  [↑/↓/←/→] move  [Ctrl+←/→] word  [Ctrl+S] save+recompile  [Ctrl+C] quit";
            }
            else
            {
                keys = "[Tab] next  [↑/↓] move  [←/→] collapse/expand  [q] quit";
            }
            help
                .Text(" " + keys + "    " + fHints)
                .Style(new CL.VtStyle(CL.SgrColor.BrightBlack, CL.SgrColor.Black))
                .Render();
        }

        void RenderPanes()
        {
            schemaTree.HeaderStyle(focus == Focus.Schema ? focusedHeader : unfocusedHeader);
            schemaTree.Render();

            // Middle pane: render whichever editor matches the current mode.
            // The non-active editor's state stays alive in the background so
            // a mode switch round-trips without losing edits or cursor pos.
            if (middleMode == MiddleMode.Input) inputArea.Render();
            else grammarArea.Render();

            // Right pane: tree or table. Both keep their selection state so
            // toggling F1 round-trips without losing the user's place.
            if (rightMode == RightMode.ParseTree)
            {
                parseTree.HeaderStyle(focus == Focus.Tree ? focusedHeader : unfocusedHeader);
                parseTree.Render();
            }
            else
            {
                // Re-set the header on every render so the column layout adapts
                // to the current viewport width (e.g. after a terminal resize).
                var headerLine = ParseTableView.BuildHeader(session.Grammar, rightVp.Size.Width);
                parseTable.Header(" " + headerLine.TrimEnd());
                parseTable.HeaderStyle(focus == Focus.Tree ? focusedHeader : unfocusedHeader);
                parseTable.Render();
            }
        }

        void RenderAll()
        {
            term.Clear();
            RenderHeader();
            RenderPanes();
            RenderHelp();
            RenderStatus();
        }

        void SetFocus(Focus next) { focus = next; RenderAll(); }

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
                Focus? clickedPane = null;
                if (schemaTree.HitTest(m.X, m.Y) is not null) clickedPane = Focus.Schema;
                else if ((middleMode == MiddleMode.Input ? inputArea.HitTest(m.X, m.Y) : grammarArea.HitTest(m.X, m.Y)) is not null) clickedPane = Focus.Input;
                else if ((rightMode == RightMode.ParseTree ? parseTree.HitTest(m.X, m.Y) : parseTable.HitTest(m.X, m.Y)) is not null) clickedPane = Focus.Tree;

                if (clickedPane is { } pn)
                {
                    if (focus != pn) SetFocus(pn);
                    var consumed = pn switch
                    {
                        Focus.Schema => schemaTree.HandleMouse(m),
                        Focus.Tree   => rightMode == RightMode.ParseTree ? parseTree.HandleMouse(m) : parseTable.HandleMouse(m),
                        // TextArea.HandleMouse landed in Console.Lib alongside
                        // the click-to-position support; route both editors
                        // (input + grammar YAML) through it so a click moves
                        // the cursor instead of just changing focus.
                        Focus.Input  => (middleMode == MiddleMode.Input ? inputArea : grammarArea).HandleMouse(m),
                        _            => false,
                    };
                    if (consumed) { RenderPanes(); RenderStatus(); }
                }
                continue;
            }

            var shift = (ev.Modifiers & ConsoleModifiers.Shift) != 0;
            var ctrl  = (ev.Modifiers & ConsoleModifiers.Control) != 0;

            if (ctrl && ev.Key == ConsoleKey.C) { quit = true; continue; }

            // Ctrl+S: write the grammar buffer to disk and re-run the full
            // pipeline (deserialise → schema compile → parser table → reparse
            // input). Works from any focus so the user can save without having
            // to tab focus into the YAML editor first. Silently ignored when
            // the buffer matches disk — saves a redundant recompile.
            if (ctrl && ev.Key == ConsoleKey.S)
            {
                if (IsGrammarDirty())
                {
                    await SaveAndRecompileAsync();
                    RenderAll();
                }
                continue;
            }

            // Any other keystroke clears the brief save-confirmation message
            // so it doesn't linger past the next interaction.
            saveStatus = null;

            // F-key mode toggles. These work from any focus so the user can
            // pop into the grammar editor or the parse table without having
            // to tab focus there first.
            if (ev.Key == ConsoleKey.F1)
            {
                rightMode = rightMode == RightMode.ParseTree ? RightMode.ParseTable : RightMode.ParseTree;
                RenderAll();
                continue;
            }
            if (ev.Key == ConsoleKey.F2)
            {
                middleMode = middleMode == MiddleMode.Input ? MiddleMode.GrammarEditor : MiddleMode.Input;
                RenderAll();
                continue;
            }

            if (ev.Key == ConsoleKey.Tab && (shift || focus != Focus.Input))
            {
                var step = shift ? -1 : 1;
                var next = (Focus)(((int)focus + step + 3) % 3);
                SetFocus(next);
                continue;
            }

            if (focus == Focus.Input)
            {
                // Route to the active editor (input or grammar). Source-input
                // edits reparse immediately (cheap — same parser, new bytes).
                // Grammar-YAML edits just mutate the buffer; the recompile is
                // deferred to Ctrl+S because the per-keystroke pipeline (yaml
                // deserialize → schema compile → parser-table generate) is
                // both expensive and visually noisy when the YAML is mid-edit.
                var area = middleMode == MiddleMode.Input ? inputArea : grammarArea;
                var moved = area.HandleKey(ev.Key, ev.Modifiers);
                var typed = !moved && area.HandleChar(ev);
                if (moved || typed)
                {
                    if (typed)
                    {
                        if (middleMode == MiddleMode.GrammarEditor)
                        {
                            // Buffer-only change. The dirty flag picks this up
                            // automatically via savedYamlText comparison; no
                            // recompile until the user hits Ctrl+S.
                        }
                        else
                        {
                            await ReparseAsync();
                        }
                    }
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
                        Focus.Tree   => rightMode == RightMode.ParseTree
                            ? parseTree.HandleKey(ev.Key, ev.Modifiers)
                            : parseTable.HandleKey(ev.Key, ev.Modifiers),
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

    private static string FormatGrammarStatus(CL.TextAreaState s, bool dirty)
    {
        var (line, col) = s.CursorLineColumn;
        var saveHint = dirty ? "[Ctrl+S to save]" : "saved";
        return $" grammar  line {line + 1}, col {col + 1}  ·  {s.Length} bytes  ·  {saveHint}";
    }

    private static string NodeTitle(Node? n)
    {
        if (n is null) return "";
        return n.PlainTitle;
    }

    // Sentinel used by every stub action; one shared instance avoids per-call
    // allocations and avoids the `null!` null-forgiving operator. SchemaCompiler
    // only checks that the dictionary has an entry for every named action — it
    // never invokes the func — and the parser is called with allowRewriting:false
    // so the func never runs at parse time either. Any non-null object satisfies
    // the contract.
    private static readonly object StubActionSentinel = new();

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
                    dict[name] = static (_, _) => StubActionSentinel;
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

    private static void PrintNonInteractive(Options opts, Session s, string initialInputText)
    {
        SystemConsole.WriteLine($"file: {opts.GrammarPath}");
        SystemConsole.WriteLine($"symbols: {s.Schema.Symbols?.Count ?? 0}");
        SystemConsole.WriteLine($"production groups: {s.Schema.Productions?.Count ?? 0}");
        SystemConsole.WriteLine($"lexer states: {s.Schema.Lexer?.Count ?? 0}");
        if (s.YamlError != null) SystemConsole.WriteLine($"yaml error: {s.YamlError}");
        if (s.CompileError != null) SystemConsole.WriteLine($"compile error: {s.CompileError}");
        if (s.ParserError != null) SystemConsole.WriteLine($"parser error: {s.ParserError}");
        if (s.Parser is { } p) SystemConsole.WriteLine($"parse-table states: {p.ParseTable.States}");
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
        SystemConsole.Error.WriteLine("  layout:   schema (left)  ·  input/grammar (middle, F2 toggles)  ·  parse tree/table (right, F1 toggles)");
        SystemConsole.Error.WriteLine("  navigate: Tab/Shift+Tab cycle panes · click pane to focus · ↑/↓/←/→ move · Ctrl+C quit");
    }
}
