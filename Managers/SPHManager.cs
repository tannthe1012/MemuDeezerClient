using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MemuDeezerClient.Managers
{
    public class SPHManager
    {
        private readonly List<long> sph;
        private readonly Timer backup_sph_timer;

        private int total_success = 0;
        private int total_failure = 0;

        public int TotalSuccess => total_success;

        public int TotalFailure => total_failure;

        public int TotalSPH => CalculateSPH();

        public SPHManager()
        {
            sph = new List<long>();

            // KHÔI PHỤC DỮ LIỆU SPH.
            {
                var sph_file = Path.Combine(Build.BASE_DIR, "data\\sph.json");

                try
                {
                    var json = File.ReadAllText(sph_file);
                    var data = JsonConvert.DeserializeObject<long[]>(json);
                    var hour = TimeSpan.FromHours(1);
                    var now = DateTimeOffset.Now;
                    var offset = now.Subtract(hour).ToUnixTimeSeconds();
                    if (data != null)
                    {
                        foreach (var d in data)
                        {
                            if (d >= offset)
                            {
                                sph.Add(d);
                            }
                        }
                    }
                }
                catch { }
            }

            // KHỞI CHẠY BỘ HẸN GIỜ.
            backup_sph_timer = new Timer(OnBackupSPH, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
        }

        /// <summary>
        /// Hàm được gọi mỗi 3 phút, có nhiệm vụ sao lưu dữ liệu đếm SPH.
        /// </summary>
        private void OnBackupSPH(object state)
        {
            var data_dir = Path.Combine(Build.BASE_DIR, "data");
            var sph_file = Path.Combine(data_dir, "sph.json");

            Directory.CreateDirectory(data_dir);

            try
            {
                var json = JsonConvert.SerializeObject(sph);
                File.WriteAllText(sph_file, json);
            }
            catch { }
        }

        public void IncreaseSuccess()
        {
            Interlocked.Increment(ref total_success);
            IncreaseSPH();
        }

        public void IncreaseFailure()
        {
            Interlocked.Increment(ref total_failure);
        }

        private void IncreaseSPH()
        {
            lock (sph)
            {
                var now = DateTimeOffset.UtcNow;
                var sec = now.ToUnixTimeSeconds();
                sph.Add(sec);
            }
        }

        private int CalculateSPH()
        {
            lock (sph)
            {
                var hour = TimeSpan.FromHours(1);
                var now = DateTimeOffset.Now;
                var offset = now.Subtract(hour).ToUnixTimeSeconds();
                sph.RemoveAll((x) => x < offset);
                return sph.Count;
            }
        }
    }
}
