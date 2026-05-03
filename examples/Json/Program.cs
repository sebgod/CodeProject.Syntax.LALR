using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;

namespace Examples.Json;

/// <summary>
/// Walks a JSON string through the YAML-driven pipeline:
/// <c>json.lalr.yaml</c> → source-generated <see cref="Json"/> partial class
/// (schema + AST records + IVisitor surface) → <see cref="JsonVisitor"/> turns
/// each reduction into a CLR value. The result is a tree of
/// <see cref="Dictionary{TKey, TValue}"/> / <see cref="List{T}"/> / primitive
/// values, identical to what <c>System.Text.Json.JsonDocument.Parse</c> would
/// hand back (modulo the lexer's deliberately-simple string handling — see
/// json.lalr.yaml's header for limitations).
/// </summary>
internal static class Program
{
    private const string Sample = """
        {
          "name": "lalr-demo",
          "version": 2,
          "active": true,
          "ratio": 1.5,
          "items": [1, 2.5, true, null, "leaf"],
          "nested": { "x": "y", "count": 42 }
        }
        """;

    public static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var visitor = new JsonVisitor();
        var (grammar, lexerTable) = SchemaCompiler.Compile(Json.Schema, Json.BuildActions(visitor));
        var parser = new Parser(grammar);

        using var lexer = PipeBytesLexer.FromString(Sample, lexerTable);
        using var tokens = new AsyncLATokenIterator(lexer);

        // Debug must be non-null even when we want silence — pass null writer
        // delegates and the trace methods become no-ops. (The parser dereferences
        // debugger directly on every loop iteration; fixing that is a runtime-
        // library tweak we'd do separately.)
        // trimReductions:false so every reduction fires its visitor — otherwise
        // arity-1 single-non-terminal productions (E -> V, M -> P, V -> O, V -> A)
        // get folded by the parser before the rewriter runs and the visitor's
        // ElementsOne / MembersOne / ValueObject / ValueArray methods would never
        // be called. With trim off, every cons case can rely on its tail already
        // being a List rather than handling scalar-vs-list ambiguity per-call.
        var result = await parser.ParseInputAsync(tokens, debugger: null, trimReductions: false);
        if (result.IsError)
        {
            Console.Error.WriteLine($"parse failed: {result}");
            return;
        }

        // The reduction frame at the root is a V (start symbol). Its Content is
        // whatever the matching `value*` visitor method returned — that's the
        // root CLR value of the parsed JSON.
        var root = result.Content;
        Console.WriteLine("Parsed:");
        Print(root, indent: 2);
    }

    private static void Print(object value, int indent)
    {
        var pad = new string(' ', indent);
        switch (value)
        {
            case null:
                Console.WriteLine($"{pad}null");
                break;
            case bool b:
                Console.WriteLine($"{pad}{(b ? "true" : "false")}");
                break;
            case string s:
                Console.WriteLine($"{pad}\"{s}\"");
                break;
            case double d:
                Console.WriteLine($"{pad}{d.ToString(CultureInfo.InvariantCulture)}");
                break;
            case List<object> list:
                Console.WriteLine($"{pad}[ ({list.Count} items)");
                foreach (var item in list) { Print(item, indent + 2); }
                Console.WriteLine($"{pad}]");
                break;
            case Dictionary<string, object> dict:
                Console.WriteLine($"{pad}{{ ({dict.Count} keys)");
                foreach (var kv in dict)
                {
                    Console.WriteLine($"{pad}  \"{kv.Key}\":");
                    Print(kv.Value, indent + 4);
                }
                Console.WriteLine($"{pad}}}");
                break;
            default:
                Console.WriteLine($"{pad}<unknown {value.GetType().Name}>");
                break;
        }
    }

    /// <summary>
    /// One <c>Visit</c> overload per generated AST record. Container actions
    /// (object/array/members/elements) build the CLR collections; leaf actions
    /// (string/number/true/false/null) extract or construct primitives. The
    /// passthrough <c>valueObject</c> / <c>valueArray</c> branches forward the
    /// child's already-built result.
    /// </summary>
    private sealed class JsonVisitor : Json.IVisitor
    {
        // V branches — return whatever the inner production already built.
        public object Visit(Json.ValueObject node) => node.Arg0.Content;
        public object Visit(Json.ValueArray node) => node.Arg0.Content;
        public object Visit(Json.ValueString node) => UnquoteString((string)node.Arg0.Content);
        public object Visit(Json.ValueNumber node) => double.Parse((string)node.Arg0.Content, CultureInfo.InvariantCulture);
        public object Visit(Json.ValueTrue node) => true;
        public object Visit(Json.ValueFalse node) => false;
        public object Visit(Json.ValueNull node) => null;

        // Object/array containers.
        public object Visit(Json.EmptyObject node) => new Dictionary<string, object>(0);
        public object Visit(Json.ObjectValue node)
        {
            // node.Arg1.Content is the M reduction's result — a List<KeyValuePair<string, object>>.
            var pairs = (List<KeyValuePair<string, object>>)node.Arg1.Content;
            var dict = new Dictionary<string, object>(pairs.Count);
            foreach (var kv in pairs)
            {
                dict[kv.Key] = kv.Value;
            }
            return dict;
        }
        public object Visit(Json.EmptyArray node) => new List<object>(0);
        public object Visit(Json.ArrayValue node) => node.Arg1.Content; // E already built the List<object>

        // Members / elements list folds.
        public object Visit(Json.MembersOne node) =>
            new List<KeyValuePair<string, object>> { (KeyValuePair<string, object>)node.Arg0.Content };

        public object Visit(Json.MembersCons node)
        {
            var head = (KeyValuePair<string, object>)node.Arg0.Content;
            var tail = (List<KeyValuePair<string, object>>)node.Arg2.Content;
            var list = new List<KeyValuePair<string, object>>(tail.Count + 1) { head };
            list.AddRange(tail);
            return list;
        }

        public object Visit(Json.ElementsOne node) =>
            new List<object> { node.Arg0.Content };

        public object Visit(Json.ElementsCons node)
        {
            var head = node.Arg0.Content;
            var tail = (List<object>)node.Arg2.Content;
            var list = new List<object>(tail.Count + 1) { head };
            list.AddRange(tail);
            return list;
        }

        // Pair: string key + value. Key needs unquoting; value is already built.
        public object Visit(Json.Pair node) =>
            new KeyValuePair<string, object>(
                UnquoteString((string)node.Arg0.Content),
                node.Arg2.Content);

        /// <summary>
        /// Strip the surrounding double quotes from the lexer's string match.
        /// We don't process backslash escapes — see json.lalr.yaml's header for
        /// the documented limitation.
        /// </summary>
        private static string UnquoteString(string raw) =>
            (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                ? raw[1..^1]
                : raw;
    }
}
