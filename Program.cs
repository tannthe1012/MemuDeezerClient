using AutomationFramework;
using BoosterClient;
using BoosterClient.Managers;
using MEmuSharp;
using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MemuDeezerClient.Managers;
using MemuDeezerClient.Services;
using MemuDeezerClient.UI;
using MemuDeezerClient.Utils;

namespace MemuDeezerClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Title = Build.PRODUCT_NAME;

            // KHÓA CHƯƠNG TRÌNH
            Console.WriteLine(" >> Lock Application");
            {
                try
                {
                    File.Create(Build.LOCK_FILE);
                }
                catch
                {
                    return;
                }
            }

            // ĐĂNG KÍ KHỞI ĐỘNG CÙNG HỆ ĐIỀU HÀNH
            Console.WriteLine(" >> Register Startup");
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                    {
                        key.SetValue(Build.ASSEMBLY_NAME, Build.ASSEMBLY_FILE, RegistryValueKind.String);
                    }
                }
                catch { }
            }

            // KHỞI CHẠY CHƯƠNG TRÌNH BẢO VỆ VANGUARD
            Console.WriteLine(" >> Start Vanguard");
            {
                if (File.Exists(Build.VANGUARD_FILE))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Build.VANGUARD_FILE,
                        WorkingDirectory = Build.BASE_DIR
                    }).Dispose();
                }
            }

            // KIỂM TRA CẬP NHẬT
            Console.WriteLine(" >> Check Update");
            {
                if (UpdateService.CheckUpdate() && UpdateService.StartUpdate())
                {
                    return;
                }
            }

            // CHUẨN BỊ MEMU
            Console.WriteLine(" >> Prepare MEmu");
            {
                var processes = ProcessUtil.GetProcesses();
                foreach (var process in processes)
                {
                    var name = process.ProcessName;
                    if (string.Equals(name, "MEmuConsole"))
                    {
                        process.Kill();
                    }
                }
            }

            // CẤU HÌNH MEMU
            Console.WriteLine(" >> Configution MEmu");
            {
                var cfg = Config.Instance;
                var memu = new MEmuPlayer(cfg.MEmuDirectory);

                // Cấu Hình MEmu
                memu.UnfreezeGlobalConfig();
                memu.SetGlobalConfig(MEmuGlobalConfigKey.SECTION_PREFERENCE, new Dictionary<string, string>()
                {
                    ["adtime"] = "0",
                    ["adurl"] = "2",
                    ["adweburl"] = string.Empty,
                    ["as"] = string.Empty,
                    ["dlhost"] = string.Empty,
                    ["fdk"] = string.Empty,
                    ["fdkurl"] = string.Empty,
                    ["ntime"] = "0",
                    ["ntip"] = string.Empty,
                    ["nurl"] = string.Empty,
                    ["guestNotification"] = "false",
                    ["sleepMode"] = "3",
                    ["console_sort"] = "1",
                    ["i71"] = "2022103100023FFF"
                });
                memu.FreezeGlobalConfig();

                // Vô Hiệu Hóa ADB
                var adb_file = Path.Combine(cfg.MEmuDirectory, "adb.exe");
                if (File.Exists(adb_file))
                {
                    var bak_file = Path.Combine(cfg.MEmuDirectory, "adb.bak.exe");
                    if (File.Exists(bak_file))
                    {
                        File.Delete(bak_file);
                    }
                    File.Move(adb_file, bak_file);
                }
            }

            // CHẠY ỨNG DỤNG
            Console.WriteLine(" >> Run Application");
            {
                var cfg = Config.Instance;

                // điều chỉnh số luồng dựa trên OS.
                {
                    var os = Environment.OSVersion;
                    var version = os.Version;
                    var major = version.Major;
                    var minor = version.Minor;

                    // trường hợp windows 8.1 (6.2, 6.3)
                    //if (major == 6 && (minor == 2 || minor == 3) && cfg.ThreadCount >= 15 && cfg.ThreadCount <= 20)
                    //{
                    //    cfg.ThreadCount = 20;
                    //}
                }

                // điều chỉnh số luồng dựa trên RAM.
                {
                    var info = new ComputerInfo();
                    var mem = info.TotalPhysicalMemory / 1024f / 1024f / 1024f;
                    if (mem < 80 && cfg.ThreadCount > 10)
                    {
                        //cfg.ThreadCount = 1;
                        cfg.ThreadCount = 10;

                    }
                    else if (mem < 100 && cfg.ThreadCount > 12)
                    {
                        cfg.ThreadCount = 12;
                        //cfg.ThreadCount = 1;

                    }
                }

                var app = AutomationApplication.CreateBuilder()
                    .AddSingleton<APIClient>()
                    .AddSingleton<ClientManager>()
                    .AddSingleton<ProfileManager>()
                    .AddSingleton<ProfileSessionManager>()
                    .AddSingleton<SourceManager>()
                    .AddSingleton<SourcePoolManager>()
                    .AddSingleton<ProxyManager>()
                    .AddSingleton<SettingManager>()
                    .AddSingleton<SPHManager>()
                    .AddSingleton<MachineManager>()
                    .AddService<UpdateService>()
                    .SetThread<MainAsyncAutoThread>((opts) =>
                    {
                        opts.ThreadCount = cfg.ThreadCount;
                        opts.StartDelay = 3000;
                        opts.StopDelay = 3000;
                        opts.RestartDelay = 3000;
                    })
                    .Build();

                // Chạy ứng dụng.
                _ = app.StartAsync();

                // Chạy giao diện.
                if (!cfg.DebugMode)
                {
                    var cui = new CUI(app);
                    cui.Start();
                }
                else
                {
                    while (true)
                    {
                        Thread.Sleep(1000);
                    }
                }

                // Dừng ứng dụng.
                app.Stop();
            }
        }
    }
}