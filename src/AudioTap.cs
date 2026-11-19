using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DynamicIsland
{
    /// <summary>
    /// 用 WASAPI 回环（loopback）抓取系统正在播放的声音，做 FFT 得到频谱，
    /// 供灵动岛画“音律”条。只监听默认播放设备，不改动任何播放器。
    /// </summary>
    internal static class AudioTap
    {
        public const int BandCount = 28;
        private const int FftSize = 2048;

        /// <summary>每个频段的电平 0..1，由 UI 侧直接读取绘制。</summary>
        public static readonly float[] Bands = new float[BandCount];

        /// <summary>当前整体音量 0..1（用来判断有没有在放音乐）。</summary>
        public static volatile float Level;
        public static volatile bool Signal;
        public static volatile bool Running;

        private static Thread _thread;
        private static volatile bool _stop;

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public static void Stop()
        {
            _stop = true;
        }

        // -------------------------------------------------------- WASAPI interop
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            int GetDevice(string id, out IMMDevice device);
            int RegisterEndpointNotificationCallback(IntPtr client);
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            int OpenPropertyStore(int access, out IntPtr props);
            int GetId(out IntPtr id);
            int GetState(out int state);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
        private interface IAudioClient
        {
            int Initialize(int shareMode, int streamFlags, long hnsBufferDuration,
                long hnsPeriodicity, IntPtr format, IntPtr sessionGuid);
            int GetBufferSize(out uint frames);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long defaultPeriod, out long minPeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr handle);
            int GetService(ref Guid iid, out IntPtr service);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
        private interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePos, out long qpcPos);
            int ReleaseBuffer(int frames);
            int GetNextPacketSize(out int frames);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        private static readonly Guid IID_IAudioClient =
            new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient =
            new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        // -------------------------------------------------------------- capture
        private static void Loop()
        {
            while (!_stop)
            {
                try
                {
                    CaptureUntilBroken();
                }
                catch (Exception)
                {
                }
                Running = false;
                for (int i = 0; i < BandCount; i++) Bands[i] = 0;
                Signal = false;
                Level = 0;
                if (_stop) return;
                Thread.Sleep(4000);   // 设备抖动/切换后重连
            }
        }

        private static void CaptureUntilBroken()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out dev) != 0) return;

            object obj;
            Guid iid = IID_IAudioClient;
            if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out obj) != 0) return;
            IAudioClient client = (IAudioClient)obj;

            IntPtr mixPtr;
            if (client.GetMixFormat(out mixPtr) != 0 || mixPtr == IntPtr.Zero) return;

            WAVEFORMATEX fmt = (WAVEFORMATEX)Marshal.PtrToStructure(mixPtr, typeof(WAVEFORMATEX));
            int channels = Math.Max(1, (int)fmt.nChannels);
            int rate = fmt.nSamplesPerSec;
            int blockAlign = fmt.nBlockAlign;
            bool isFloat = fmt.wFormatTag == 3 || fmt.wBitsPerSample == 32;

            // 共享模式 + 回环，缓冲 1 秒，周期 0（共享模式要求）
            int hr = client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK,
                10000000L, 0, mixPtr, IntPtr.Zero);
            Marshal.FreeCoTaskMem(mixPtr);
            if (hr != 0) return;

            Guid capIid = IID_IAudioCaptureClient;
            IntPtr capPtr;
            if (client.GetService(ref capIid, out capPtr) != 0 || capPtr == IntPtr.Zero) return;
            IAudioCaptureClient cap = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capPtr);
            Marshal.Release(capPtr);

            if (client.Start() != 0) return;
            Running = true;

            float[] mono = new float[FftSize];
            int monoLen = 0;
            float[] frame = new float[FftSize];
            var fft = new Fft(FftSize);
            float[] bandsRaw = new float[BandCount];
            float[] bandsSmooth = new float[BandCount];
            float[] prev = new float[BandCount];
            int lastEmit = Environment.TickCount;
            int lastAudioDataTick = Environment.TickCount;

            try
            {
                while (!_stop)
                {
                    Thread.Sleep(8);

                    int packets;
                    cap.GetNextPacketSize(out packets);
                    while (packets > 0)
                    {
                        IntPtr data;
                        int frames, flags;
                        long devPos, qpcPos;
                        if (cap.GetBuffer(out data, out frames, out flags, out devPos, out qpcPos) != 0) break;

                        if (frames > 0 && data != IntPtr.Zero)
                        {
                            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                            {
                                monoLen = 0;   // 静音物理清空缓冲
                                Array.Clear(mono, 0, mono.Length);
                            }
                            else if (isFloat)
                            {
                                lastAudioDataTick = Environment.TickCount;
                                int total = frames * channels;
                                float[] buf = new float[total];
                                Marshal.Copy(data, buf, 0, total);
                                for (int f = 0; f < frames; f++)
                                {
                                    float acc = 0f;
                                    int baseIdx = f * channels;
                                    for (int c = 0; c < channels; c++) acc += buf[baseIdx + c];
                                    mono[monoLen] = acc / channels;
                                    monoLen++;
                                    if (monoLen >= FftSize) monoLen = 0;
                                }
                            }
                        }
                        cap.ReleaseBuffer(frames);
                        cap.GetNextPacketSize(out packets);
                    }

                    if (Environment.TickCount - lastEmit < 40) continue;
                    lastEmit = Environment.TickCount;

                    // 若超过 150ms 无有效音频包（如暂停或停止播放），将电平与频谱快速归零
                    if (Environment.TickCount - lastAudioDataTick > 150)
                    {
                        monoLen = 0;
                        Array.Clear(mono, 0, mono.Length);
                        Array.Clear(frame, 0, frame.Length);
                        Level = 0f;
                        Signal = false;
                        for (int b = 0; b < BandCount; b++)
                        {
                            bandsSmooth[b] *= 0.6f;
                            if (bandsSmooth[b] < 0.001f) bandsSmooth[b] = 0f;
                            Bands[b] = bandsSmooth[b];
                        }
                        continue;
                    }

                    // 组装一帧（环形缓冲 → 线性）
                    for (int i = 0; i < FftSize; i++) frame[i] = mono[(monoLen + i) % FftSize];

                    float peak = 0f;
                    for (int i = 0; i < FftSize; i++)
                    {
                        float a = Math.Abs(frame[i]);
                        if (a > peak) peak = a;
                    }
                    Level = Math.Min(1f, peak * 3f);

                    fft.Run(frame);
                    MapBands(fft.Magnitude, rate, bandsRaw);

                    // 自适应归一（跟着最近的最大值走），再做攻击/释放平滑
                    for (int b = 0; b < BandCount; b++)
                    {
                        float target = bandsRaw[b];
                        float cur = bandsSmooth[b];
                        float up = 0.35f, down = 0.10f;
                        float v = target > cur ? cur + (target - cur) * up : cur + (target - cur) * down;
                        bandsSmooth[b] = v;
                        prev[b] = cur;
                        Bands[b] = v;
                    }
                    Signal = peak > 0.0008f;
                }
            }
            finally
            {
                try { client.Stop(); } catch { }
                try { Marshal.ReleaseComObject(cap); } catch { }
                try { Marshal.ReleaseComObject(client); } catch { }
            }
        }

        /// <summary>把线性幅度谱映射到对数分布的频段（~60Hz 到 12kHz）。</summary>
        private static void MapBands(float[] mag, int rate, float[] outBands)
        {
            int bins = mag.Length / 2;
            double binHz = (double)rate / mag.Length;
            double lo = 60.0, hi = Math.Min(12000.0, rate * 0.45);

            for (int b = 0; b < outBands.Length; b++)
            {
                double f0 = lo * Math.Pow(hi / lo, (double)b / outBands.Length);
                double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / outBands.Length);
                int i0 = Math.Max(1, (int)(f0 / binHz));
                int i1 = Math.Min(bins - 1, Math.Max(i0 + 1, (int)(f1 / binHz)));

                double sum = 0;
                for (int i = i0; i < i1; i++) sum += mag[i];
                double avg = sum / (i1 - i0);

                // 转成分贝再压缩到 0..1
                double db = 20.0 * Math.Log10(avg + 1e-9);
                double t = (db + 52.0) / 46.0;      // -52dB → 0, -6dB → 1
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                outBands[b] = (float)(t * t * (3 - 2 * t));   // smoothstep，弱信号更收
            }
        }

        // ------------------------------------------------------------ FFT (radix-2)
        private sealed class Fft
        {
            private readonly int _n;
            private readonly float[] _cos;
            private readonly float[] _sin;
            private readonly int[] _rev;
            public readonly float[] Magnitude;

            public Fft(int n)
            {
                _n = n;
                _cos = new float[n / 2];
                _sin = new float[n / 2];
                for (int i = 0; i < n / 2; i++)
                {
                    _cos[i] = (float)Math.Cos(-2 * Math.PI * i / n);
                    _sin[i] = (float)Math.Sin(-2 * Math.PI * i / n);
                }
                _rev = new int[n];
                int bits = 0;
                while ((1 << bits) < n) bits++;
                for (int i = 0; i < n; i++)
                {
                    int r = 0;
                    for (int b = 0; b < bits; b++)
                        if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                    _rev[i] = r;
                }
                Magnitude = new float[n];
            }

            private readonly float[] _re = new float[1];
            private readonly float[] _im = new float[1];

            public void Run(float[] samples)
            {
                float[] re = new float[_n];
                float[] im = new float[_n];

                // Hann 窗 + 位反转重排
                for (int i = 0; i < _n; i++)
                {
                    double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (_n - 1)));
                    re[_rev[i]] = (float)(samples[i] * w);
                    im[_rev[i]] = 0f;
                }

                for (int size = 2; size <= _n; size <<= 1)
                {
                    int half = size >> 1;
                    int step = _n / size;
                    for (int i = 0; i < _n; i += size)
                    {
                        int k = 0;
                        for (int j = i; j < i + half; j++)
                        {
                            float tRe = re[j + half] * _cos[k] - im[j + half] * _sin[k];
                            float tIm = re[j + half] * _sin[k] + im[j + half] * _cos[k];
                            re[j + half] = re[j] - tRe;
                            im[j + half] = im[j] - tIm;
                            re[j] += tRe;
                            im[j] += tIm;
                            k += step;
                        }
                    }
                }

                for (int i = 0; i < _n; i++)
                {
                    double m = Math.Sqrt(re[i] * re[i] + im[i] * im[i]) / (_n * 0.25);
                    Magnitude[i] = (float)m;
                }
            }
        }
    }
}
