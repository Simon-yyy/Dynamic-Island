using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland
{
    /// <summary>
    /// 只读监听 Windows 通知库，抓取微信（Weixin / WeChat）投递的 toast，
    /// 解析出发信人与消息摘要。不修改微信、不注入进程。
    /// 依赖系统自带的 winsqlite3.dll，因此不需要额外部署。
    /// </summary>
    internal static class WeChatFeed
    {
        public sealed class Message
        {
            public string Sender = "";
            public string Body = "";
            public DateTime Time = DateTime.Now;
            public long Order;
        }

        private static readonly string DbDir = ResolveDbDir();
        private static string ResolveDbDir()
        {
            // 允许用环境变量指向测试库，方便自检；默认读系统真实通知库
            string overrideDir = Environment.GetEnvironmentVariable("ISLAND_WPND_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Notifications");
        }

        private static readonly string WorkDir = Path.Combine(Path.GetTempPath(), "island_wpnd");
        private static readonly string WorkDb = Path.Combine(WorkDir, "wpnd.db");

        private static string _lastSig = "";
        private static bool _warned;

        // ---------------------------------------------------------------- sqlite3
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READONLY = 0x00000001;
        private const int SQLITE_OPEN_READWRITE = 0x00000002;

        // winsqlite3 用的是标准 UTF-8 (sqlite3_filename / const char*) 接口，
        // 必须按字节传递，不能用 CharSet.Unicode 交给 CLR 做宽字符封送。
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2")]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step")]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text")]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes")]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int index);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64")]
        private static extern long sqlite3_column_int64(IntPtr stmt, int index);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize")]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close")]
        private static extern int sqlite3_close(IntPtr db);

        // ------------------------------------------------------------------ public
        /// <summary>读取最近若干条微信通知（倒序，最新的在前）。失败时返回空列表。</summary>
        public static List<Message> Poll(int max)
        {
            var result = new List<Message>();
            try
            {
                // 优先只读打开原库；被占用/只能拷贝时再退回副本方案
                if (!QueryInto(DbDir + @"\wpndatabase.db", max, true, result))
                {
                    result.Clear();
                    if (SyncDatabase()) QueryInto(WorkDb, max, false, result);
                }
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    try
                    {
                        Directory.CreateDirectory(WorkDir);
                        File.AppendAllText(Path.Combine(WorkDir, "feed.log"),
                            DateTime.Now.ToString("s") + " " + ex.GetType().Name + ": " + ex.Message + "\r\n");
                    }
                    catch { }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------- sync
        private static bool SyncDatabase()
        {
            string db = Path.Combine(DbDir, "wpndatabase.db");
            if (!File.Exists(db)) return false;

            string sig = Signature(db);
            if (sig == _lastSig && File.Exists(WorkDb)) return true;

            Directory.CreateDirectory(WorkDir);
            CopyWithRetry(db, WorkDb);
            CopyWithRetry(db + "-wal", WorkDb + "-wal", true);
            CopyWithRetry(db + "-shm", WorkDb + "-shm", true);
            _lastSig = sig;
            return true;
        }

        private static string Signature(string db)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (string p in new[] { db, db + "-wal" })
                {
                    if (File.Exists(p))
                    {
                        var info = new FileInfo(p);
                        sb.Append(info.Length).Append(':')
                          .Append(info.LastWriteTimeUtc.Ticks).Append('|');
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static void CopyWithRetry(string src, string dst, bool optional = false)
        {
            if (!File.Exists(src))
            {
                if (optional) return;
                throw new FileNotFoundException(src);
            }

            string tmp = dst + ".tmp" + Guid.NewGuid().ToString("N");
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.Copy(src, tmp, true);
                    File.Copy(tmp, dst, true);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            throw new IOException("copy failed: " + src);
        }

        // ------------------------------------------------------------------ query
        private static bool QueryInto(string path, int max, bool readOnly, List<Message> target)
        {
            IntPtr db = IntPtr.Zero;
            int flags = readOnly ? SQLITE_OPEN_READONLY : SQLITE_OPEN_READWRITE;
            if (sqlite3_open_v2(Utf8(path), out db, flags, IntPtr.Zero) != SQLITE_OK)
            {
                if (db != IntPtr.Zero) sqlite3_close(db);
                return false;
            }

            IntPtr stmt = IntPtr.Zero;
            bool prepared = false;
            try
            {
                string sql =
                    "SELECT n.[Order], n.[Payload], n.[ArrivalTime] " +
                    "FROM [Notification] n JOIN [NotificationHandler] h ON h.[RecordId] = n.[HandlerId] " +
                    "WHERE h.[PrimaryId] LIKE 'Weixin%' " +
                    "ORDER BY n.[Order] DESC LIMIT " + Math.Max(1, max);

                byte[] sqlBytes = Utf8(sql);
                if (sqlite3_prepare_v2(db, sqlBytes, sqlBytes.Length, out stmt, IntPtr.Zero) != SQLITE_OK) return false;
                prepared = true;

                while (sqlite3_step(stmt) == SQLITE_ROW)
                {
                    long order = sqlite3_column_int64(stmt, 0);
                    var payload = ReadColumn(stmt, 1);
                    long arrival = sqlite3_column_int64(stmt, 2);

                    var msg = ParsePayload(payload);
                    if (msg == null) continue;
                    msg.Order = order;
                    if (arrival > 0)
                    {
                        try { msg.Time = DateTime.FromFileTimeUtc(arrival).ToLocalTime(); }
                        catch { }
                    }
                    target.Add(msg);
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
                sqlite3_close(db);
            }
            return prepared;
        }

        private static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        private static string ReadColumn(IntPtr stmt, int index)
        {
            IntPtr ptr = sqlite3_column_text(stmt, index);
            if (ptr == IntPtr.Zero) return "";
            int len = sqlite3_column_bytes(stmt, index);
            if (len <= 0) return "";
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        private static readonly Regex TextNode = new Regex(
            @"<text[^>]*>(.*?)</text>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex Tag = new Regex(@"<[^>]+>", RegexOptions.Singleline);

        private static Message ParsePayload(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            var ms = TextNode.Matches(payload);
            if (ms.Count == 0) return null;

            var msg = new Message();
            msg.Sender = CleanText(ms[0].Groups[1].Value);

            var body = new StringBuilder();
            for (int i = 1; i < ms.Count; i++)
            {
                string piece = CleanText(ms[i].Groups[1].Value);
                if (piece.Length == 0) continue;
                if (body.Length > 0) body.Append(" / ");
                body.Append(piece);
            }
            msg.Body = body.ToString();
            if (msg.Sender.Length == 0 && msg.Body.Length == 0) return null;
            if (msg.Body.Length == 0) msg.Body = "收到一条消息";
            return msg;
        }

        private static string CleanText(string raw)
        {
            string s = WebUtility.HtmlDecode(Tag.Replace(raw, ""));
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s;
        }

        // -------------------------------------------------------------- activation
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

        /// <summary>把微信主窗口拉到前台。</summary>
        public static bool OpenWeChat()
        {
            try
            {
                IntPtr h = FindWindow("Qt51514QWindowIcon", "微信");
                if (h == IntPtr.Zero) h = FindWindow("WeChatMainWndForPC", "微信");
                if (h == IntPtr.Zero) return false;
                ShowWindow(h, 9);   // SW_RESTORE
                SetForegroundWindow(h);
                return true;
            }
            catch { return false; }
        }
    }
}
