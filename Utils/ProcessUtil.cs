using System.Collections.Generic;
using System.Diagnostics;

namespace MemuDeezerClient.Utils
{
    internal class ProcessUtil
    {
        private static int sid = -1;

        public static Process[] GetProcesses()
        {
            if (sid == -1)
            {
                var process = Process.GetCurrentProcess();
                sid = process.SessionId;
            }

            var list = new List<Process>();
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                if (process.SessionId == sid)
                {
                    list.Add(process);
                }
            }

            return list.ToArray();
        }
    }
}
