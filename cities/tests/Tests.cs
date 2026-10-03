// Tests for the game-independent half of the mod (Json, PoseMapper, OutbreakModel, SharedFrames, LinkServer).
// Built and run by cities/build.sh under Mono; the pytest suite also uses "city" mode as a real C# link server
// for the cross-language tests.
//
//   mono Tests.exe unit <path to protocol/vectors.json>
//   mono Tests.exe read <name> <seconds>     reads frames another process publishes (e.g. the bodycam)
//   mono Tests.exe city <port> <seconds>     a minimal city: hello, 4 Hz city state, waves on tyrant, frames
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RaccoonCitySkylines;

static class Tests
{
    static int failures;
    static int passes;

    static void Check(bool ok, string what)
    {
        if (ok)
            passes++;
        else
        {
            failures++;
            Console.WriteLine("FAIL: " + what);
        }
    }

    static bool Near(double a, double b, double eps = 1e-6)
    {
        return Math.Abs(a - b) <= eps * Math.Max(1.0, Math.Abs(b));
    }

    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "read")
            return Read(args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        if (args.Length >= 1 && args[0] == "city")
            return City(int.Parse(args[1]), double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        string vectors = args.Length >= 2 ? args[1] : Path.Combine("..", Path.Combine("protocol", "vectors.json"));
        JsonTests();
        PoseTests(vectors);
        OutbreakTests();
        FrameTests();
        LinkTests();
        Console.WriteLine(passes + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void JsonTests()
    {
        var o = Json.Parse("{\"t\":\"cam\",\"f\":12,\"p\":[1.5,-2,3e2],\"ok\":true,\"n\":null,\"s\":\"a\\\"b\\u00e9\\n\"}") as Dictionary<string, object>;
        Check(o != null, "json object parses");
        Check(Json.Str(o, "t") == "cam", "json string");
        Check(Json.Num(o, "f") == 12, "json number");
        var p = new double[3];
        Check(Json.Nums(o, "p", p) && p[0] == 1.5 && p[1] == -2 && p[2] == 300, "json numeric array");
        Check(Json.Bool(o, "ok"), "json bool");
        Check(Json.Get(o, "n") == null && o.ContainsKey("n"), "json null");
        Check(Json.Str(o, "s") == "a\"bé\n", "json escapes");
        string w = Json.Write(Json.Obj("t", "city", "inf", 0.25, "power", false, "name", "Raccoon \"City\"", "arr", new double[] { 1, 2 }));
        var back = Json.Parse(w) as Dictionary<string, object>;
        Check(Json.Num(back, "inf") == 0.25 && !Json.Bool(back, "power", true) && Json.Str(back, "name") == "Raccoon \"City\"", "json round trip: " + w);
        Check(Json.ParseMessage("{\"x\":1}") == null, "message without t is rejected");
        Check(Json.ParseMessage("{\"t\":1}") == null, "message with non-string t is rejected");
        Check(Json.ParseMessage("not json") == null, "garbage is rejected");
        Check(Json.ParseMessage("{\"t\":\"a\"} trailing") == null, "trailing junk is rejected");
        Check(Json.Write(double.NaN) == "null", "NaN writes as null");
    }

    static void PoseTests(string path)
    {
        var root = Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        var cases = Json.Get(root, "cases") as List<object>;
        Check(cases != null && cases.Count > 0, "vectors.json has cases");
        foreach (Dictionary<string, object> c in cases)
        {
            double[] p = new double[3], q = new double[4], origin = new double[3], anchor = new double[3], pos = new double[3], rot = new double[4];
            Json.Nums(c, "p", p);
            Json.Nums(c, "q", q);
            Json.Nums(c, "origin", origin);
            Json.Nums(c, "anchor", anchor);
            Json.Nums(c, "pos", pos);
            Json.Nums(c, "rot", rot);
            var m = new PoseMapper
            {
                Anchor = new Vec3(anchor[0], anchor[1], anchor[2]),
                HeadingDeg = Json.Num(c, "heading"),
                Scale = Json.Num(c, "scale", 1),
                FlipZ = Json.Bool(c, "flipZ", true),
            };
            m.Recentre(new Vec3(origin[0], origin[1], origin[2]));
            Vec3 gp;
            Quat gq;
            m.Map(new Vec3(p[0], p[1], p[2]), new Quat(q[0], q[1], q[2], q[3]), out gp, out gq);
            bool ok = Near(gp.X, pos[0], 1e-9) && Near(gp.Y, pos[1], 1e-9) && Near(gp.Z, pos[2], 1e-9)
                && Near(gq.X, rot[0], 1e-9) && Near(gq.Y, rot[1], 1e-9) && Near(gq.Z, rot[2], 1e-9) && Near(gq.W, rot[3], 1e-9);
            Check(ok, "pose vector matches Python reference");
        }

        // The first camera lands on the anchor.
        var first = new PoseMapper { Anchor = new Vec3(100, 50, -20), HeadingDeg = 37 };
        Vec3 fp;
        Quat fq;
        first.Map(new Vec3(5, 1, 9), Quat.Identity, out fp, out fq);
        Check(Near(fp.X, 100) && Near(fp.Y, 50) && Near(fp.Z, -20), "first camera position lands on the anchor");

        // Walking forward in RE2 (-Z in RE Engine, mirrored to +Z) walks toward the heading in the city.
        var walk = new PoseMapper { Anchor = new Vec3(0, 0, 0), HeadingDeg = 90 };
        walk.Recentre(new Vec3(0, 0, 0));
        Vec3 wp;
        Quat wq;
        walk.Map(new Vec3(0, 0, -10), Quat.Identity, out wp, out wq);
        Check(Near(wp.X, 10, 1e-9) && Math.Abs(wp.Z) < 1e-9, "RE2 forward with heading 90 maps to city +X, got " + wp.X + "," + wp.Z);
        // A camera yawing left in RE2 (positive about +Y, right-handed) yaws left in Unity too (negative about +Y).
        var yaw = new PoseMapper { HeadingDeg = 0 };
        Quat left = new Quat(0, Math.Sin(Math.PI / 8), 0, Math.Cos(Math.PI / 8)); // 45 degrees, right-handed
        yaw.Map(new Vec3(0, 0, 0), left, out wp, out wq);
        Vec3 fwd = wq.Rotate(new Vec3(0, 0, 1));
        Check(fwd.X < -0.5 && fwd.Z > 0.5, "RE2 yaw left stays left in the city, forward " + fwd.X + "," + fwd.Z);
    }

    static DistrictInput[] Districts(int n, double pop, double police, double health, bool power)
    {
        var a = new DistrictInput[OutbreakModel.MaxDistricts];
        for (int i = 0; i < n; i++)
            a[i] = new DistrictInput { Exists = true, Population = pop, Police = police, Health = health, Power = power };
        return a;
    }

    static double Run(OutbreakModel m, DistrictInput[] d, int days, int district = 0)
    {
        for (int i = 0; i < days * 16; i++)
            m.Step(d, 1.0 / 16);
        return m.Infection[district];
    }

    static void OutbreakTests()
    {
        var m = new OutbreakModel();
        Check(m.SeedIfClean(0) && Near(m.Infection[0], m.P.Seed), "patient zero seeds a clean city");
        Check(!m.SeedIfClean(1), "a city with infection is not reseeded");
        double unchecked10 = Run(m, Districts(1, 10000, 0, 0, true), 10);
        Check(unchecked10 > 0.5, "unchecked outbreak takes over a dense district: " + unchecked10);

        var policed = new OutbreakModel();
        policed.Infection[0] = 0.3;
        double after = Run(policed, Districts(1, 10000, 1, 1, true), 10);
        Check(after < 0.3, "full police + healthcare pushes it back: " + after);

        var dark = new OutbreakModel();
        var lit = new OutbreakModel();
        dark.Infection[0] = lit.Infection[0] = 0.1;
        double darkI = Run(dark, Districts(1, 3000, 0.3, 0.3, false), 3);
        double litI = Run(lit, Districts(1, 3000, 0.3, 0.3, true), 3);
        Check(darkI > litI, "blackouts make it worse: " + darkI + " > " + litI);

        var spread = new OutbreakModel();
        spread.Infection[1] = 0.5;
        Run(spread, Districts(3, 5000, 0.2, 0.2, true), 2, 1);
        Check(spread.Infection[2] > 0, "commuters carry it to a clean district");

        var kills = new OutbreakModel();
        kills.Infection[4] = 0.2;
        kills.Kills(4, 10);
        Check(Near(kills.Infection[4], 0.2 - 10 * kills.P.KillRelief), "RE2 kills lower the district's infection");
        kills.Kills(4, 100000);
        Check(kills.Infection[4] == 0, "kills never go below zero");
        kills.Wave(4, 1);
        Check(Near(kills.Infection[4], kills.P.WaveSize), "a Mr. X wave raises it");
        kills.Wave(-1, 1);
        kills.Kills(500, 1);
        Check(true, "out-of-range districts are ignored");

        var empty = new OutbreakModel();
        empty.Infection[7] = 0.4;
        Run(empty, Districts(0, 0, 0, 0, true), 5, 7);
        Check(Near(empty.Infection[7], 0.4), "districts that don't exist don't change");
        Check(OutbreakModel.Clamp01(-1) == 0 && OutbreakModel.Clamp01(2) == 1, "clamp");
    }

    static void FrameTests()
    {
        string name = "Test" + System.Diagnostics.Process.GetCurrentProcess().Id;
        using (var w = new FrameWriter(name, 64, 32, 3))
        using (var r = new FrameReader(name))
        {
            byte[] rgba;
            Check(r.TryRead(out rgba) == null, "nothing published yet");
            var px = new byte[8 * 4 * 4];
            for (int i = 0; i < px.Length; i++)
                px[i] = (byte)i;
            for (int k = 1; k <= 5; k++)
            {
                px[0] = (byte)k;
                w.Write(new FrameInfo { Frame = k, HostFrame = 100 + k, Width = 8, Height = 4, Fov = 60, Flags = SharedFrames.FlagBottomUp, Daylight = 0.5f, Infection = 0.25f }, px);
            }
            FrameInfo f = r.TryRead(out rgba);
            Check(f != null && f.Frame == 5 && f.HostFrame == 105 && f.Width == 8 && f.Height == 4 && f.BottomUp, "reader gets the newest frame");
            Check(f != null && rgba[0] == 5 && rgba[127] == 127 && f.Daylight == 0.5f && f.Infection == 0.25f, "pixels and metadata survive");
            Check(r.TryRead(out rgba) == null, "the same frame is not returned twice");
            bool threw = false;
            try
            {
                w.Write(new FrameInfo { Width = 65, Height = 4 }, new byte[65 * 16]);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Check(threw, "oversized frames are refused");
        }
        try
        {
            File.Delete("/dev/shm/RaccoonSkylines." + name);
        }
        catch (IOException)
        {
        }
    }

    static void LinkTests()
    {
        using (var link = new LinkServer(0))
        {
            link.Start();
            using (var c = new System.Net.Sockets.TcpClient("127.0.0.1", link.Port))
            {
                var s = c.GetStream();
                var hello = System.Text.Encoding.UTF8.GetBytes("{\"t\":\"hello\",\"side\":\"re2\"}\nnot json\n{\"t\":\"cam\",\"f\":1,\"p\":[1,2,3]}\n" + new string('x', LinkServer.MaxLine + 10) + "\n{\"t\":\"ev\",\"id\":1");
                s.Write(hello, 0, hello.Length);
                var tail = System.Text.Encoding.UTF8.GetBytes(",\"k\":\"kill\"}\n");
                s.Write(tail, 0, tail.Length);
                var got = new List<Dictionary<string, object>>();
                for (int i = 0; i < 100 && got.Count < 3; i++)
                {
                    Thread.Sleep(20);
                    got.AddRange(link.Drain());
                }
                Check(got.Count == 3, "link keeps 3 good messages and drops garbage/over-long lines, got " + got.Count);
                Check(got.Count == 3 && Json.Str(got[0], "t") == "hello" && Json.Str(got[1], "t") == "cam" && Json.Str(got[2], "k") == "kill", "link preserves order and splits across packets");
                Check(link.Connected && link.Connections == 1, "link reports the client");
                link.Send(Json.Obj("t", "city", "inf", 0.5));
                var buf = new byte[4096];
                s.ReadTimeout = 2000;
                int n = s.Read(buf, 0, buf.Length);
                string line = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                Check(line.EndsWith("\n") && Json.Num(Json.ParseMessage(line.Trim()), "inf") == 0.5, "link sends newline-terminated JSON: " + line);
            }
            for (int i = 0; i < 100 && link.Connected; i++)
                Thread.Sleep(20);
            Check(!link.Connected, "link notices the disconnect");
        }
    }

    static int Read(string name, double seconds)
    {
        int frames = 0, w = 0, h = 0;
        long last = 0;
        using (var r = new FrameReader(name))
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                byte[] px;
                FrameInfo f = r.TryRead(out px);
                if (f != null)
                {
                    frames++;
                    w = f.Width;
                    h = f.Height;
                    last = f.Frame;
                }
                Thread.Sleep(10);
            }
        }
        Console.WriteLine("{\"frames\":" + frames + ",\"w\":" + w + ",\"h\":" + h + ",\"last\":" + last + "}");
        return frames > 0 ? 0 : 1;
    }

    /// <summary>A stand-in city built from the real LinkServer and FrameWriter, for the cross-language tests.</summary>
    static int City(int port, double seconds)
    {
        var model = new OutbreakModel();
        model.Infection[0] = 0.4;
        using (var link = new LinkServer(port))
        using (var frames = new FrameWriter(SharedFrames.City, 320, 180))
        {
            link.Log = s => Console.WriteLine(s);
            link.Start();
            int seen = 0, ev = 0;
            long frame = 0, hostFrame = 0;
            var px = new byte[320 * 180 * 4];
            DateTime end = DateTime.UtcNow.AddSeconds(seconds), nextCity = DateTime.MinValue;
            Console.WriteLine("ready");
            while (DateTime.UtcNow < end)
            {
                if (link.Connections != seen)
                {
                    seen = link.Connections;
                    link.Send(Json.Obj("t", "hello", "side", "city", "v", 1, "city", "Mono City"));
                }
                foreach (var m in link.Drain())
                {
                    string t = Json.Str(m, "t");
                    if (t == "cam")
                        hostFrame = (long)Json.Num(m, "f");
                    else if (t == "ev" && Json.Str(m, "k") == "kill")
                        model.Kills(0, 1);
                    else if (t == "ev" && Json.Str(m, "k") == "tyrant")
                    {
                        model.Wave(0, 1);
                        link.Send(Json.Obj("t", "ev", "id", ++ev, "k", "wave", "d", Json.Obj("strength", 1.0)));
                    }
                    Console.WriteLine("got " + Json.Write(m));
                }
                if (DateTime.UtcNow >= nextCity)
                {
                    nextCity = DateTime.UtcNow.AddMilliseconds(250);
                    link.Send(Json.Obj("t", "city", "district", "Mono City", "inf", model.Infection[0], "cityInf", model.Infection[0], "power", true,
                        "police", 0.5, "health", 0.5, "pop", 1000, "daylight", 0.8, "feed", true));
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        px[i] = 40;
                        px[i + 1] = 60;
                        px[i + 2] = 200;
                        px[i + 3] = 255;
                    }
                    frames.Write(new FrameInfo { Frame = ++frame, HostFrame = hostFrame, Width = 320, Height = 180, Fov = 60, Flags = SharedFrames.FlagBottomUp, Daylight = 0.8f, Infection = (float)model.Infection[0] }, px);
                }
                Thread.Sleep(5);
            }
        }
        return 0;
    }
}
