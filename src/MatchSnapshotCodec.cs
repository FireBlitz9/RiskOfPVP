using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ArenaPvP
{
    internal static class MatchSnapshotCodec
    {
        internal const int MaximumBytes = 512 * 1024, MaximumWireBytes = 60000;
        internal static byte[] Encode(string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            if (data.Length > MaximumBytes) throw new InvalidDataException("Match snapshot exceeds decoded limit.");
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(data, 0, data.Length);
                if (output.Length > MaximumWireBytes) throw new InvalidDataException("Match snapshot exceeds wire limit.");
                return output.ToArray();
            }
        }
        internal static string Decode(byte[] data)
        {
            if (data == null || data.Length > MaximumWireBytes) throw new InvalidDataException("Invalid match snapshot size.");
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096]; int count;
                while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                { if (output.Length + count > MaximumBytes) throw new InvalidDataException("Match snapshot expansion exceeds limit."); output.Write(buffer, 0, count); }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
