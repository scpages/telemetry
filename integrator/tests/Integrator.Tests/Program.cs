using Integrator.Protobuf;

namespace Integrator.Tests;

internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Run("descriptor parse + frame decode", DecodeRoundTrip);
        Run("exe extractor recovers embedded schema", ExtractorFindsSchema);
        Run("raw decode keeps field numbers", RawDecodeByNumbers);

        Console.WriteLine(_failed == 0 ? "\nALL TESTS PASSED" : $"\n{_failed} TEST(S) FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine($"[PASS] {name}"); }
        catch (Exception e) { _failed++; Console.WriteLine($"[FAIL] {name}: {e.Message}"); }
    }

    // ---- a tiny schema: package demo { enum Mode; message Vitals; message Frame } ----

    private static byte[] BuildFrameDescriptor()
    {
        // FieldDescriptorProto: name=1, number=3, label=4, type=5, type_name=6
        byte[] FieldDesc(string nm, int num, int type, int label = 1, string? typeName = null)
        {
            var f = new List<byte>();
            Pb.StringField(f, 1, nm);
            Pb.VarintField(f, 3, (ulong)num);
            Pb.VarintField(f, 4, (ulong)label);
            Pb.VarintField(f, 5, (ulong)type);
            if (typeName != null) Pb.StringField(f, 6, typeName);
            return f.ToArray();
        }

        // DescriptorProto Frame: name=1, field=2
        var frame = new List<byte>();
        Pb.StringField(frame, 1, "Frame");
        Pb.LenField(frame, 2, FieldDesc("health", 1, (int)PType.Float));
        Pb.LenField(frame, 2, FieldDesc("mode", 2, (int)PType.Enum, typeName: ".demo.Mode"));
        Pb.LenField(frame, 2, FieldDesc("label", 3, (int)PType.String));
        Pb.LenField(frame, 2, FieldDesc("vitals", 4, (int)PType.Message, label: 3, typeName: ".demo.Vitals"));

        // DescriptorProto Vitals: heart (float, number 1)
        var vitals = new List<byte>();
        Pb.StringField(vitals, 1, "Vitals");
        Pb.LenField(vitals, 2, FieldDesc("heart", 1, (int)PType.Float));

        // EnumDescriptorProto Mode: value=2 (name=1, number=2)
        byte[] EnumValue(string nm, int num)
        {
            var v = new List<byte>();
            Pb.StringField(v, 1, nm);
            Pb.VarintField(v, 2, (ulong)num);
            return v.ToArray();
        }
        var mode = new List<byte>();
        Pb.StringField(mode, 1, "Mode");
        Pb.LenField(mode, 2, EnumValue("IDLE", 0));
        Pb.LenField(mode, 2, EnumValue("RUN", 1));

        // FileDescriptorProto: name=1, package=2, message_type=4, enum_type=5
        var file = new List<byte>();
        Pb.StringField(file, 1, "sc/game/telemetry/v1/demo.proto");
        Pb.StringField(file, 2, "demo");
        Pb.LenField(file, 4, frame.ToArray());
        Pb.LenField(file, 4, vitals.ToArray());
        Pb.LenField(file, 5, mode.ToArray());
        return file.ToArray();
    }

    private static void DecodeRoundTrip()
    {
        var schema = SchemaExtractor.BuildSchema(new[] { BuildFrameDescriptor() });
        Assert(schema.Message(".demo.Frame") != null, "Frame type missing");
        Assert(schema.Message(".demo.Vitals") != null, "Vitals type missing");
        Assert(schema.Enum(".demo.Mode") != null, "Mode enum missing");

        // encode a Frame
        var msg = new List<byte>();
        Pb.FloatField(msg, 1, 36.5f);
        Pb.VarintField(msg, 2, 1);                    // mode = RUN
        Pb.StringField(msg, 3, "ANVL_Asgard");
        var v1 = new List<byte>(); Pb.FloatField(v1, 1, 72f);
        var v2 = new List<byte>(); Pb.FloatField(v2, 1, 80f);
        Pb.LenField(msg, 4, v1.ToArray());
        Pb.LenField(msg, 4, v2.ToArray());

        var decoded = new FrameDecoder(schema).Decode(msg.ToArray(), ".demo.Frame");

        Assert(decoded["health"] is float f && Math.Abs(f - 36.5f) < 1e-6, "health wrong");
        Assert((string)decoded["mode"]! == "RUN", "enum not mapped to name");
        Assert((string)decoded["label"]! == "ANVL_Asgard", "string wrong");
        Assert(decoded["vitals"] is List<object?> { Count: 2 }, "repeated message not a list of 2");
        var first = (Dictionary<string, object?>)((List<object?>)decoded["vitals"]!)[0]!;
        Assert(first["heart"] is float h && Math.Abs(h - 72f) < 1e-6, "nested heart wrong");
    }

    private static void ExtractorFindsSchema()
    {
        // FileDescriptorProto with a telemetry service, wrapped in junk, written as a fake "exe".
        var file = new List<byte>();
        Pb.StringField(file, 1, "sc/game/telemetry/v1/service.proto");
        Pb.StringField(file, 2, "sc.game.telemetry.v1");
        var svc = new List<byte>(); Pb.StringField(svc, 1, "GameTelemetryService");
        Pb.LenField(file, 6, svc.ToArray());
        var frame = new List<byte>(); Pb.StringField(frame, 1, "TelemetryStreamResponse");
        Pb.LenField(file, 4, frame.ToArray());

        var exe = new List<byte>();
        exe.AddRange(new byte[137]);                  // leading junk
        exe.AddRange(file.ToArray());
        exe.AddRange(new byte[64]);                   // trailing junk

        string path = Path.Combine(Path.GetTempPath(), "fake_sc_" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, exe.ToArray());
        try
        {
            var blobs = SchemaExtractor.ExtractBlobs(path);
            Assert(blobs.Count >= 1, "no descriptor blob recovered");
            var schema = SchemaExtractor.BuildSchema(blobs);
            Assert(SchemaExtractor.HasTelemetryService(schema), "service not detected");
            Assert(schema.FindMessageEndingWith("TelemetryStreamResponse") != null, "response type not found");
        }
        finally { File.Delete(path); }
    }

    // Schema-less decode: keys are field numbers, nested messages recurse, strings stay
    // strings, and repeated fields fold into a list. (The "RAW" view in the UI.)
    private static void RawDecodeByNumbers()
    {
        var v1 = new List<byte>(); Pb.FloatField(v1, 1, 72f);
        var v2 = new List<byte>(); Pb.FloatField(v2, 1, 80f);
        var msg = new List<byte>();
        Pb.FloatField(msg, 1, 36.5f);             // field 1 (I32)
        Pb.VarintField(msg, 2, 7);                // field 2 (varint)
        Pb.StringField(msg, 3, "ANVL_Asgard");    // field 3 (string)
        Pb.LenField(msg, 4, v1.ToArray());        // field 4 (repeated nested message)
        Pb.LenField(msg, 4, v2.ToArray());

        var raw = RawDecoder.Decode(msg.ToArray());

        Assert(raw.ContainsKey("1") && raw.ContainsKey("2") && raw.ContainsKey("3") && raw.ContainsKey("4"),
            "keys should be field numbers, not names");
        Assert(Convert.ToUInt64(raw["2"]) == 7, "varint field wrong");
        Assert((string)raw["3"]! == "ANVL_Asgard", "string field should round-trip as text");
        Assert(raw["4"] is List<object?> { Count: 2 }, "repeated field should fold into a list of 2");
        var nested = (Dictionary<string, object?>)((List<object?>)raw["4"]!)[0]!;
        Assert(nested.ContainsKey("1"), "nested message should be keyed by field number");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception(msg);
    }
}
