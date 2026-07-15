using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class LoftempsDecoderTests
    {
        private static byte[] Loftemps() =>
            File.ReadAllBytes(Path.Combine(TestPaths.RawDataDir, "GEODATA", "LOFTEMPS.DAT"));

        [Fact]
        public void Load_ValueCountMatchesFileSizeOver2()
        {
            var raw = Loftemps();
            var values = LoftempsDecoder.Load(raw);
            Assert.Equal(raw.Length / 2, values.Length);
        }

        [Fact]
        public void Load_FirstValueMatchesLittleEndianBytes()
        {
            var raw = Loftemps();
            var values = LoftempsDecoder.Load(raw);
            Assert.Equal(raw[0] | (raw[1] << 8), values[0]);
        }

        [Fact]
        public void Load_TemplateCountIs112()
        {
            var values = LoftempsDecoder.Load(Loftemps());
            Assert.Equal(112, values.Length / LoftempsDecoder.RowsPerTemplate);
        }

        [Fact]
        public void Load_OddByteLengthThrows()
        {
            Assert.Throws<InvalidDataException>(() => LoftempsDecoder.Load(new byte[] { 1, 2, 3 }));
        }
    }
}
