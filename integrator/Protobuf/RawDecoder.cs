using System.Text;

namespace Integrator.Protobuf;

/// <summary>
/// Schema-less protobuf decode, like <c>protoc --decode_raw</c>: walks the wire format
/// with no knowledge of the message types, so keys are the raw <b>field numbers</b>
/// (not names) and length-delimited fields are shown as a nested message when they
/// parse as one, otherwise as a UTF-8 string, otherwise base64 bytes. This is the true
/// "raw" view — the readable, named version comes from <see cref="FrameDecoder"/>.
/// </summary>
internal static class RawDecoder
{
    public static Dictionary<string, object?> Decode(byte[] msg) => DecodeMessage(msg, 0, msg.Length);

    private static Dictionary<string, object?> DecodeMessage(byte[] d, int start, int len)
    {
        var obj = new Dictionary<string, object?>();
        int i = start, end = start + len;
        while (i < end)
        {
            ulong tag = Wire.ReadVarint(d, ref i);
            int fn = (int)(tag >> 3), wt = (int)(tag & 7);
            object? val = ReadValue(d, ref i, wt);
            Add(obj, fn.ToString(), val);          // key = raw field number
        }
        return obj;
    }

    private static object? ReadValue(byte[] d, ref int i, int wt)
    {
        switch (wt)
        {
            case Wire.Varint: return Wire.ReadVarint(d, ref i);
            case Wire.I64: { ulong v = BitConverter.ToUInt64(d, i); i += 8; return v; }
            case Wire.I32: { uint v = BitConverter.ToUInt32(d, i); i += 4; return v; }
            case Wire.Len:
                int len = (int)Wire.ReadVarint(d, ref i);
                int s = i; i += len;
                return DecodeLen(d, s, len);
            default: throw new FormatException($"bad wire type {wt}");
        }
    }

    // Repeated fields reuse the same number: fold duplicates into a list.
    private static void Add(Dictionary<string, object?> o, string key, object? val)
    {
        if (!o.TryGetValue(key, out var cur)) { o[key] = val; return; }
        if (cur is List<object?> list) { list.Add(val); return; }
        o[key] = new List<object?> { cur, val };
    }

    // A length-delimited field is ambiguous without a schema: try nested message,
    // then printable UTF-8 string, else base64.
    private static object? DecodeLen(byte[] d, int s, int len)
    {
        if (len == 0) return "";
        if (LooksLikeMessage(d, s, len)) return DecodeMessage(d, s, len);
        if (TryUtf8(d, s, len, out string str)) return str;
        return Convert.ToBase64String(d, s, len);
    }

    /// <summary>True if the bytes parse cleanly as a protobuf message (consumed exactly).</summary>
    private static bool LooksLikeMessage(byte[] d, int start, int len)
    {
        try
        {
            int i = start, end = start + len;
            while (i < end)
            {
                ulong tag = Wire.ReadVarint(d, ref i);
                int fn = (int)(tag >> 3), wt = (int)(tag & 7);
                if (fn == 0) return false;
                switch (wt)
                {
                    case Wire.Varint: Wire.ReadVarint(d, ref i); break;
                    case Wire.I64: i += 8; break;
                    case Wire.I32: i += 4; break;
                    case Wire.Len:
                        int l = (int)Wire.ReadVarint(d, ref i);
                        if (l < 0 || i + l > end) return false;
                        i += l;
                        break;
                    default: return false;   // groups / bad wire type
                }
                if (i > end) return false;
            }
            return i == end;
        }
        catch { return false; }
    }

    private static bool TryUtf8(byte[] d, int s, int len, out string str)
    {
        str = "";
        try
        {
            string candidate = new UTF8Encoding(false, true).GetString(d, s, len);
            foreach (char c in candidate)
                if (char.IsControl(c) && c is not ('\n' or '\r' or '\t')) return false;  // not text
            str = candidate;
            return true;
        }
        catch { return false; }
    }
}
