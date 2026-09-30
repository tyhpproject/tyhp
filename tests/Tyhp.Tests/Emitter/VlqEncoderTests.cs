using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class VlqEncoderTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "C")]
    [InlineData(-1, "D")]
    [InlineData(5, "K")]
    [InlineData(-5, "L")]
    [InlineData(15, "e")]
    [InlineData(16, "gB")]
    [InlineData(-16, "hB")]
    [InlineData(100, "oG")]
    [InlineData(1000, "w+B")]
    public void Encode_KnownValues_MatchSourceMapV3Examples(int value, string expected)
    {
        VlqEncoder.Encode(value).Should().Be(expected);
    }

    [Fact]
    public void EncodeThenDecode_RoundTripsInclusiveRangeNegative10000To10000()
    {
        for (int n = -10000; n <= 10000; n++)
        {
            string encoded = VlqEncoder.Encode(n);
            int offset = 0;
            int decoded = VlqEncoder.Decode(encoded, ref offset);
            decoded.Should().Be(n, "Encode({0}) produced \"{1}\"", n, encoded);
            offset.Should().Be(encoded.Length);
        }
    }

    [Fact]
    public void EncodeArray_ConcatenatesSelfDelimitingValuesThatDecodeBack()
    {
        int[] values = [0, 0, 1, 0];
        string encoded = VlqEncoder.Encode(values);

        encoded.Should().NotContain(",");
        encoded.Should().NotContain(";");
        encoded.Should().Be(string.Concat(values.Select(VlqEncoder.Encode)));
        VlqEncoder.DecodeSegment(encoded).Should().Equal(values);
    }

    [Fact]
    public void Decode_AdvancesOffsetAcrossConcatenatedValues()
    {
        int[] values = [1, -1, 16, 1000];
        string encoded = VlqEncoder.Encode(values);
        int offset = 0;

        foreach (int expected in values)
        {
            VlqEncoder.Decode(encoded, ref offset).Should().Be(expected);
        }

        offset.Should().Be(encoded.Length);
    }

    [Fact]
    public void DecodeSegment_EmptyString_ReturnsEmptyArray()
    {
        VlqEncoder.DecodeSegment("").Should().BeEmpty();
    }

    [Fact]
    public void Encode_EmptyArray_ReturnsEmptyString()
    {
        VlqEncoder.Encode([]).Should().BeEmpty();
    }

    [Fact]
    public void EncodeThenDecode_RoundTripsIntExtremes()
    {
        foreach (int n in new[] { int.MinValue, int.MaxValue })
        {
            string encoded = VlqEncoder.Encode(n);
            int offset = 0;
            VlqEncoder.Decode(encoded, ref offset).Should().Be(n);
            offset.Should().Be(encoded.Length);
        }
    }

    [Fact]
    public void Decode_TruncatedContinuation_ThrowsFormatException()
    {
        // 16 encodes as "gB"; "g" is a continuation digit with no following digit.
        int offset = 0;
        Action act = () => VlqEncoder.Decode("g", ref offset);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_InvalidCharacter_ThrowsFormatException()
    {
        int offset = 0;
        Action act = () => VlqEncoder.Decode("!", ref offset);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_OffsetPastEnd_ThrowsArgumentOutOfRangeException()
    {
        int offset = 0;
        Action act = () => VlqEncoder.Decode("", ref offset);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Encode_NullArray_ThrowsArgumentNullException()
    {
        Action act = () => VlqEncoder.Encode((int[])null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
