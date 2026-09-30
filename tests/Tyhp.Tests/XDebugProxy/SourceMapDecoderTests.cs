using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class SourceMapDecoderTests
{
    [Theory]
    [InlineData("AAAA", new[] { 0, 0, 0, 0 })]
    [InlineData("AACA", new[] { 0, 0, 1, 0 })]
    public void DecodeVlq_KnownFourFieldSegments_MatchSourceMapV3Examples(string segment, int[] expected)
    {
        SourceMapDecoder.DecodeVlq(segment).Should().Equal(expected);
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("C", 1)]
    [InlineData("D", -1)]
    [InlineData("K", 5)]
    [InlineData("L", -5)]
    [InlineData("e", 15)]
    [InlineData("gB", 16)]
    [InlineData("hB", -16)]
    [InlineData("oG", 100)]
    [InlineData("w+B", 1000)]
    public void DecodeVlq_KnownSingleValues_MatchSourceMapV3Examples(string encoded, int expected)
    {
        SourceMapDecoder.DecodeVlq(encoded).Should().Equal(expected);
    }

    [Fact]
    public void DecodeVlq_ConcatenatedContinuationDigits_DecodesEachValue()
    {
        // 16 → gB (continuation on first digit), 1000 → w+B, -1 → D.
        SourceMapDecoder.DecodeVlq("gBw+BD").Should().Equal(16, 1000, -1);
    }

    [Fact]
    public void DecodeVlq_EmptyString_ReturnsEmptyArray()
    {
        SourceMapDecoder.DecodeVlq("").Should().BeEmpty();
    }

    [Fact]
    public void DecodeVlq_Null_ThrowsArgumentNullException()
    {
        Action act = () => SourceMapDecoder.DecodeVlq(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DecodeVlq_TruncatedContinuation_ThrowsFormatException()
    {
        Action act = () => SourceMapDecoder.DecodeVlq("g");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void DecodeVlq_InvalidCharacter_ThrowsFormatException()
    {
        Action act = () => SourceMapDecoder.DecodeVlq("!");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void DecodeMappings_EmptyString_ReturnsEmptyList()
    {
        SourceMapDecoder.DecodeMappings("").Should().BeEmpty();
    }

    [Fact]
    public void DecodeMappings_OriginSegment_IsGeneratedAndOriginalZero()
    {
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("AAAA");

        decoded.Should().HaveCount(1);
        decoded[0].Should().ContainSingle().Which.Should().Be(new MappingEntry(0, 0, 0, 0, 0, null));
    }

    [Fact]
    public void DecodeMappings_EmptyGeneratedLines_PreserveLineIndex()
    {
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("AAAA;;AACA");

        decoded.Should().HaveCount(3);
        decoded[0].Should().ContainSingle().Which.Should().Be(new MappingEntry(0, 0, 0, 0, 0, null));
        decoded[1].Should().BeEmpty();
        decoded[2].Should().ContainSingle().Which.Should().Be(new MappingEntry(2, 0, 0, 1, 0, null));
    }

    [Fact]
    public void DecodeMappings_SameLineSegments_AreRelativeToPrevious()
    {
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("AAAA,KACI");

        decoded.Should().HaveCount(1);
        decoded[0].Should().Equal(
            new MappingEntry(0, 0, 0, 0, 0, null),
            new MappingEntry(0, 5, 0, 1, 4, null));
    }

    [Fact]
    public void DecodeMappings_FiveFieldSegment_CapturesNameIndex()
    {
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("AAAAA");

        decoded[0].Should().ContainSingle().Which.Should().Be(new MappingEntry(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void DecodeMappings_OneFieldSegment_HasNoOriginalPosition()
    {
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("A");

        MappingEntry entry = decoded[0].Should().ContainSingle().Subject;
        entry.GeneratedLine.Should().Be(0);
        entry.GeneratedColumn.Should().Be(0);
        entry.HasOriginalPosition.Should().BeFalse();
        entry.OriginalSourceIndex.Should().BeNull();
    }

    [Fact]
    public void DecodeMappings_GeneratedColumnResetsEachLine_OtherFieldsDoNot()
    {
        // Line 0 col 5 → orig (0,0); line 1 col 0 (reset) → orig line +1.
        IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = SourceMapDecoder.DecodeMappings("KAAA;AACA");

        decoded[0].Should().ContainSingle().Which.GeneratedColumn.Should().Be(5);
        decoded[1].Should().ContainSingle().Which.Should().Be(new MappingEntry(1, 0, 0, 1, 0, null));
    }
}
