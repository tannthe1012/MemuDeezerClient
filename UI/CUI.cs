using AutomationFramework;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemuDeezerClient.Managers;

namespace MemuDeezerClient.UI
{
    // Console User Interface
    internal class CUI
    {
        private class NativeConsole
        {
            private const int SWP_NOMOVE = 0x2;
            private const int SWP_NOSIZE = 0x1;
            private const int SWP_NOZORDER = 0x4;
            private const int SWP_NOACTIVATE = 0x10;

            [DllImport("kernel32")]
            private static extern IntPtr GetConsoleWindow();

            [DllImport("user32")]
            private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);

            [DllImport("user32")]
            private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rect);

            [StructLayout(LayoutKind.Sequential)]
            private readonly struct RECT
            {
                public int Left { get; }

                public int Top { get; }

                public int Right { get; }

                public int Bottom { get; }

                public int Width => Right - Left;

                public int Height => Bottom - Top;
            }

            public static void SetWindowRect(int x, int y, int w = -1, int h = -1)
            {
                var handle = GetConsoleWindow();
                var rect = new RECT();
                if (GetWindowRect(handle, ref rect))
                {
                    if (w < 0)
                        w = rect.Width;
                    if (h < 0)
                        h = rect.Height;
                    SetWindowPos(handle, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
                }
            }
        }

        private readonly AutomationApplication app;
        private readonly SPHManager sphm;

        private readonly string version;
        private readonly DateTime time;

        private readonly int thread_count;
        private readonly int page_size;
        private readonly int page_count;
        private int page_index;

        public CUI(AutomationApplication app)
        {
            this.app = app;
            this.sphm = app.GetSingleton<SPHManager>();

            version = $"{Build.ASSEMBLY_NAME}/{Build.ASSEMBLY_MAJOR_VERSION}.{Build.ASSEMBLY_MINOR_VERSION}(ALPHA 1)";
            time = DateTime.UtcNow;

            thread_count = app.ThreadManager.ThreadCount;
            page_size = 10;
            page_count = thread_count / page_size;
            page_index = 0;

            if (thread_count % page_size != 0)
            {
                page_count++;
            }
        }

        public void Start()
        {
            Console.CursorVisible = false;
            Console.Clear();
            NativeConsole.SetWindowRect(0, 385);

            Task.Run(ReadKey);

            while (true)
            {
                DrawData();
                DrawThread();

                Thread.Sleep(3000);
            }
        }

        private void ReadKey()
        {
            var key = Console.ReadKey(false);

            if (key.Key == ConsoleKey.RightArrow)
            {
                if (page_index < page_count - 1)
                {
                    page_index++;
                }
                else
                {
                    page_index = 0;
                }
                DrawThread();
            }
            else if (key.Key == ConsoleKey.LeftArrow)
            {
                if (page_index > 0)
                {
                    page_index--;
                }
                else
                {
                    page_index = page_count - 1;
                }
                DrawThread();
            }

            Task.Run(ReadKey);
        }

        private void DrawData()
        {
            var line = 0;
            var uptime = DateTime.UtcNow.Subtract(time);


            var total_failure = sphm.TotalFailure;
            var total_success = sphm.TotalSuccess;
            var total = total_success + total_failure;
            var sph = sphm.TotalSPH;

            DrawLine(line++, $" + VERSION      : {version}");
            DrawLine(line++, $" + UPTIME       : {uptime.Days:00}:{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}");
            DrawLine(line++, $" + SPH          : {sph}");
            DrawLine(line++, $" + TOTAL        : {total}");
            DrawLine(line++, $" + SUCCESS      : {total_success}");
            DrawLine(line++, $" + FAILURE      : {total_failure}");
            DrawLine(line++, string.Empty);
        }

        private void DrawThread()
        {
            var line = 07;
            var tm = app.ThreadManager;

            // TRUY VẤN DỮ LIỆU
            var cols = new string[] { "ID", "PROFILE", "PROXY", "STATUS" };
            var rows = new string[page_size][];
            {
                var threads = tm.GetAll<MainAsyncAutoThread>();

                for (var i = 0; i < page_size; i++)
                {
                    var index = page_index * page_size + i;
                    if (index < threads.Length)
                    {
                        var thread = threads[index];
                        rows[i] = new string[]
                        {
                            thread.ID.ToString("00"),
                            thread.Profile ?? "NULL",
                            thread.Proxy ?? "NULL",
                            thread.Status ?? "NULL"
                        };
                    }
                    else
                    {
                        rows[i] = new string[0];
                    }
                }
            }


            // TÍNH ĐỘ RỘNG CỘT
            var widths = new int[cols.Length];
            for (var i = 0; i < cols.Length; i++)
            {
                var def = cols[i].Length;
                var max = rows.Max((row) =>
                {
                    var val = i < row.Length ? row[i] : string.Empty;
                    return val.Length;
                });
                widths[i] = Math.Max(def, max);
            }

            // VẼ DÒNG TIÊU ĐỀ
            // ┌────┬───────────────────────────┬────────────────────────────────┬──────────────────┐
            // │ ID │ PROFILE                   │ PROXY                          │ STATUS           │
            // ├────┼───────────────────────────┼────────────────────────────────┼──────────────────┤
            {
                var sba = new StringBuilder(" ┌─");
                var sbb = new StringBuilder(" │ ");
                var sbc = new StringBuilder(" ├─");

                for (var i = 0; i < cols.Length; i++)
                {
                    var width = widths[i];
                    var cell = cols[i];
                    sba.Append('─', width);
                    sbb.Append(MakeText(cell, width));
                    sbc.Append('─', width);

                    if (i < cols.Length - 1)
                    {
                        sba.Append("─┬─");
                        sbb.Append(" │ ");
                        sbc.Append("─┼─");
                    }
                }

                sba.Append("─┐ ");
                sbb.Append(" │ ");
                sbc.Append("─┤ ");

                DrawLine(line++, sba.ToString());
                DrawLine(line++, sbb.ToString());
                DrawLine(line++, sbc.ToString());
            }

            // VẼ DÒNG NỘI DUNG
            // │ 01 │ bairammahmud@gmail.com    │ 45.192.141.165:5202            │ Enjoy [2]: 1s    │
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    var sb = new StringBuilder(" │ ");

                    var row = rows[i];
                    for (var j = 0; j < cols.Length; j++)
                    {
                        var width = widths[j];
                        var cell = j < row.Length ? row[j] : string.Empty;
                        sb.Append(MakeText(cell, width));

                        if (j < cols.Length - 1)
                        {
                            sb.Append(" │ ");
                        }
                    }

                    sb.Append(" │ ");

                    DrawLine(line++, sb.ToString());
                }
            }

            // VẼ DÒNG CHÂN TRANG
            // ├────┴───────────────────────────┴────────────────────────────────┴──────────────────┤
            // │ 20 Threads                                                            Page 01 / 01 │
            // └────────────────────────────────────────────────────────────────────────────────────┘
            {
                var sba = new StringBuilder(" ├─");
                var sbb = new StringBuilder(" │ ");
                var sbc = new StringBuilder(" └─");

                for (var i = 0; i < cols.Length; i++)
                {
                    var width = widths[i];
                    var col = cols[i];
                    sba.Append('─', width);
                    sbc.Append('─', width);

                    if (i < cols.Length - 1)
                    {
                        sba.Append("─┴─");
                        sbc.Append("───");
                    }
                }

                var txta = $"{tm.ThreadCount:00} Threads";
                var txtb = $"Page {page_index + 1:00} / {page_count:00}";
                var sum = widths.Sum() + (cols.Length - 1) * 3;
                sbb.Append(txta).Append(' ', sum - txta.Length - txtb.Length).Append(txtb);

                sba.Append("─┤ ");
                sbb.Append(" │ ");
                sbc.Append("─┘ ");

                DrawLine(line++, sba.ToString());
                DrawLine(line++, sbb.ToString());
                DrawLine(line++, sbc.ToString());
            }
        }

        private void DrawLine(int index, string text)
        {
            var width = Console.WindowWidth;
            var offset = width - text.Length;
            if (offset > 0)
            {
                text = string.Concat(text, new string(' ', offset));
            }
            else if (offset < 0)
            {
                text = text.Remove(width);
            }

            Console.SetCursorPosition(0, index);
            Console.Write(text);
        }

        private string MakeText(string text, int length)
        {
            var l = text.Length;
            if (l == length)
            {
                return text;
            }
            if (l > length)
            {
                return text.Substring(0, length);
            }
            else
            {
                return string.Concat(text, new string(' ', length - l));
            }
        }
    }
}
