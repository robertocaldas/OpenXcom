using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class McdDecoderTests
    {
        private static readonly string DataDir = TestPaths.RawDataDir;

        private static byte[] Mcd() => File.ReadAllBytes(Path.Combine(DataDir, "TERRAIN", "CULTIVAT.MCD"));

        [Fact]
        public void RecordSizeIs62()
        {
            Assert.Equal(62, McdDecoder.RecordSize);
        }

        [Fact]
        public void Load_RecordCountMatchesFileSizeOver62()
        {
            var records = McdDecoder.Load(Mcd());
            Assert.Equal(Mcd().Length / 62, records.Count);
        }

        [Fact]
        public void Load_ParsesFrameAndFlagFieldsAtCorrectOffsets()
        {
            var raw = Mcd();
            var records = McdDecoder.Load(raw);
            var r0 = records[0];

            // Frame[0] is byte 0; Stop_LOS is byte 31; TU_Walk is byte 39; T_Level is byte 48.
            Assert.Equal(raw[0], r0.Frames[0]);
            Assert.Equal(raw[31] != 0, r0.StopLOS);
            Assert.Equal(raw[39], (byte)r0.TuWalk);
            Assert.Equal((sbyte)raw[48], (sbyte)r0.TLevel);
            // ScanG is the only 16-bit little-endian read: bytes 20-21
            Assert.Equal(raw[20] | (raw[21] << 8), r0.ScanG);
        }
    }
}
