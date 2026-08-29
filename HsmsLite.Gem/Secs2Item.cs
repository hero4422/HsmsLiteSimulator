using System.Text;

namespace HsmsLite.Gem
{
    /// <summary>
    /// Minimal SECS-II (SEMI E5) item encoder/decoder. Only implements the four types this
    /// project's GEM (SEMI E30) messages actually need: List, ASCII, Boolean, U4.
    ///
    /// Wire format: one format byte (top 6 bits = format code, bottom 2 bits = number of
    /// length bytes that follow), then that many big-endian length bytes, then the value.
    /// Full SEMI E5 format code table for reference (only the four below are implemented here):
    ///   List=0, Binary=8, Boolean=9, ASCII=16, JIS-8=17, 2-byte=18,
    ///   I8=24, I1=25, I2=26, I4=28, F8=32, F4=36, U8=40, U1=41, U2=42, U4=44.
    /// </summary>
    public abstract class Secs2Item
    {
        private protected const byte FormatList = 0;
        private protected const byte FormatBoolean = 9;
        private protected const byte FormatAscii = 16;
        private protected const byte FormatU4 = 44;

        public abstract byte[] Encode();

        /// <summary>Appends a human-readable, indented SECS-II tree representation of this item
        /// (e.g. <c>&lt;L 2 ... &gt;</c>) to <paramref name="sb"/>, matching the trace format
        /// SECS/GEM tools conventionally log.</summary>
        public abstract void WriteTree(StringBuilder sb, int indent);

        private protected static byte[] EncodeHeaderAndValue(byte formatCode, int lengthField, byte[] valueBytes)
        {
            if (lengthField < 0 || lengthField > 0xFFFFFF)
                throw new Secs2FormatException($"SECS-II length {lengthField} exceeds the 3-byte length field (max 16777215).");

            var numLenBytes = lengthField <= 0xFF ? 1 : lengthField <= 0xFFFF ? 2 : 3;
            var result = new byte[1 + numLenBytes + valueBytes.Length];
            result[0] = (byte)((formatCode << 2) | numLenBytes);
            for (var i = 0; i < numLenBytes; i++)
                result[1 + i] = (byte)(lengthField >> (8 * (numLenBytes - 1 - i)));
            valueBytes.CopyTo(result, 1 + numLenBytes);
            return result;
        }

        public static Secs2Item Decode(ReadOnlySpan<byte> data) => Decode(data, out _);

        /// <summary>Decodes one item starting at the beginning of <paramref name="data"/> and reports
        /// how many bytes it occupied, so callers (mainly <see cref="Secs2List"/>) can decode a
        /// sequence of items back to back.</summary>
        public static Secs2Item Decode(ReadOnlySpan<byte> data, out int bytesConsumed)
        {
            if (data.Length < 2)
                throw new Secs2FormatException("SECS-II item is truncated (need at least a format byte and 1 length byte).");

            var formatByte = data[0];
            var numLenBytes = formatByte & 0x03;
            var formatCode = (byte)(formatByte >> 2);
            if (numLenBytes == 0 || 1 + numLenBytes > data.Length)
                throw new Secs2FormatException($"SECS-II item has an invalid length-byte count ({numLenBytes}).");

            var length = 0;
            for (var i = 0; i < numLenBytes; i++)
                length = (length << 8) | data[1 + i];
            var headerLen = 1 + numLenBytes;

            return formatCode switch
            {
                FormatList => Secs2List.DecodeBody(data, headerLen, itemCount: length, out bytesConsumed),
                FormatAscii => Secs2Ascii.DecodeBody(data, headerLen, byteCount: length, out bytesConsumed),
                FormatBoolean => Secs2Boolean.DecodeBody(data, headerLen, byteCount: length, out bytesConsumed),
                FormatU4 => Secs2U4.DecodeBody(data, headerLen, byteCount: length, out bytesConsumed),
                _ => throw new Secs2FormatException(
                    $"Unsupported SECS-II format code {formatCode} (0x{formatCode:X2}) in format byte 0x{formatByte:X2}. " +
                    "This lite implementation only decodes List/ASCII/Boolean/U4.")
            };
        }
    }
}
