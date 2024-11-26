using AutomationFramework;
using AutomationFramework.Service;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MemuDeezerClient.Properties;

namespace MemuDeezerClient.Services
{
    internal class UpdateService : ScheduleService
    {
        private readonly AutomationApplication app;

        public UpdateService(AutomationApplication app) : base(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3))
        {
            this.app = app;
        }

        public override Task ExecuteAsync(CancellationToken token)
        {
            if (!token.IsCancellationRequested && CheckUpdate())
            {
                // Dừng Ứng Dụng Trong 3 Phút
                app.StopAsync().Wait(TimeSpan.FromMinutes(3));

                // Chạy Cập Nhật
                StartUpdate();

                // Thoát Chương Trình
                Environment.Exit(0);
            }
            return Task.CompletedTask;
        }

        public static bool CheckUpdate()
        {
            //if (!File.Exists(Build.UPDATE_FILE))
            //{
            //    return false;
            //}
            if (Build.IS_LITE)
            {
                return false;
            } 


            try
            {
                using (var http = new HttpClient())
                {
                    var data = http.GetStringAsync(Build.UPDATE_URL).Result;
                    var manifest = JObject.Parse(data);

                    var major = manifest.Value<int>("MajorVersion");
                    var minor = manifest.Value<int>("MinorVersion");

                    return major > Build.ASSEMBLY_MAJOR_VERSION || major == Build.ASSEMBLY_MAJOR_VERSION && minor > Build.ASSEMBLY_MINOR_VERSION;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool StartUpdate()
        {
            try
            {
                // write update executable.
                var bytes = Resources.MemuDeezerUpdate;
                File.WriteAllBytes(Build.UPDATE_FILE, bytes);

                // start update executable.
                Process.Start(new ProcessStartInfo()
                {
                    FileName = Build.UPDATE_FILE,
                    WorkingDirectory = Build.BASE_DIR
                }).Dispose();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
