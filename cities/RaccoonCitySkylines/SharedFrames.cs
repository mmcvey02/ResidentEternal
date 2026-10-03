using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Frames in named shared memory, layout in protocol/PROTOCOL.md section 2. On Windows this is a pagefile-backed
    /// mapping "Local\RaccoonSkylines.&lt;name&gt;"; elsewhere (tests under Mono) a file in /dev/shm with the same bytes.
    /// </summary>
    public abstract class SharedMemory : IDisposable
    {
        public abstract long Size { get; }
        public abstract void Write(long offset, byte[] src, int srcOffset, int count);
        public abstract void Read(long offset, byte[] dst, int dstOffset, int count);
        public abstract void Dispose();

        static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        public static SharedMemory Create(string name, long size)
        {
            return IsWindows ? (SharedMemory)WinSharedMemory.Open(name, size, true) : FileSharedMemory.Open(name, size, true);
        }

        /// <summary>Opens an existing mapping; null if nobody has created it yet.</summary>
        public static SharedMemory OpenExisting(string name)
        {
            try
            {
                return IsWindows ? (SharedMemory)WinSharedMemory.Open(name, 0, false) : FileSharedMemory.Open(name, 0, false);
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void WriteI32(long o, int v) { Write(o, BitConverter.GetBytes(v), 0, 4); }
        public void WriteI64(long o, long v) { Write(o, BitConverter.GetBytes(v), 0, 8); }
        public void WriteF32(long o, float v) { Write(o, BitConverter.GetBytes(v), 0, 4); }

        public int ReadI32(long o)
        {
            var b = new byte[4];
            Read(o, b, 0, 4);
            return BitConverter.ToInt32(b, 0);
        }

        public long ReadI64(long o)
        {
            var b = new byte[8];
            Read(o, b, 0, 8);
            return BitConverter.ToInt64(b, 0);
        }

        public float ReadF32(long o)
        {
            var b = new byte[4];
            Read(o, b, 0, 4);
            return BitConverter.ToSingle(b, 0);
        }
    }

    sealed class WinSharedMemory : SharedMemory
    {
        const uint PAGE_READWRITE = 0x04;
        const uint FILE_MAP_ALL_ACCESS = 0xF001F;
        const int HeaderProbe = 4096;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool UnmapViewOfFile(IntPtr view);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        IntPtr mapping, view;
        long size;

        public override long Size { get { return size; } }

        public static WinSharedMemory Open(string name, long size, bool create)
        {
            string full = "Local\\RaccoonSkylines." + name;
            var m = new WinSharedMemory();
            if (create)
            {
                m.mapping = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, (uint)(size >> 32), (uint)size, full);
                m.size = size;
            }
            else
            {
                m.mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, false, full);
            }
            if (m.mapping == IntPtr.Zero)
                throw new IOException("no file mapping " + full + " (" + Marshal.GetLastWin32Error() + ")");
            if (!create)
            {
                // Size comes from the header: header bytes + stride * slots.
                IntPtr probe = MapViewOfFile(m.mapping, FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr(HeaderProbe));
                if (probe == IntPtr.Zero)
                {
                    CloseHandle(m.mapping);
                    throw new IOException("cannot map " + full);
                }
                int slots = Marshal.ReadInt32(probe, 12);
                long stride = Marshal.ReadInt64(probe, 16);
                int header = Marshal.ReadInt32(probe, 8);
                UnmapViewOfFile(probe);
                m.size = header + stride * slots;
            }
            m.view = MapViewOfFile(m.mapping, FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr((ulong)m.size));
            if (m.view == IntPtr.Zero)
            {
                CloseHandle(m.mapping);
                throw new IOException("cannot map " + full + " (" + Marshal.GetLastWin32Error() + ")");
            }
            return m;
        }

        public override void Write(long offset, byte[] src, int srcOffset, int count)
        {
            Marshal.Copy(src, srcOffset, new IntPtr(view.ToInt64() + offset), count);
        }

        public override void Read(long offset, byte[] dst, int dstOffset, int count)
        {
            Marshal.Copy(new IntPtr(view.ToInt64() + offset), dst, dstOffset, count);
        }

        public override void Dispose()
        {
            if (view != IntPtr.Zero)
                UnmapViewOfFile(view);
            if (mapping != IntPtr.Zero)
                CloseHandle(mapping);
            view = mapping = IntPtr.Zero;
        }
    }

    sealed class FileSharedMemory : SharedMemory
    {
        FileStream file;

        public override long Size { get { return file.Length; } }

        public static FileSharedMemory Open(string name, long size, bool create)
        {
            string path = "/dev/shm/RaccoonSkylines." + name;
            if (!create && !File.Exists(path))
                throw new IOException("no " + path);
            var m = new FileSharedMemory();
            m.file = new FileStream(path, create ? FileMode.OpenOrCreate : FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            if (create)
                m.file.SetLength(size);
            return m;
        }

        public override void Write(long offset, byte[] src, int srcOffset, int count)
        {
            file.Seek(offset, SeekOrigin.Begin);
            file.Write(src, srcOffset, count);
            file.Flush();
        }

        public override void Read(long offset, byte[] dst, int dstOffset, int count)
        {
            file.Seek(offset, SeekOrigin.Begin);
            int done = 0;
            while (done < count)
            {
                int n = file.Read(dst, dstOffset + done, count - done);
                if (n <= 0)
                    throw new IOException("short read");
                done += n;
            }
        }

        public override void Dispose()
        {
            if (file != null)
                file.Dispose();
            file = null;
        }
    }

    public sealed class FrameInfo
    {
        public long Frame, HostFrame;
        public int Width, Height, Flags;
        public float Near, Far, Fov, Daylight, Infection;
        public bool BottomUp { get { return (Flags & SharedFrames.FlagBottomUp) != 0; } }
    }

    public static class SharedFrames
    {
        public const uint Magic = 0x4B534352; // "RCSK"
        public const int Version = 1;
        public const int Header = 4096;
        public const int SlotDesc = 256;
        public const int SlotDescBytes = 128;
        public const int FlagDepth = 1;
        public const int FlagBottomUp = 2;
        public const string City = "City";
        public const string Bodycam = "Bodycam";
    }

    /// <summary>Writes colour frames into a ring of slots (seqlock per slot).</summary>
    public sealed class FrameWriter : IDisposable
    {
        readonly SharedMemory mem;
        readonly long stride;
        readonly int slots;
        readonly long[] seq;
        long publish;
        int next;

        public readonly int MaxWidth, MaxHeight;

        public FrameWriter(string name, int maxWidth, int maxHeight, int slots = 3)
        {
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            this.slots = slots;
            stride = (long)maxWidth * maxHeight * 4;
            seq = new long[slots];
            mem = SharedMemory.Create(name, SharedFrames.Header + stride * slots);
            var head = new byte[48];
            Put(head, 0, BitConverter.GetBytes(SharedFrames.Magic));
            Put(head, 4, BitConverter.GetBytes(SharedFrames.Version));
            Put(head, 8, BitConverter.GetBytes(SharedFrames.Header));
            Put(head, 12, BitConverter.GetBytes(slots));
            Put(head, 16, BitConverter.GetBytes(stride));
            Put(head, 24, BitConverter.GetBytes(maxWidth));
            Put(head, 28, BitConverter.GetBytes(maxHeight));
            Put(head, 32, BitConverter.GetBytes(0L));
            Put(head, 40, BitConverter.GetBytes(-1));
            Put(head, 44, BitConverter.GetBytes(System.Diagnostics.Process.GetCurrentProcess().Id));
            mem.Write(0, head, 0, head.Length);
        }

        static void Put(byte[] dst, int at, byte[] src)
        {
            Buffer.BlockCopy(src, 0, dst, at, src.Length);
        }

        public void Write(FrameInfo info, byte[] rgba)
        {
            if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxWidth || info.Height > MaxHeight)
                throw new ArgumentException("frame size " + info.Width + "x" + info.Height + " outside 1.." + MaxWidth + "x" + MaxHeight);
            int bytes = info.Width * info.Height * 4;
            if (rgba.Length < bytes)
                throw new ArgumentException("pixel buffer too small");
            int i = next;
            next = (next + 1) % slots;
            long d = SharedFrames.SlotDesc + SharedFrames.SlotDescBytes * i;
            seq[i]++;
            mem.WriteI64(d, seq[i]);
            mem.Write(SharedFrames.Header + stride * i, rgba, 0, bytes);
            var desc = new byte[48];
            Put(desc, 0, BitConverter.GetBytes(info.Frame));
            Put(desc, 8, BitConverter.GetBytes(info.HostFrame));
            Put(desc, 16, BitConverter.GetBytes(info.Width));
            Put(desc, 20, BitConverter.GetBytes(info.Height));
            Put(desc, 24, BitConverter.GetBytes(info.Near));
            Put(desc, 28, BitConverter.GetBytes(info.Far));
            Put(desc, 32, BitConverter.GetBytes(info.Fov));
            Put(desc, 36, BitConverter.GetBytes(info.Flags & ~SharedFrames.FlagDepth));
            Put(desc, 40, BitConverter.GetBytes(info.Daylight));
            Put(desc, 44, BitConverter.GetBytes(info.Infection));
            mem.Write(d + 8, desc, 0, desc.Length);
            seq[i]++;
            mem.WriteI64(d, seq[i]);
            publish++;
            var tail = new byte[12];
            Put(tail, 0, BitConverter.GetBytes(publish));
            Put(tail, 8, BitConverter.GetBytes(i));
            mem.Write(32, tail, 0, 12);
        }

        public void Dispose()
        {
            mem.Dispose();
        }
    }

    /// <summary>Reads the newest frame from a mapping someone else writes. Reconnects lazily.</summary>
    public sealed class FrameReader : IDisposable
    {
        readonly string name;
        SharedMemory mem;
        long lastPublish = -1;
        DateTime nextAttempt = DateTime.MinValue;
        byte[] pixels = new byte[0];

        public FrameReader(string name)
        {
            this.name = name;
        }

        public bool Open
        {
            get { return mem != null; }
        }

        bool Ensure()
        {
            if (mem != null)
                return true;
            if (DateTime.UtcNow < nextAttempt)
                return false;
            nextAttempt = DateTime.UtcNow.AddSeconds(1);
            mem = SharedMemory.OpenExisting(name);
            if (mem != null && (uint)mem.ReadI32(0) != SharedFrames.Magic)
            {
                mem.Dispose();
                mem = null;
            }
            return mem != null;
        }

        /// <summary>The newest unseen frame, or null. The returned pixel buffer is reused by the next call.</summary>
        public FrameInfo TryRead(out byte[] rgba)
        {
            rgba = null;
            if (!Ensure())
                return null;
            long publish = mem.ReadI64(32);
            int slot = mem.ReadI32(40);
            int slots = mem.ReadI32(12);
            long stride = mem.ReadI64(16);
            if (publish == lastPublish || slot < 0 || slot >= slots)
                return null;
            long d = SharedFrames.SlotDesc + SharedFrames.SlotDescBytes * slot;
            long seq = mem.ReadI64(d);
            if ((seq & 1) != 0)
                return null;
            var desc = new byte[48];
            mem.Read(d + 8, desc, 0, 48);
            var f = new FrameInfo
            {
                Frame = BitConverter.ToInt64(desc, 0),
                HostFrame = BitConverter.ToInt64(desc, 8),
                Width = BitConverter.ToInt32(desc, 16),
                Height = BitConverter.ToInt32(desc, 20),
                Near = BitConverter.ToSingle(desc, 24),
                Far = BitConverter.ToSingle(desc, 28),
                Fov = BitConverter.ToSingle(desc, 32),
                Flags = BitConverter.ToInt32(desc, 36),
                Daylight = BitConverter.ToSingle(desc, 40),
                Infection = BitConverter.ToSingle(desc, 44),
            };
            long bytes = (long)f.Width * f.Height * 4;
            if (f.Width <= 0 || f.Height <= 0 || bytes > stride)
                return null;
            if (pixels.Length != bytes)
                pixels = new byte[bytes];
            mem.Read(SharedFrames.Header + stride * slot, pixels, 0, (int)bytes);
            if (mem.ReadI64(d) != seq)
                return null; // lapped by the writer mid-copy
            lastPublish = publish;
            rgba = pixels;
            return f;
        }

        public void Dispose()
        {
            if (mem != null)
                mem.Dispose();
            mem = null;
        }
    }
}
