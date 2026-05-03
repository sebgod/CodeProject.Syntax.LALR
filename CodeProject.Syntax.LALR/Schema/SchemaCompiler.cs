using System;
using System.Collections.Generic;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Schema;

/// <summary>
/// Turns a declarative <see cref="GrammarSchema"/> (typically loaded from YAML/JSON,
/// or built in code) into a runtime <see cref="Grammar"/> plus a lexer table ready
/// to feed <see cref="PipeBytesLexer"/>. Validates references, resolves semantic
/// action names through a caller-supplied dictionary, and surfaces every problem as
/// a <see cref="SchemaCompilationException"/> with a path-style locator into the
/// schema (e.g. <c>productions[1].rules[3].rhs[2]</c>).
/// </summary>
public static class SchemaCompiler
{
    public const string IgnoreActionKeyword = "ignore";

    /// <summary>
    /// Compile the schema. <paramref name="actions"/> resolves
    /// <see cref="ProductionSchema.Action"/> names to rewriter delegates; pass
    /// <c>null</c> if no production uses an action.
    /// </summary>
    public static (Grammar Grammar, Dictionary<string, LexRule[]> Lexer) Compile(
        GrammarSchema schema,
        IReadOnlyDictionary<string, Func<int, Item[], object>> actions = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        ValidateSymbols(schema);
        var symbolIds = BuildSymbolMap(schema);
        var grammar = BuildGrammar(schema, symbolIds, actions);
        var lexer = BuildLexer(schema, symbolIds);
        return (grammar, lexer);
    }

    private static void ValidateSymbols(GrammarSchema schema)
    {
        if (schema.Symbols is null || schema.Symbols.Count == 0)
        {
            throw new SchemaCompilationException("symbols must be non-empty (index 0 is the start symbol)");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < schema.Symbols.Count; i++)
        {
            var name = schema.Symbols[i];
            if (string.IsNullOrEmpty(name))
            {
                throw new SchemaCompilationException($"symbols[{i}] is null or empty");
            }
            if (!seen.Add(name))
            {
                throw new SchemaCompilationException($"symbols[{i}] = '{name}' is a duplicate; symbol names must be unique");
            }
        }
    }

    private static Dictionary<string, int> BuildSymbolMap(GrammarSchema schema)
    {
        var map = new Dictionary<string, int>(schema.Symbols.Count, StringComparer.Ordinal);
        for (var i = 0; i < schema.Symbols.Count; i++)
        {
            map[schema.Symbols[i]] = i;
        }
        return map;
    }

    private static Grammar BuildGrammar(GrammarSchema schema, Dictionary<string, int> symbolIds,
        IReadOnlyDictionary<string, Func<int, Item[], object>> actions)
    {
        var groupCount = schema.Productions?.Count ?? 0;
        if (groupCount == 0)
        {
            throw new SchemaCompilationException("productions must have at least one group");
        }

        var symbolNames = new SymbolName[schema.Symbols.Count];
        for (var i = 0; i < schema.Symbols.Count; i++)
        {
            symbolNames[i] = new SymbolName(i, schema.Symbols[i]);
        }

        var groups = new PrecedenceGroup[groupCount];
        for (var gi = 0; gi < groupCount; gi++)
        {
            var groupSchema = schema.Productions[gi];
            var ruleCount = groupSchema.Rules?.Count ?? 0;
            if (ruleCount == 0)
            {
                throw new SchemaCompilationException($"productions[{gi}].rules must be non-empty");
            }
            var rules = new Production[ruleCount];
            for (var ri = 0; ri < ruleCount; ri++)
            {
                var rule = groupSchema.Rules[ri];
                rules[ri] = BuildProduction(rule, symbolIds, actions, $"productions[{gi}].rules[{ri}]");
            }
            groups[gi] = new PrecedenceGroup(groupSchema.Derivation, rules);
        }
        return new Grammar(symbolNames, groups);
    }

    private static Production BuildProduction(ProductionSchema rule, Dictionary<string, int> symbolIds,
        IReadOnlyDictionary<string, Func<int, Item[], object>> actions, string path)
    {
        if (string.IsNullOrEmpty(rule.Lhs))
        {
            throw new SchemaCompilationException($"{path}.lhs is missing");
        }
        if (!symbolIds.TryGetValue(rule.Lhs, out var lhs))
        {
            throw new SchemaCompilationException($"{path}.lhs = '{rule.Lhs}' is not in symbols[]");
        }

        var rhsCount = rule.Rhs?.Count ?? 0;
        var rhs = new int[rhsCount];
        for (var i = 0; i < rhsCount; i++)
        {
            var name = rule.Rhs[i];
            if (string.IsNullOrEmpty(name))
            {
                throw new SchemaCompilationException($"{path}.rhs[{i}] is null or empty");
            }
            if (!symbolIds.TryGetValue(name, out var id))
            {
                throw new SchemaCompilationException($"{path}.rhs[{i}] = '{name}' is not in symbols[]");
            }
            rhs[i] = id;
        }

        Func<int, Item[], object> rewriter = null;
        if (!string.IsNullOrEmpty(rule.Action))
        {
            if (actions is null)
            {
                throw new SchemaCompilationException(
                    $"{path}.action = '{rule.Action}' but no `actions` dictionary was passed to SchemaCompiler.Compile");
            }
            if (!actions.TryGetValue(rule.Action, out rewriter) || rewriter is null)
            {
                throw new SchemaCompilationException(
                    $"{path}.action = '{rule.Action}' is not in the actions dictionary");
            }
        }

        return rewriter is null
            ? new Production(lhs, rhs)
            : new Production(lhs, rewriter, rhs);
    }

    private static Dictionary<string, LexRule[]> BuildLexer(GrammarSchema schema, Dictionary<string, int> symbolIds)
    {
        if (schema.Lexer is null || schema.Lexer.Count == 0)
        {
            throw new SchemaCompilationException("lexer table is missing");
        }
        if (!schema.Lexer.ContainsKey(PipeBytesLexer.RootState))
        {
            throw new SchemaCompilationException(
                $"lexer table must contain a '{PipeBytesLexer.RootState}' state");
        }

        var compiledStates = new Dictionary<string, LexRule[]>(schema.Lexer.Count, StringComparer.Ordinal);
        var knownStates = new HashSet<string>(schema.Lexer.Keys, StringComparer.Ordinal);

        foreach (var (stateName, ruleSchemas) in schema.Lexer)
        {
            if (ruleSchemas is null || ruleSchemas.Count == 0)
            {
                throw new SchemaCompilationException($"lexer.{stateName} has no rules");
            }
            var compiled = new LexRule[ruleSchemas.Count];
            for (var i = 0; i < ruleSchemas.Count; i++)
            {
                compiled[i] = BuildLexRule(ruleSchemas[i], symbolIds, knownStates, $"lexer.{stateName}[{i}]");
            }
            compiledStates[stateName] = compiled;
        }
        return compiledStates;
    }

    private static LexRule BuildLexRule(LexRuleSchema rule, Dictionary<string, int> symbolIds,
        HashSet<string> knownStates, string path)
    {
        if (string.IsNullOrEmpty(rule.Symbol))
        {
            throw new SchemaCompilationException($"{path}.symbol is missing");
        }
        if (!symbolIds.TryGetValue(rule.Symbol, out var symbolId))
        {
            throw new SchemaCompilationException($"{path}.symbol = '{rule.Symbol}' is not in symbols[]");
        }
        if (string.IsNullOrEmpty(rule.Match))
        {
            throw new SchemaCompilationException($"{path}.match is missing");
        }

        IRx pattern;
        try
        {
            pattern = IRxParser.Parse(rule.Match);
        }
        catch (FormatException ex)
        {
            throw new SchemaCompilationException($"{path}.match: {ex.Message}", ex);
        }

        // Mutual-exclusion check: at most one of (Push, Pop, Action) may be set.
        var instructions = 0;
        if (!string.IsNullOrEmpty(rule.Push))
        {
            instructions++;
        }
        if (rule.Pop)
        {
            instructions++;
        }
        if (!string.IsNullOrEmpty(rule.Action))
        {
            instructions++;
        }
        if (instructions > 1)
        {
            throw new SchemaCompilationException(
                $"{path}: at most one of `push`, `pop`, `action` may be set");
        }

        string instruction = null;
        if (!string.IsNullOrEmpty(rule.Push))
        {
            if (!knownStates.Contains(rule.Push))
            {
                throw new SchemaCompilationException(
                    $"{path}.push = '{rule.Push}' is not a defined lexer state");
            }
            instruction = rule.Push;
        }
        else if (rule.Pop)
        {
            instruction = PipeBytesLexer.PopState;
        }
        else if (!string.IsNullOrEmpty(rule.Action))
        {
            if (!string.Equals(rule.Action, IgnoreActionKeyword, StringComparison.Ordinal))
            {
                throw new SchemaCompilationException(
                    $"{path}.action = '{rule.Action}' is not recognized (only 'ignore' is supported)");
            }
            instruction = PipeBytesLexer.Ignore;
        }

        return new LexRule(symbolId, pattern, instruction);
    }
}

/// <summary>
/// Thrown by <see cref="SchemaCompiler.Compile"/> when the schema is invalid
/// (unknown symbol, missing root state, conflicting lexer instructions, bad regex,
/// unresolved action name, etc.). The message includes a path locator into the schema.
/// </summary>
public sealed class SchemaCompilationException : InvalidOperationException
{
    public SchemaCompilationException(string message) : base(message) { }
    public SchemaCompilationException(string message, Exception inner) : base(message, inner) { }
}
