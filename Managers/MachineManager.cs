using MemuHyperv;
using MEmuSharp;
using MEmuSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Principal;
using System.Threading;
using VBoxAPI = MemuHyperv.MemuHyperv;

namespace MemuDeezerClient.Managers
{
    internal class MachineManager
    {
        private readonly VBoxAPI vbox;
        private readonly MEmuPlayer memu;
        private readonly SemaphoreSlim semaphore;

        private readonly ConcurrentDictionary<int, Process> core_process;
        private readonly ConcurrentDictionary<int, Process> memu_process;

        private int uid = -1;

        public MachineManager()
        {
            semaphore = new SemaphoreSlim(1, 1);
            vbox = new VBoxAPI();
            memu = new MEmuPlayer(Config.Instance.MEmuDirectory);

            core_process = new ConcurrentDictionary<int, Process>();
            memu_process = new ConcurrentDictionary<int, Process>();

            // thay đổi vị trí lưu máy ảo.
            vbox.SystemProperties.DefaultMachineFolder = Path.Combine(Config.Instance.MEmuDirectory, "MemuHyperv VMs");

            // cấu hình ổ đĩa disk 1.
            var disk_file = Path.Combine(Config.Instance.MEmuDirectory, "image\\71\\MEmu71-2022103100023FF-disk1.vmdk");
            var medium = vbox.OpenMedium(disk_file, DeviceType.DeviceType_HardDisk, AccessMode.AccessMode_ReadOnly, 0);
            if (medium.Type != MediumType.MediumType_Readonly)
            {
                medium.Type = MediumType.MediumType_Readonly;
            }
        }

        public void CreateMachine(int tid)
        {
            var mid = GetMachineID(tid);
            var unique = mid.ToString().PadLeft(12, '0');
            var mname = mid == 0 ? "MEmu" : string.Concat("MEmu_", mid);
            var muuid = string.Concat("00000000-aaaa-aaaa-aaaa-", unique);

            // Thử Tìm Máy Ảo.
            IMachine core = null;
            try
            {
                core = vbox.FindMachine(muuid);
            }
            catch { }

            // Hủy Đăng Kí Máy Nếu Không Tìm Thấy Tệp Cấu Hình.
            if (core != null && !File.Exists(core.SettingsFilePath))
            {
                core.Unregister(CleanupMode.CleanupMode_DetachAllReturnNone);
                core = null;
            }

            // Tạo Máy Ảo Nếu Không Tìm Thấy.
            if (core == null)
            {
                // Tạo Máy Ảo.
                core = vbox.CreateMachine(null, mname, null, "Linux", $"forceOverwrite=1,UUID={muuid}");

                // Cấu Hình Extra Data.
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeAudio/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeCommand/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeDevice/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeIme/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeMemud/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/Devices/MEmuPipeVInput/0/Trusted", "1");
                core.SetExtraData("MEmuInternal/TM/TSCMode", "RealTSCOffset");

                // Cấu Hình Phần Cứng.
                core.CPUCount = 1;
                core.MemorySize = 1024;
                core.VRAMSize = 12;
                core.PointingHIDType = PointingHIDType.PointingHIDType_USBTablet;
                core.SetHWVirtExProperty(HWVirtExPropertyType.HWVirtExPropertyType_LargePages, 1);

                // Cấu Hình Boot Order.
                core.SetBootOrder(1, DeviceType.DeviceType_HardDisk);
                core.SetBootOrder(2, DeviceType.DeviceType_Null);
                core.SetBootOrder(3, DeviceType.DeviceType_Null);
                core.SetBootOrder(4, DeviceType.DeviceType_Null);

                // Cấu Hình BIOS.
                var bios = core.BIOSSettings;
                bios.IOAPICEnabled = 1;
                bios.LogoFadeIn = 0;
                bios.LogoFadeOut = 0;
                bios.LogoDisplayTime = 0;
                bios.BootMenuMode = BIOSBootMenuMode.BIOSBootMenuMode_Disabled;

                // Cấu Hình USB Controller.
                core.AddUSBController("OHCI", USBControllerType.USBControllerType_OHCI);

                // Cấu Hình Network Adapter.
                var net = core.GetNetworkAdapter(0);
                net.InternalNetwork = "intnet";
                net.NATNetwork = "NatNetwork";
                net.MACAddress = "7A732D9293F4";
                net.AdapterType = NetworkAdapterType.NetworkAdapterType_Virtio;
                net.CableConnected = 1;

                // Chuyển Tiếp Cổng.
                var nat = net.NATEngine;
                nat.AddRedirect("LDIUM", NATProtocol.NATProtocol_TCP, "127.0.0.1", (ushort)(4444 + mid), "10.0.2.15", 4444);

                // Cấu Hình Audio Adapter.
                var audio = core.AudioAdapter;
                audio.AudioDriver = AudioDriverType.AudioDriverType_Null;

                // Cấu Hình Thời Gian.
                core.RTCUseUTC = 1;

                // Cấu Hình Storage Controller.
                var storage = core.AddStorageController("IDE", StorageBus.StorageBus_IDE);
                storage.ControllerType = StorageControllerType.StorageControllerType_PIIX4;
                storage.PortCount = 2;
                storage.UseHostIOCache = 1;
                core.SetStorageControllerBootable(storage.Name, 1);

                // Cấu Hình Guest Property.
                core.SetGuestPropertyValue(MEmuConfigKey.NAME, string.Concat("M", mid));
                core.SetGuestPropertyValue(MEmuConfigKey.FPS, "25");
                core.SetGuestPropertyValue(MEmuConfigKey.VSYNC, "0");
                core.SetGuestPropertyValue(MEmuConfigKey.SHOW_TABBAR, "0");
                core.SetGuestPropertyValue(MEmuConfigKey.PHONE_LAYOUT, "2");
                core.SetGuestPropertyValue(MEmuConfigKey.IS_HIDE_TOOLBAR, "0");
                core.SetGuestPropertyValue(MEmuConfigKey.IS_FULL_SCREEN, "0");
                core.SetGuestPropertyValue(MEmuConfigKey.CURSOR_TYPE, "0");
                core.SetGuestPropertyValue(MEmuConfigKey.WINDPI_SCALE, "0");

                core.SetGuestPropertyValue(MEmuConfigKey.CPUS, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.MEMORY, "1024");
                core.SetGuestPropertyValue(MEmuConfigKey.GRAPHICS_RENDER_MODE, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.GPU_MEM_OPTIMIZE, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.ASTC, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.ASTC_DECODE, "2");
                core.SetGuestPropertyValue(MEmuConfigKey.ENABLE_SU, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.MIC_NAME_MD5, "NoSound");
                core.SetGuestPropertyValue(MEmuConfigKey.SPEAKER_NAME_MD5, "NoSound");
                
                core.SetGuestPropertyValue(MEmuConfigKey.DISK_ATTACH_MODE, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.IS_CUSTOMED_RESOLUTION, "1");
                core.SetGuestPropertyValue(MEmuConfigKey.RESOLUTION_WIDTH, "1080");
                core.SetGuestPropertyValue(MEmuConfigKey.RESOLUTION_HEIGHT, "1920");
                core.SetGuestPropertyValue(MEmuConfigKey.DPI, "360");

                core.SetGuestPropertyValue(MEmuConfigKey.LATITUDE, "");
                core.SetGuestPropertyValue(MEmuConfigKey.LONGITUDE, "");

                core.SetGuestPropertyValue(MEmuConfigKey.BRAND, "");
                core.SetGuestPropertyValue(MEmuConfigKey.MANUFACTURER, "");
                core.SetGuestPropertyValue(MEmuConfigKey.MODEL, "");

                core.SetGuestPropertyValue(MEmuConfigKey.OPERATOR_ISO, "us");
                core.SetGuestPropertyValue(MEmuConfigKey.OPERATOR_MCC, "310");
                core.SetGuestPropertyValue(MEmuConfigKey.OPERATOR_MNC, "04");
                core.SetGuestPropertyValue(MEmuConfigKey.OPERATOR_NETWORK, "Verizon Wireless");
                core.SetGuestPropertyValue(MEmuConfigKey.OPERATOR_NETWORK2, "Verizon Wireless");

                // Cấu Hình Shared Folder
                var shared_folder = Path.Combine(Build.BASE_DIR, "shared");
                core.SetGuestPropertyValue(MEmuConfigKey.MUSIC_PATH, Path.Combine(shared_folder, "music"));
                core.SetGuestPropertyValue(MEmuConfigKey.MOVIE_PATH, Path.Combine(shared_folder, "movie"));
                core.SetGuestPropertyValue(MEmuConfigKey.PICTURE_PATH, Path.Combine(shared_folder, "picture"));
                core.SetGuestPropertyValue(MEmuConfigKey.DOWNLOAD_PATH, Path.Combine(shared_folder, "download"));

                core.SetGuestPropertyValue("channel", "cd5e1e15");
                core.SetGuestPropertyValue("gui_language", "2");
                core.SetGuestPropertyValue("selected_map", "1");
                core.SetGuestPropertyValue("root_mode", "0");

                // Đăng Kí Máy Ảo.
                core.SaveSettings();
                vbox.RegisterMachine(core);
            }

            // Cấu Hình Máy Ảo
            var state = core.State;
            if (state == MachineState.MachineState_Aborted || state == MachineState.MachineState_PoweredOff)
            {
                var sess = new Session();
                core.LockMachine(sess, LockType.LockType_Write);
                try
                {
                    core = sess.Machine;

                    // Kiểm Tra Ổ Đĩa Disk 2
                    {
                        var mlocation = Path.GetDirectoryName(core.SettingsFilePath);
                        var disk_file = Path.Combine(mlocation, "MEmu71-2022103100023FF-disk2.vmdk");
                        var base_file = Path.Combine(Config.Instance.MEmuDirectory, "image\\71\\MEmu71-2022103100023FF-disk2.vmdk");

                        var file_info = new FileInfo(disk_file);
                        if (!file_info.Exists || DateTime.Now.Subtract(file_info.CreationTime).TotalDays >= 3)
                        {
                            File.Copy(base_file, disk_file, true);
                            file_info.CreationTime = DateTime.Now;
                        }

                        try
                        {
                            var medium = core.GetMedium("IDE", 0, 1);
                            if (medium != null && medium.State == MediumState.MediumState_Inaccessible)
                            {
                                core.DetachDevice("IDE", 0, 1);
                            }
                        }
                        catch { }
                    }

                    // Lưu Cài Đặt.
                    core.SaveSettings();
                }
                finally
                {
                    sess.UnlockMachine();
                }
            }
        }

        public void DeleteMachine(int tid)
        {
            StopMachine(tid);

            var mid = GetMachineID(tid);
            var machine = memu.GetMachine(mid);
            if (machine != null)
            {
                machine.Delete();
            }
        }

        public MEmuMachine StartMachine(int tid)
        {
            // Tạo Máy Ảo.
            CreateMachine(tid);

            // Lấy Máy Theo ID.
            var mid = GetMachineID(tid);
            var machine = memu.GetMachine(mid);

            //// Đóng Máy Nếu Cần.
            //var process = GetCoreProcess(tid);
            //if (process != null)
            //{
            //    var uptime = DateTime.Now.Subtract(process.StartTime)
            //    if (uptime >= TimeSpan.FromHours(10))
            //    {
            //        // giết máy ảo.
            //        KillMachine(tid);

            //        // ngủ 5 giây.
            //        Thread.Sleep(5000);

            //        // khởi động máy ảo.
            //        return StartMachine(tid);
            //    }
            //}

            // khởi động máy ảo nếu cần.
            if (!IsMachineRunning(tid))
            {
                // giết máy ảo.
                KillMachine(tid);

                // cấu hình máy ảo.
                machine.SetStartWindowPosition(tid % 5 * 193, 0, 192);
                machine.SetConfig(MEmuConfigKey.IS_HIDE_TOOLBAR, "1");

                // đợi tới lượt.
                semaphore.Wait();
                try
                {
                    var exe = Path.Combine(memu.MEmuDirectory, "MEmu.exe");
                    var args = mid == 0 ? "MEmu" : string.Concat("MEmu_", mid);
                    if (tid >= 5)
                    {
                        args = string.Concat(args, " -b");
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        WorkingDirectory = memu.MEmuDirectory,
                        UseShellExecute = true
                    }).Dispose();

                    MEmuShell shell = null;
                    try
                    {
                        for (var i = 30; true; i--)
                        {
                            if (i <= 0)
                            {
                                throw new TimeoutException("Failed to start machine after 5 minutes.");
                            }

                            Thread.Sleep(10000);

                            if (shell == null)
                            {
                                try
                                {
                                    shell = machine.CreateShell();
                                }
                                catch
                                {
                                    continue;
                                }
                            }

                            try
                            {
                                var text = shell.Execute("getprop sys.boot_completed").Trim();
                                if (string.Equals(text, "1"))
                                {
                                    break;
                                }
                            }
                            catch
                            {
                                shell.Dispose();
                                shell = null;
                            }
                        }
                    }
                    finally
                    {
                        shell?.Dispose();
                    }
                }
                catch (TimeoutException)
                {
                    // xóa máy ảo.
                    DeleteMachine(tid);

                    // tung lỗi.
                    throw new Exception("Failed to start machine after 5 minutes.");
                }
                finally
                {
                    // mở khóa lượt.
                    semaphore.Release();
                }
            }

            // trả về kết quả.
            return machine;
        }

        public void StopMachine(int tid)
        {
            var mid = GetMachineID(tid);
            var machine = memu.GetMachine(mid);
            if (machine != null)
            {
                machine.Stop();
                machine.WaitForStop();
            }
        }

        public void KillMachine(int tid)
        {
            var mid = GetMachineID(tid);
            var mname = mid == 0 ? "MEmu" : string.Concat("MEmu_", mid);
            var sql = "SELECT processid, commandline FROM win32_process WHERE name = 'MEmu.exe' OR name = 'MEmuHeadless.exe'";

            var pids = new HashSet<int>();
            using (var mos = new ManagementObjectSearcher(sql))
            using (var moc = mos.Get())
            {
                foreach (var mo in moc)
                {
                    var pid = (int)(uint)mo["processid"];
                    var cline = (string)mo["commandline"];
                    if (cline != null)
                    {
                        var index = cline[0] == '"' ? cline.IndexOf('"', 1) : cline.IndexOf(' ');
                        if (index > -1)
                        {
                            cline = cline.Substring(index + 1);
                        }

                        if (cline.Contains(mname))
                        {
                            pids.Add(pid);
                        }
                    }
                }
            }

            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    var pid = process.Id;
                    if (pids.Contains(pid))
                    {
                        process.Kill();
                    }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        public bool IsMachineRunning(int tid)
        {
            // Lấy Máy Theo ID
            var mid = GetMachineID(tid);
            var machine = memu.GetMachine(mid);

            if (machine != null && machine.IsRunning)
            {
                var unique = mid.ToString().PadLeft(12, '0');
                var muuid = string.Concat("00000000-aaaa-aaaa-aaaa-", unique);

                // Thử Kiểm Tra Nhân
                try
                {
                    var core = vbox.FindMachine(muuid);
                    var state = core.State;
                    return state == MachineState.MachineState_Running;
                }
                catch { }
            }

            return false;
        }

        public Process GetCoreProcess(int tid)
        {
            var mid = GetMachineID(tid);
            if (core_process.TryGetValue(mid, out var process))
            {
                if (!process.HasExited)
                {
                    return process;
                }
                else
                {
                    process.Dispose();
                    core_process.TryRemove(mid, out _);
                }
            }

            var mname = mid == 0 ? "MEmu" : string.Concat("MEmu_", mid);
            var sql = "SELECT processid, commandline FROM win32_process WHERE name = 'MEmuHeadless.exe'";

            using (var mos = new ManagementObjectSearcher(sql))
            using (var moc = mos.Get())
            {
                foreach (var mo in moc)
                {
                    var pid = (int)(uint)mo["processid"];
                    var cline = (string)mo["commandline"];
                    if (cline != null)
                    {
                        var index = cline[0] == '"' ? cline.IndexOf('"', 1) : cline.IndexOf(' ');
                        if (index > -1)
                        {
                            cline = cline.Substring(index + 1);
                        }

                        if (cline.Contains(mname))
                        {
                            try
                            {
                                var proc = Process.GetProcessById(pid);
                                core_process[pid] = proc;
                                return proc;
                            }
                            catch { }
                        }
                    }
                }
            }

            return null;
        }

        public int GetMachineID(int tid)
        {
            if (uid == -1)
            {
                var identity = WindowsIdentity.GetCurrent();
                var name = identity.Name;
                var index = name.IndexOf('\\');

                if (index >= 0)
                {
                    name = name.Substring(index + 1);
                }

                if (name[0] == 'T')
                {
                    var str = name.Remove(0, 1);
                    if (int.TryParse(str, out int id))
                    {
                        uid = id;
                    }
                    else
                    {
                        uid = 0;
                    }
                }
                else
                {
                    uid = 0;
                }
            }
            return uid * 20 + tid;
        }
    }
}
