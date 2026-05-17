using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using LALR.CC.LexicalGrammar;
using Xunit;

namespace LALR.CC.Tests;

public class PipeRuneIteratorTests
{
    [Theory]
    [InlineData("1+2*34+5/6\r\n")]
    [InlineData("\r\n")]
    [InlineData("\r\r")]
    [InlineData("\n\r")]
    [InlineData("𝒜\r\n𝓑")]
    [InlineData("")]
    // EOL normalisation and UTF-8 decoding via Rune.DecodeFromUtf8.
    public async Task TestNormalisationAsync(string input)
    {
        using var runeReader = PipeRuneIterator.FromString(input, cancellationToken: TestContext.Current.CancellationToken);
        var i = 0;
        while (i < input.Length)
        {
            input.CurrentAndLA(ref i, out var expectedCurrent, out var expectedLA);
            Assert.Equal(expectedCurrent != PipeRuneIterator.EOF, await runeReader.MoveNextAsync());

            var current = await runeReader.CurrentAsync();
            Assert.True(current == expectedCurrent,
                $"Current codepoint i={i} expected={expectedCurrent.DisplayUTF8()} is={current.DisplayUTF8()}");

            var la = await runeReader.LookAheadAsync();
            Assert.True(la == expectedLA,
                $"Lookahead codepoint i={i} expected={expectedLA.DisplayUTF8()} is={la.DisplayUTF8()}");
        }
    }

    [Theory]
    [InlineData('\r', 4096)]
    [InlineData('\n', 4096)]
    [InlineData('a', 4096)]
    [InlineData('\r', 4097)]
    [InlineData('b', 4097)]
    // Fence-post: long ASCII runs that span multiple Pipe segments.
    public async Task TestLargeStreamStringAsync(char input, int count)
    {
        await TestNormalisationAsync(new string(input, count));
    }

    [Theory]
    [InlineData('a', 4096)]
    [InlineData('a', 4097)]
    [InlineData(0x1D400, 4096)]
    [InlineData(0x1D400, 4097)]
    // Multi-byte runes (incl. supplementary plane) crossing buffer boundaries.
    public async Task TestLargeSequenceStringAsync(int seedCodepoint, int count)
    {
        var chars = new StringBuilder(count);
        for (var i = 0; i < count; i++)
        {
            var c = seedCodepoint + i;
            if (c >= char.MaxValue || !char.IsSurrogate((char)c))
            {
                chars.Append(char.ConvertFromUtf32(System.Math.Min(0x10ffff, c)));
            }
        }
        await TestNormalisationAsync(chars.ToString());
    }

    [Fact]
    // Throws if Current/LookAhead are read before MoveNextAsync.
    public async Task TestUninitialised()
    {
        using var runeReader = PipeRuneIterator.FromString("", cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runeReader.CurrentAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runeReader.LookAheadAsync());
    }

    [Fact]
    // Reset is intentionally unsupported on a streaming reader.
    public void TestResetIsUnsupported()
    {
        using var runeReader = PipeRuneIterator.FromString("12", cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(runeReader.SupportsResetting);
        Assert.Throws<InvalidOperationException>(runeReader.Reset);
    }

    [Fact]
    // Truncated UTF-8 at EOF surfaces as a replacement codepoint, not a hang.
    public async Task TestTruncatedUtf8Async()
    {
        // 0xE2 0x82 is the start of a 3-byte sequence (€ is E2 82 AC) — truncate the trailing AC.
        var truncated = new byte[] { 0xE2, 0x82 };
        using var runeReader = PipeRuneIterator.FromBytes(truncated, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal(PipeRuneIterator.ReplacementCodepoint, await runeReader.CurrentAsync());
        Assert.False(await runeReader.MoveNextAsync());
    }

    [Fact]
    // Invalid UTF-8 mid-stream surfaces as a replacement codepoint.
    public async Task TestInvalidUtf8Async()
    {
        // 'a' (0x61), then 0xFF (never valid in UTF-8), then 'b' (0x62).
        var bytes = new byte[] { 0x61, 0xFF, 0x62 };
        using var runeReader = PipeRuneIterator.FromBytes(bytes, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal((int)'a', await runeReader.CurrentAsync());
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal(PipeRuneIterator.ReplacementCodepoint, await runeReader.CurrentAsync());
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal((int)'b', await runeReader.CurrentAsync());
        Assert.False(await runeReader.MoveNextAsync());
    }

    [Fact]
    // FromStream feeds bytes directly into the Pipe — no StreamReader / no UTF-16 round trip.
    public async Task TestFromStreamUtf8Async()
    {
        // €a — multi-byte then ASCII, via a MemoryStream.
        var bytes = Encoding.UTF8.GetBytes("€a");
        await using var stream = new MemoryStream(bytes, writable: false);
        using var runeReader = PipeRuneIterator.FromStream(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal(0x20AC, await runeReader.CurrentAsync());
        Assert.True(await runeReader.MoveNextAsync());
        Assert.Equal((int)'a', await runeReader.CurrentAsync());
        Assert.False(await runeReader.MoveNextAsync());
    }
}
