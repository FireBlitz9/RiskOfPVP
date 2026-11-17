using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ArenaPvP
{
    internal static class SettingsSnapshotCodec
    {
        internal const int MaximumBytes = 512 * 1024;
        internal static string Encode(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Host settings exceed snapshot limit.");
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(bytes, 0, bytes.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }
        internal static string Decode(string encoded)
        {
            if (encoded.Length > 60000) throw new InvalidDataException("Settings packet too large.");
            using (var input = new MemoryStream(Convert.FromBase64String(encoded)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096]; int count;
                while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                { if (output.Length + count > MaximumBytes) throw new InvalidDataException("Settings expansion exceeds limit."); output.Write(buffer, 0, count); }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
