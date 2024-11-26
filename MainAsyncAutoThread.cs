using AutomationFramework;
using AutomationFramework.Threading;
using BoosterClient.Exceptions;
using BoosterClient.Managers;
using BoosterClient.Models;
using LDiumSharp;
using LDiumSharp.Exceptions;
using LDiumSharp.Models;
using LDiumSharp.Options;
using LDiumSharp.Utils;
using MEmuSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MemuDeezerClient.Log;
using MemuDeezerClient.Managers;
using MemuDeezerClient.Utils;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace MemuDeezerClient
{
    public class MainAsyncAutoThread : AsyncAutoThread
    {
        private static readonly SemaphoreSlim semaphore;

        private readonly ClientManager cm;
        private readonly ProfileManager pm;
        private readonly ProfileSessionManager psm;
        private readonly SourceManager sm;
        private readonly SourcePoolManager spm;
        private readonly ProxyManager prm;
        private readonly SettingManager stm;
        private readonly MachineManager mm;
        private readonly SPHManager sphm;

        private readonly Config config;
        private readonly Random random;
        private readonly string package_name;

        private string status;
        private string cache;
        private bool backupable;
        private bool first_unable;
        private int listen_count;
        private int listen_times;
        private MEmuMachine machine;
        private LDium ldium;
        private ProfileSession session;
        private Profile profile;
        private Proxy proxy;
        private SourceURL source;

        public string Profile
        {
            get
            {
                if (profile != null)
                {
                    if (profile.type == ProfileType.PRIVATE)
                    {
                        return string.Concat('$', profile.name);
                    }
                    else if (profile.type == ProfileType.PREMIUM)
                    {
                        return string.Concat('#', profile.name);
                    }
                    else if (profile.type == ProfileType.PREMBRAZIL)
                    {
                        return string.Concat('@', profile.name);
                    }
                    return profile.name;
                }
                return null;
            }
        }

        public string Proxy
        {
            get
            {
                if (proxy != null)
                {
                    return string.Concat(proxy.host, ':', proxy.port);
                }
                return null;
            }
        }

        public string Status
        {
            get
            {
                return status;
            }
        }

        static MainAsyncAutoThread()
        {
            semaphore = new SemaphoreSlim(5, 5);
        }

        public MainAsyncAutoThread(int id, AutomationApplication app) : base(id)
        {
            cm = app.GetSingleton<ClientManager>();
            pm = app.GetSingleton<ProfileManager>();
            psm = app.GetSingleton<ProfileSessionManager>();
            sm = app.GetSingleton<SourceManager>();
            spm = app.GetSingleton<SourcePoolManager>();
            prm = app.GetSingleton<ProxyManager>();
            stm = app.GetSingleton<SettingManager>();
            sphm = app.GetSingleton<SPHManager>();
            mm = app.GetSingleton<MachineManager>();

            config = Config.Instance;
            random = new Random();
            package_name = "deezer.android.app";
        }

        protected override async Task<bool> OnStartAsync()
        {

            // XÁC THỰC CLIENT
            OnLog(status = "Authorize client");
            {
                while (true)
                {
                    try
                    {
                        await cm.AuthorizeAsync();
                        break;
                    }
                    catch
                    {
                        OnLog(status = "Failed to authorize client, retry after 1 minutes");
                    }
                    await Task.Delay(60000);
                }
            }

            // KIỂM TRA BẢO TRÌ
            OnLog(status = "Check maintenance");
            {
                while (true)
                {
                    if (await stm.TryGetMaintenanceAsync(true))
                    {
                        OnLog(status = "Server is begin maintenance by admin");
                    }
                    else
                    {
                        break;
                    }

                    Thread.Sleep(60000);
                }
            }

            // KIỂM TRA BỂ SOURCE
            OnLog(status = "Check source pool");
            {
                while (true)
                {
                    try
                    {
                        if (await spm.CountAsync() > 0)
                        {
                            break;
                        }

                        OnLog(status = "Source pool is over");
                    }
                    catch
                    {
                        OnLog(status = "Failed to check source pool, retry after 1 minutes");
                    }

                    Thread.Sleep(60000);
                }
            }

            // KHỞI ĐỘNG MÁY ẢO
            OnLog(status = "Start memu machine");
            {
                for (var i = 5; true; i--)
                {
                    try
                    {
                        machine = mm.StartMachine(ID);
                        Thread.Sleep(5000);
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Create("StartMachineError")
                            .AppendError(ex).Commit();

                        OnLog(status = "Failed to start machine, retry after 1 minutes");
                    }

                    if (i <= 0)
                    {
                        throw new Exception("Failed to start machine after 5 attempts");
                    }
                    Thread.Sleep(60000);
                }
            }

            // KHỞI ĐỘNG LDIUM
            OnLog(status = "Start ldium server");
            {
                var text = machine.ExecuteShell("netstat -tlpn | grep :4444 | grep ldium").Trim();
                if (text.Length == 0)
                {
                    // # DEBUG
                    OnLog("Start ldium");

                    using (var proc = machine.RunShell("/data/$/ldium/daemon"))
                    {
                        try
                        {
                            for (int i = 30; true; i--)
                            {
                                await Task.Delay(1000);

                                text = machine.ExecuteShell("netstat -tlpn | grep :4444 | grep ldium").Trim();
                                if (text.Length > 0)
                                {
                                    break;
                                }
                                else if (i <= 0)
                                {
                                    throw new Exception("Failed to wait LDium server online.");
                                }
                            }
                        }
                        finally
                        {
                            if (!proc.HasExited)
                            {
                                proc.Kill();
                            }
                        }
                    }
                }
            }

            // KẾT NỐI LDIUM
            OnLog(status = "Connect ldium server");
            {
                int ldium_port = 4444 + machine.ID;

                ldium = new LDium("127.0.0.1", ldium_port);

                for (var i = 5; true; i--)
                {
                    await Task.Delay(1000);

                    try
                    {
                        await ldium.ConnectAsync();
                        break;
                    }
                    catch
                    {
                        if (i <= 0)
                        {
                            throw new Exception("Failed to connect to LDium server after 5 attempts.");
                        }
                    }
                }

                await ldium.UI.EnableAsync();
            }

            // THIẾT LẬP MÁY ẢO
            OnLog(status = "Setup machine status");
            {
                // thay đổi múi giờ.
                await ldium.System.ExecuteProcessAsync(new string[]
                {
                    "service", "call", "alarm", "3", "s16", "America/New_York"
                }, new ExecuteProcessOptions { WaitForExit = true });

                // thay đổi trạng thái sạc pin.
                await ldium.System.ExecuteProcessAsync(new string[]
                {
                    "cmd", "battery", "unplug"
                }, new ExecuteProcessOptions { WaitForExit = true });

                // thay đổi phần trăm pin.
                await ldium.System.ExecuteProcessAsync(new string[]
                {
                    "cmd", "battery", "set", "level", random.Next(30, 90).ToString()
                }, new ExecuteProcessOptions { WaitForExit = true });

                // giết tidal nếu đang chạy.
                await ldium.KillAppAsync(package_name);
            }

            // KIỂM TRA MÁY ẢO
            OnLog(status = "Check MEmu machine");
            {
                var dir = ldium.FileIO.GetDirectory("/data/app/com.module.ldium.faker-1");
                if (!await dir.ExistAsync())
                {
                    OnLog(status = "Please update MEmu machine first");
                    return false;
                }
            }

            // MỞ PHIÊN PROFILE
            OnLog(status = "Create profile session");
            {
                session = null;
                while (session == null)
                {
                    try
                    {
                        session = await psm.OpenAsync();
                    }
                    catch (Exception ex)
                    {
                        if (ex is ProfileQueueOverException)
                        {
                            OnLog(status = "Profile queue is over, retry after 1 minutes.");
                        }
                        else
                        {
                            OnLog(status = "Failed to create profile session, retry after 1 minutes.");
                        }

                        await Task.Delay(60000);
                    }
                }
            }

            //session.profile_id = 10908;
            // LẤY DỮ LIỆU PROFILE
            OnLog(status = "Get profile data");
            {
                try
                {
                    profile = await pm.GetAsync(session.profile_id);
                }
                catch (Exception ex)
                {
                    if (ex is ProfileNotFoundException)
                    {
                        throw new Exception("Failed to get profile data, profile is not exist.");
                    }
                    else
                    {
                        throw new Exception("Failed to get profile data.", ex);
                    }
                }
            }
            //profile.proxy_id = 2278;

            // CẤU HÌNH CACHE SERVER
            OnLog(status = "Config cache server");
            {
                if (cache == null)
                {
                    var address = NetworkUtil.GetLocalIP();
                    if (address != null)
                    {
                        var split = address.Split('.');
                        //var strip = split[2];
                        //if (strip.Equals("2") || strip.Equals("5") || strip.Equals("7"))
                        //{
                        //    split[2] = "6";
                        //}
                        split[3] = "23";
                        cache = string.Join(".", split);
                    }
                    else
                    {
                        cache = "127.0.0.1";
                    }
                }

                var cache_dir = ldium.FileIO.GetDirectory("/data/$/cache");
                await cache_dir.CreateAsync();
                await cache_dir.SetPermissionAsync(755);

                var config_file = cache_dir.GetFile("config.json");
                await config_file.WriteAllTextAsync(JsonConvert.SerializeObject(new
                {
                    server = cache
                }));
                await config_file.SetPermissionAsync(755);
            }
            // ÁP DỤNG PROXY
            OnLog(status = "Apply proxy");
            {
                if (profile.proxy_id == null)
                {
                    throw new Exception("Failed to apply proxy, profile is not have proxy.");
                }

                this.proxy = await prm.GetAsync(profile.proxy_id.Value);
                var type = proxy.type;

                if (type == ProxyType.HTTPS && (proxy.username == null || proxy.password == null))
                {
                    // PHƯƠNG PHÁP TRUYỀN THỐNG

                    await ldium.Network.SetProxyAsync(new ProxyInfo()
                    {
                        Host = proxy.host,
                        Port = proxy.port,
                        ExclusionList = new string[] { cache, "10.0.2.2" }
                    });
                }
                else
                {
                    // PHƯƠNG PHÁP GLIDER

                    // Giết Glider
                    await ldium.System.ExecuteProcessAsync(new string[]
                    {
                        "killall", "glider"
                    }, new ExecuteProcessOptions { WaitForExit = true });

                    // Dựng Chuỗi Tham Số
                    var sb = new StringBuilder("http://");
                    if (proxy.username != null && proxy.password != null)
                    {
                        sb.Append(proxy.username).Append(':')
                            .Append(proxy.password).Append('@');
                    }
                    sb.Append(proxy.host).Append(':').Append(proxy.port);

                    // Chạy Glider
                    await ldium.System.ExecuteProcessAsync(new string[]
                    {
                        "/data/$/glider/glider", "-listen", "http://127.0.0.1:8888", "-forward", sb.ToString()
                    }, new ExecuteProcessOptions { WaitForExit = false });

                    // Áp Dụng Proxy
                    await ldium.Network.SetProxyAsync(new ProxyInfo()
                    {
                        Host = "127.0.0.1",
                        Port = 8888,
                        ExclusionList = new string[] { cache, "10.0.2.2" }
                    });
                }
            }

            // ÁP DỤNG VÂN TAY
            OnLog(status = "Apply fingerprint");
            {
                using (var stream = await pm.ReadFingerprintAsync(profile.profile_id))
                using (var reader = new StreamReader(stream))
                {
                    var text = reader.ReadToEnd();
                    var jobj = JObject.Parse(text);

                    jobj["screen"]["width"] = 1080;
                    jobj["screen"]["height"] = 1920;
                    jobj["screen"]["dpi"] = 360;
                    text = jobj.ToString();

                    var io = ldium.FileIO;

                    // Tạo Thư Mục Faker
                    var faker = io.GetDirectory("/data/$/faker");
                    await faker.CreateAsync();
                    await faker.SetPermissionAsync(755);

                    // Ghi Tệp Targets
                    var targets = faker.GetFile("targets.json");
                    await targets.WriteAllTextAsync(JsonConvert.SerializeObject(new string[] {
                        "android",
                        "com.android.settings",
                        "ru.andr7e.deviceinfohw",
                        package_name
                    }));
                    await targets.SetPermissionAsync(755);

                    // Ghi Tệp Data
                    var data = faker.GetFile("data.json");
                    await data.WriteAllTextAsync(text);
                    await data.SetPermissionAsync(755);
                }
            }

            // KHÔI PHỤC PROFILE
            OnLog(status = "Restore profile");
            {
                await ldium.CloseAppAsync(package_name);

                MemoryStream stream;
                try
                {
                    stream = (MemoryStream)await pm.ReadPayloadAsync(profile.profile_id);
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to download profile payload.", ex);
                }

                try
                {
                    var bytes = stream.ToArray();
                    await ldium.RestoreAppAsync(bytes);
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to restore profile payload.", ex);
                }
                finally
                {
                    stream.Dispose();
                }

                Thread.Sleep(1000);

                // mở khóa nội dung explicit.
                //try
                //{
                //    var file = ldium.FileIO.GetFile("/data/data/com.aspiro.tidal/shared_prefs/com.aspiro.tidal_preferences.xml");
                //    var xml = await file.ReadAllTextAsync();

                //    var doc = new XmlDocument();
                //    doc.LoadXml(xml);

                //    var map = doc.SelectSingleNode("//map");
                //    var node = map.SelectSingleNode("//string[@name='explicit_content']");
                //    if (node == null)
                //    {
                //        var child = doc.CreateElement("string");
                //        child.InnerText = "dHJ1ZQ==";

                //        var attr = doc.CreateAttribute("name");
                //        attr.Value = "explicit_content";

                //        child.Attributes.Append(attr);
                //        map.AppendChild(child);

                //        xml = doc.OuterXml;
                //        await file.WriteAllTextAsync(xml);
                //    }
                //}
                //catch { }
            }

            // MỞ DEEZER
            bool retry = false;

            OnLog(status = "Open Deezer");
            {
                semaphore.Wait();
                try
                {
                    await ldium.CloseAppAsync(package_name);
                    Thread.Sleep(1000);

                    // Đóng Thanh Thông Báo
                    await ldium.System.ExecuteProcessAsync(new string[]
                    {
                    "service", "call", "statusbar", "2"
                    }, new ExecuteProcessOptions { WaitForExit = true });

                    // Mở Deezer
                    await ldium.System.ExecuteProcessAsync(new string[]
                    {
                    "am", "start",
                    "-n", $"{package_name}/com.deezer.android.ui.activity.LauncherActivity",
                    "-a", "android.intent.action.MAIN",
                    "-c", "android.intent.category.LAUNCHER"
                    }, new ExecuteProcessOptions { WaitForExit = true });

                    // Đợi Nội Dung
                    //await ldium.WaitForActivityAsync((activity) =>
                    //{
                    //    var class_name = activity.ClassName;
                    //    var result = class_name.Equals("com.aspiro.wamp.MainActivity") || class_name.Equals("com.aspiro.wamp.LoginFragmentActivity");
                    //    return Task.FromResult(result);
                    //});
                    await ldium.WaitForExistAnyAsync(new string[]
                    {
                        $"//node[@resource-id=\"deezer.android.app:id/cover_and_title\"]",
                        $"//node[@resource-id=\"deezer.android.app:id/continue_btn\"]",
                        $"//node[@resource-id=\"android:id/button1\"]",
                        $"//node[@resource-id=\"deezer.android.app:id/header_close_button\"]",
                        $"//node[@text=\"Change my payment details\"]",
                        $"//node[@resource-id=\"deezer.android.app:id/welcome_title\"]",
                        $"//node[@resource-id=\"deezer.android.app:id/webview_cancel\"]",
                        $"//node[@text=\"Accept\"]",
                        $"//node[@text=\"Process system isn't responding\"]",
                        $"//node[@resource-id=\"android:id/message\"]",
                    });

                    var hierarchy = await ldium.DumpViewHierarchyAsync();
                    // trương hợp proxy fail cần đổi proxy khác.
                    if (hierarchy.FindNodeByText("QUIT") != null)
                    {
                        await pm.RotateProxyAsync(profile.profile_id);
                        return true;
                        // Viết hàm rotate proxy
                    }

                    // trường hợp profile hỏng
                    if (hierarchy.FindNodeByResourceID("deezer.android.app:id/welcome_title") != null || hierarchy.FindNodeByResourceID("deezer.android.app:id/continue_btn") != null)
                    {
                        // cập nhật trạng thái profile.
                        if (profile.status != ProfileStatus.BAD)
                        {
                            await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.BAD);
                        }

                        // thoát luồng.
                        return true;
                    }

                    // Trường hợp xuất hiện nút Accept
                    if (hierarchy.FindNodeByText("Accept") != null)
                    {
                        await ldium.TouchAsync("//node[@text=\"Accept\"]");
                        Thread.Sleep(2000);
                    }

                    Thread.Sleep(2000);
                    ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                    //if (hierarchy.FindNodeByResourceID("android:id/message") != null || ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"android:id/message\"]").Text.ToLower().Trim().Contains("to verify your subscription and keep using deezer on your phone, please connect to wifi or a cellular network within"))
                    //{
                    //    return true;
                    //}

                    if (hierarchy.FindNodeByResourceID("android:id/button1") != null)
                    {
                        if (hierarchy.FindNodeByText("RETRY") != null)
                        {
                            await ldium.TouchAsync("//node[@text=\"RETRY\"]");
                            retry = true;
                        }
                    }

                    if (hierarchy.FindNodeByResourceID("deezer.android.app:id/header_close_button") != null)
                    {
                        await ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/header_close_button\"]");
                    }

                    if (hierarchy.FindNodeByText("RETRY") != null)
                    {
                        retry = true;
                        await ldium.TouchAsync("//node[@text=\"RETRY\"]");
                    }

                    if (hierarchy.FindNodeByText("Error: Failed to play.") != null)
                    {
                        retry = true;
                        await ldium.TouchAsync("//node[@resource-id=\"android:id/button2\"]");
                    }

                    Thread.Sleep(2000);

                    if (hierarchy.FindNodeByText("Consent") != null && hierarchy.FindNodeByText("Subscribe") != null)
                    {
                        await ldium.TouchAsync("//node[@text=\"Consent\"]");
                    }

                    if (hierarchy.FindNodeByResourceID("android:id/resolver_list") != null)
                    {
                        await ldium.TouchByTextAsync("Deezer");
                        Thread.Sleep(500);
                        await ldium.TouchByTextAsync("ALWAYS");
                        Thread.Sleep(500);
                    }

                    // Check Sub
                    if (hierarchy.FindNodeByResourceID("deezer.android.app:id/cover_and_title") != null || hierarchy.FindNodeByResourceID("deezer.android.app:id/braze_card_title") != null || hierarchy.FindNodeByText("Home") != null)
                    {
                        if (profile.status == ProfileStatus.BAD)
                        {
                            await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.GOOD);
                        }

                        hierarchy = await ldium.DumpViewHierarchyAsync();
                        try
                        {
                            await ldium.WaitForExistAsync("//node[@class=\"androidx.compose.ui.platform.ComposeView\"]");
                        }
                        catch
                        {

                        }

                        await ldium.TouchAsync("//node[@class=\"androidx.compose.ui.platform.ComposeView\"]/node[1]/node[1]/node[2]", new TouchOptions
                        {
                            Padding = new Padding { Bottom = 10, Top = 10, Right = 10, Left = 10 }
                        });
                        Thread.Sleep(2000);

                        int touch_time = 3;
                        while (touch_time > 0)
                        {
                            //if (hierarchy.FindNodeByResourceID("deezer.android.app:id/offer") == null) 
                            if (!ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/offer\"]").Result)
                            {
                                await ldium.DumpViewHierarchyAsync();
                                if (ldium.ExistAsync("//node[@class=\"androidx.compose.ui.platform.ComposeView\"]/node[1]/node[1]/node[2]").Result)
                                {
                                    ldium.TouchAsync("//node[@class=\"androidx.compose.ui.platform.ComposeView\"]/node[1]/node[1]/node[2]", new LDiumSharp.Options.TouchOptions
                                    {
                                        Padding = new Padding { Bottom = 10, Top = 10, Right = 10, Left = 10 }
                                    }).Wait();

                                }
                                if (ldium.ExistAsync("//node[@text=\"Accept\"]").Result)
                                {
                                    ldium.TouchAsync("//node[@text=\"Accept\"]").Wait();
                                    Thread.Sleep(2000);
                                }
                            }
                            else
                            {
                                break;
                            }
                            touch_time--;
                            Thread.Sleep(1000);
                        }

                        ldium.WaitForExistAsync("//node[@resource-id=\"deezer.android.app:id/offer\"]").Wait();
                        hierarchy = await ldium.DumpViewHierarchyAsync();

                        string sub = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/offer\"]").Text;

                        if (sub.ToLower() == "free" || sub.ToLower() == "deezer free")
                        {
                            if (profile.type != ProfileType.FREE)
                            {
                                //api.Profile.UpdateProfile(profile.ProfileId, new ProfileType?(ProfileType.Free), new ProfileStatus?(ProfileStatus.Good));
                                //return true;
                                await pm.TryUpdateAsync(profile_id: profile.profile_id, type: ProfileType.FREE);
                                return true;
                            }
                        }
                        else if (sub.ToLower().Contains("tim"))
                        {
                            if (profile.type != ProfileType.PREMBRAZIL && profile.type != ProfileType.PRIVATE)
                            {
                                //api.Profile.UpdateProfile(profile.ProfileId, new ProfileType?(ProfileType.Brazil), new ProfileStatus?(ProfileStatus.Good));
                                await pm.TryUpdateAsync(profile_id: profile.profile_id, type: ProfileType.PREMBRAZIL);
                            }
                        }
                        else
                        {
                            if (profile.type != ProfileType.PREMIUM && profile.type != ProfileType.PRIVATE)
                            {
                                //api.Profile.UpdateProfile(profile.ProfileId, new ProfileType?(ProfileType.Prem), new ProfileStatus?(ProfileStatus.Good));
                                await pm.TryUpdateAsync(profile_id: profile.profile_id, type: ProfileType.PREMIUM);
                            }
                        }

                        if (ldium.ExistAsync("//node[@text=\"Audio\"]").Result)
                        {
                            ldium.TouchAsync("//node[@text=\"Audio\"]").Wait();
                            Thread.Sleep(1000);
                            if (!ldium.ExistAsync("//node[@text=\"Mobile data\"]").Result)
                            {

                            }
                            else
                            {
                                if (!ldium.ExistAsync("//node[@text=\"Mobile data\"]/following-sibling::node[@text=\"Standard\"]").Result)
                                {
                                    ldium.TouchAsync("//node[@text=\"Mobile data\"]").Wait();
                                    Thread.Sleep(1000);
                                    ldium.TouchAsync("//node[@text=\"Standard\"]").Wait();
                                    Thread.Sleep(1000);
                                }
                                if (!ldium.ExistAsync("//node[@text=\"WiFi\"]/following-sibling::node[@text=\"Standard\"]").Result)
                                {
                                    ldium.TouchAsync("//node[@text=\"WiFi\"]").Wait();
                                    Thread.Sleep(1000);
                                    ldium.TouchAsync("//node[@text=\"Standard\"]").Wait();
                                    Thread.Sleep(1000);
                                }
                                if (!ldium.ExistAsync("//node[@text=\"Google Cast\"]/following-sibling::node[@text=\"Standard\"]").Result)
                                {
                                    ldium.TouchAsync("//node[@text=\"Google Cast\"]").Wait();
                                    Thread.Sleep(1000);
                                    ldium.TouchAsync("//node[@text=\"Standard\"]").Wait();
                                    Thread.Sleep(1000);
                                }

                            }
                        }
                    }





                    //// trường hợp profile hỏng.
                    //if (hierarchy.FindNodeByResourceID($"{package_name}:id/loginButton") != null)
                    //{
                    //    // cập nhật trạng thái profile.
                    //    if (profile.status != ProfileStatus.BAD)
                    //    {
                    //        await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.BAD);
                    //    }

                    //    // thoát luồng.
                    //    return true;
                    //}

                    //// trường hợp phiên đăng nhập hết hạn.
                    //if (hierarchy.FindNodeByText("Session expired") != null)
                    //{
                    //    // cập nhật trạng thái.
                    //    await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.BAD);

                    //    // thoát luồng.
                    //    return true;
                    //}

                    //// trường hợp hiện hộp thoại thông báo cập nhật.
                    //if (hierarchy.FindNodeByText("Close Message") != null)
                    //{
                    //    await ldium.TouchByTextAsync("Close Message");
                    //    await ldium.WaitForGoneByTextAsync("Close Message");

                    //    await Task.Delay(2000);

                    //    hierarchy = await ldium.DumpViewHierarchyAsync();
                    //}

                    // trường hợp chọn ngẫu nhiên nghệ sĩ
                    if (hierarchy.FindNodeByResourceID($"{package_name}:id/loadMoreButton") != null)
                    {
                        // Nhấn "Show More"
                        await ldium.TouchAsync(hierarchy.FindNodeByResourceID($"{package_name}:id/loadMoreButton"));

                        // Đợi Nội Dung
                        await Task.Delay(2000);

                        // Chọn Ngẫu Nhiên Nghệ Sĩ
                        var choose_count = random.Next(4, 7);
                        for (var i = 0; i < choose_count; i++)
                        {
                            hierarchy = await ldium.DumpViewHierarchyAsync();

                            // Trường Hợp Hiện Hộp Thoại Thông Báo Cập Nhập
                            if (hierarchy.FindNodeByText("Close Message") != null)
                            {
                                await ldium.TouchByTextAsync("Close Message");
                                await ldium.WaitForGoneByTextAsync("Close Message");

                                await Task.Delay(2000);

                                hierarchy = await ldium.DumpViewHierarchyAsync();
                            }

                            var nodes = hierarchy.FindNodeByResourceID($"{package_name}:id/recyclerView")
                                .ChildNodes.Where((x) =>
                                {
                                    var fc = x.FirstChild;
                                    var lc = x.LastChild;
                                    var b = x.Bound;
                                    return !x.Selected && b.Bottom < 900 && fc != null && lc != null && fc.ResourceID.EndsWith("artwork") && lc.ResourceID.EndsWith("name");
                                }).ToArray();

                            var index = random.Next(nodes.Length);
                            await ldium.TouchAsync(nodes[index]);

                            await Task.Delay(2000);
                        }

                        // Nhấn "Continue"
                        await ldium.TouchByResourceIDAsync($"{package_name}:id/continueButton");

                        // Đợi Nội Dung.
                        await ldium.WaitForExistAnyAsync(new string[]
                        {
                            $"//node[@resource-id='LazyColumn']",
                            $"//node[@resource-id='{package_name}:id/design_bottom_sheet']/node[1]/node[1]/node[1]/node[2]/node[@text='Plans']"
                        });

                        // Đóng Popup Nếu Xuất Hiện
                        hierarchy = await ldium.DumpViewHierarchyAsync();
                        var close = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/design_bottom_sheet']/node[1]/node[1]/node[1]/node[2]/node[1]");
                        if (close != null)
                        {
                            await ldium.TouchAsync(close);
                        }
                    }

                    // trường hợp truy cập thành công.


                    //if (hierarchy.FindNodeByResourceID("LazyColumn") != null || hierarchy.FindNodeByResourceID($"{package_name}:id/swipe_refresh") != null)
                    //{
                    //    // đợi profile sẵn sàng.
                    //    Thread.Sleep(10000);

                    //    // cập nhật trạng thái profile.
                    //    if (profile.status == ProfileStatus.BAD)
                    //    {
                    //        await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.GOOD);
                    //    }

                    //    // cập nhật thông tin profile.
                    //    try
                    //    {
                    //        var file = ldium.FileIO.GetFile("/data/data/com.aspiro.tidal/shared_prefs/com.aspiro.tidal_preferences.xml");
                    //        var xml = await file.ReadAllTextAsync();

                    //        var doc = new XmlDocument();
                    //        doc.LoadXml(xml);

                    //        var changed = false;
                    //        var node = doc.SelectSingleNode("//string[@name='session_country_code']");
                    //        if (node != null)
                    //        {
                    //            var text = node.InnerText;
                    //            var bytes = Convert.FromBase64String(text);
                    //            var value = Encoding.UTF8.GetString(bytes);

                    //            if (profile.country == null || !profile.country.Equals(value))
                    //            {
                    //                profile.country = value;
                    //                changed = true;
                    //            }
                    //        }

                    //        node = doc.SelectSingleNode("//string[@name='user_subscription_start_date']");
                    //        if (node != null)
                    //        {
                    //            var text = node.InnerText;
                    //            var bytes = Convert.FromBase64String(text);
                    //            var value = Encoding.UTF8.GetString(bytes);

                    //            if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var datetime))
                    //            {
                    //                if (profile.subs_start_date == null || !profile.subs_start_date.Equals(datetime))
                    //                {
                    //                    profile.subs_start_date = datetime;
                    //                    changed = true;
                    //                }
                    //            }
                    //        }

                    //        node = doc.SelectSingleNode("//string[@name='user_subscription_valid_until']");
                    //        if (node != null)
                    //        {
                    //            var text = node.InnerText;
                    //            var bytes = Convert.FromBase64String(text);
                    //            var value = Encoding.UTF8.GetString(bytes);

                    //            if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var datetime))
                    //            {
                    //                if (profile.subs_end_date == null || !profile.subs_end_date.Equals(datetime))
                    //                {
                    //                    profile.subs_end_date = datetime;
                    //                    changed = true;
                    //                }
                    //            }
                    //        }

                    //        if (changed)
                    //        {
                    //            await pm.TryUpdateAsync
                    //            (
                    //                profile_id: profile.profile_id,
                    //                country: profile.country,
                    //                subs_start_date: profile.subs_start_date,
                    //                subs_end_date: profile.subs_end_date
                    //            );
                    //        }
                    //    }
                    //    catch { }

                    //    // đánh dấu profile có thể sao lưu.
                    //    backupable = true;
                    //}

                    //// Trường Hợp Truy Cập Thất Bại
                    //else
                    //{
                    //    throw new Exception("Failed to open Tidal");
                    //}
                }
                finally
                {
                    semaphore.Release();
                }
                if (retry)
                {
                    backupable = true;
                }
            }

            listen_count = 0;
            listen_times = random.Next(10, 15);
            first_unable = false;

            while (listen_count < listen_times)
            {
                var success = true;

                // KIỂM TRA BẢO TRÌ
                if (await stm.TryGetMaintenanceAsync(true))
                {
                    break;
                }

                //LẤY SOURCE
                OnLog(status = "Pick random source");
                try
                {
                    if (Build.IS_LITE)
                    {
                        source = spm.PickSourceLite();
                    }
                    else
                    {
                        source = await spm.PickAsync();

                    }

                }
                catch (Exception ex)
                {
                    if (ex is SourcePoolOverException)
                    {
                        throw new Exception("Failed to pick source, source pool is over.");
                    }
                    else
                    {
                        throw new Exception("Failed to pick source.", ex);
                    }
                }
                string link = "";
                if (Build.IS_LITE)
                {
                    link = source.url;
                }
                else
                {
                    if (source.type == SourceUrlType.ALBUM)
                    {
                        link = "https://www.deezer.com/us/album/" + source.url;
                    }
                    else if (source.type == SourceUrlType.ARTIST)
                    {
                        link = "https://www.deezer.com/us/artist/" + source.url;
                    }

                }

                //source.source_id = "artist.167476237";
                //source.type = SourceUrlType.ALBUM;
                //source.url = "312637867";
                //link = "https://www.deezer.com/us/album/312637867";


                semaphore.Wait();
                try
                {
                    // MỞ SOURCE
                    OnLog(status = "Open source");
                    if (success)
                        success = await OpenSourceAsync(link);

                    //// PHÁT NHẠC
                    //OnLog(status = "Play source");
                    //if (success)
                    //    success &= await PlaySourceAsync();
                    Thread.Sleep(4000);
                    ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                    if (link.Contains("artist") && ldium.ExistAsync("//node[@text=\"No Items to Display\"]").Result)
                    {
                        continue;
                    }
                    //if (source.type == SourceUrlType.ALBUM)
                    //{
                    //    ViewNode[] nodes = null;
                    //    int times = 5;
                    //    while (times > 0)
                    //    {
                    //        ViewNode node = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]");
                    //        try
                    //        {
                    //            nodes = node.ChildNodes.Cast<ViewNode>().ToArray<ViewNode>();
                    //        }
                    //        catch
                    //        {

                    //        }
                    //        if (nodes.Length > 4)
                    //        {
                    //            break;
                    //        }
                    //        times--;
                    //        Thread.Sleep(2000);
                    //    }
                    //    if (nodes.Length < 4)
                    //    {
                    //        if (listen_count == 0)
                    //        {
                    //            first_unable = true;
                    //        }
                    //        else if (listen_count == 1 && first_unable)
                    //        {
                    //            //api.Profile.Rotateproxy(profile.ProfileId, proxy.ProxyId);
                    //            //return true;
                    //        }
                    //    }
                    //}



                    //if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/card\"]").Result)
                    //{
                    //    //api.Profile.UpdateProfile(profile.ProfileId, ProfileType.Free, ProfileStatus.Good);
                    //    return true;
                    //}





                    if (ldium.ExistAsync("//node[@text=\"Unable to load page.\"]").Result)
                    {
                        await sm.ReportAsync(source.source_id, SourceReportType.BAD);
                        if (listen_count == 0)
                        {
                            first_unable = true;
                        }
                        else if (listen_count == 1 && first_unable)
                        {

                            //await pm.RotateProxyAsync(profile.profile_id);
                            return true;
                        }
                        //else if (listen_count)
                        listen_count++;
                        continue;
                    }
                    if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/header_close_button\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/header_close_button\"]").Wait();
                    }
                    if (ldium.ExistAsync("//node[@text=\"QUIT\"]").Result)
                    {
                        await pm.RotateProxyAsync(profile.profile_id);
                        return true;
                    }
                    if (ldium.ExistAsync("//node[@text=\"Maybe later\"]").Result)
                    {
                        ldium.TouchAsync("//node[@text=\"Maybe later\"]").Wait();
                    }
                    if (ldium.ExistAsync("//node[@resource-id=\"android:id/button2\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"android:id/button2\"]").Wait();
                    }
                    if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/welcome_title\"]").Result)
                    {
                        await pm.TryUpdateAsync(profile_id: profile.profile_id, status: ProfileStatus.BAD);

                        return true;
                    }

                    if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/cover_and_title\"]").Result)
                    {
                        await pm.RotateProxyAsync(profile.profile_id);
                        return true;
                    }

                    if (ldium.ExistAsync("//node[@text=\"QUIT\"]").Result)
                    {
                        await pm.RotateProxyAsync(profile.profile_id);
                        return true;
                    }

                    OnLog(status = "Play Music");
                    PlayMusic2(link, ldium, listen_count);

                }
                finally
                {
                    semaphore.Release();
                }


                OnLog(status = "Listen Music");
                ListenMusic2(link, ldium, listen_count);
                // NGHE NHẠC
                //OnLog(status = "Listen source");
                //if (success)
                //    await ListenSourceAsync2();
            }

            return true;
        }

        private async Task<bool> OpenSourceAsync(string link)
        {
            // TẠO XPATH
            //string xpath = null;
            //{
            //    var hierarchy = await ldium.DumpViewHierarchyAsync();
            //    var album_title_node = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/recyclerView']/node[1]/node[@resource-id='{package_name}:id/title']");
            //    var artist_title_node = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/recyclerView']/node[1]/node[@resource-id='{package_name}:id/artistName']");

            //    if (album_title_node != null)
            //    {
            //        var album_title = album_title_node.Text;
            //        xpath = $"//node[@resource-id='{package_name}:id/recyclerView']/node[1]/node[@text={XPathUtils.StringLiteral(album_title)}]";
            //    }
            //    else if (artist_title_node != null)
            //    {
            //        var artist_title = artist_title_node.Text;
            //        xpath = $"//node[@resource-id='{package_name}:id/recyclerView']/node[1]/node[@text={XPathUtils.StringLiteral(artist_title)}]";
            //    }
            //}
            //string link = "";
            //if (source.type == SourceUrlType.ALBUM)
            //{
            //    link = "https://www.deezer.com/us/album/" + source.url;
            //}
            //else if (source.type == SourceUrlType.ARTIST)
            //{
            //    link = "https://www.deezer.com/us/artist/" + source.url;
            //}

            string[] cmd = new string[]
{
                "am",
                "start",
                "-a",
                "android.intent.action.VIEW",
                "-p",
                "deezer.android.app",
                "-d",
                link
};
            ExecuteProcessOptions executeProcessOptions = new ExecuteProcessOptions();
            ldium.System.ExecuteProcessAsync(cmd, executeProcessOptions).Wait(10000);

            // MỞ SOURCE
            //await ldium.System.ExecuteProcessAsync(new string[]
            //{
            //    "am", "start",
            //    "-a", "android.intent.action.VIEW",
            //    "-p", "com.aspiro.tidal",
            //    "-d", source.type == SourceUrlType.ALBUM ? $"https://tidal.com/album/{source.url}" : $"https://tidal.com/artist/{source.url}"
            //}, new ExecuteProcessOptions { WaitForExit = true });

            // ĐỢI NỘI DUNG BIẾN MẤT
            //if (xpath != null)
            //{
            //    await ldium.WaitForGoneAsync(xpath);
            //}

            // ĐỢI NỘI DUNG XUẤT HIỆN
            //await ldium.WaitForExistAnyAsync(new string[]
            //{
            //    $"//node[@resource-id='{package_name}:id/animatedAlbumCover']",
            //    $"//node[@resource-id='{package_name}:id/artworkOverlay']",
            //    $"//node[@resource-id='{package_name}:id/placeholderButton']",
            //    $"//node[@resource-id='{package_name}:id/placeholderText']"
            //});
            ldium.ClearDumpViewHierarchyCacheAsync().Wait(10000);

            try
            {
                await ldium.WaitForExistAnyAsync(new string[]
                {
                    $"//node[@resource-id='{package_name}:id/masthead_motion_layout_masthead_illustration_View']",
                    $"//node[@resource-id='android:id/resolver_list']",
                    $"//node[@resource-id='{package_name}:id/fl_insets']",
                    $"//node[@resource-id='{package_name}:id/card']",
                });

            }
            catch
            {


            }
            if (ldium.ExistAsync("//node[@resource-id=\"android:id/resolver_list\"]").Result)
            {
                await ldium.TouchAsync("//node[@text=\"Deezer\"]");
                Thread.Sleep(500);
                await ldium.TouchAsync("//node[@text=\"ALWAYS\"]");
                Thread.Sleep(500);
            }

            await ldium.WaitForExistAnyAsync(new string[]
            {
                $"//node[@resource-id='{package_name}:id/masthead_motion_layout_masthead_illustration_View']",
                $"//node[@index='9']",
                $"//node[@resource-id='{package_name}:id/fl_insets']",
                $"//node[@resource-id='{package_name}:id/card']",
                $"//node[@text='QUIT']",
                $"//node[@text='RETRY']"
            });

            // KIỂM TRA SOURCE KHÔNG CÒN TỒN TẠI
            //{
            //    var hierarchy = await ldium.DumpViewHierarchyAsync();
            //    if (hierarchy.FindNodeByResourceID($"{package_name}:id/placeholderButton") != null || hierarchy.FindNodeByResourceID($"{package_name}:id/placeholderText") != null)
            //    {
            //        // Báo Cáo Source
            //        try
            //        {
            //            await sm.ReportAsync(source.source_id, SourceReportType.BAD);
            //        }
            //        catch { }

            //        // Ngủ 5 Giây
            //        Thread.Sleep(5000);

            //        return false;
            //    }
            //}

            return true;
        }

        private async Task<bool> PlaySourceAsync()
        {
            var not_from_artist = false;
            var not_from_album = false;

            // PHÁT NHẠC TỪ TRANG ARTIST
            if (await ldium.ExistByResourceIDAsync($"{package_name}:id/artworkOverlay"))
            {
                var hierarchy = await ldium.DumpViewHierarchyAsync();

                // Trường Hợp Hiện Hộp Thoại Chuyển Giao Danh Sách Phát
                if (hierarchy.FindNodeByText("Close Message") != null)
                {
                    await ldium.TouchByTextAsync("Close Message");
                    await ldium.WaitForGoneByTextAsync("Close Message");

                    await Task.Delay(2000);

                    hierarchy = await ldium.DumpViewHierarchyAsync();
                }

                // Cuộn Trang Một Khoảng Ngẫu Nhiên
                {
                    await Task.Delay(1000);

                    var node = (await ldium.DumpViewHierarchyAsync())
                        .FindNode($"//node[@resource-id='{package_name}:id/recyclerView']");
                    var x = node.Bound.Left + 24 + random.Next(node.Bound.Width - 48);
                    var y = node.Bound.Top + 24 + random.Next(node.Bound.Height - 48);
                    var distance = random.Next(100, 800);

                    await ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                    {
                        hierarchy = await ldium.DumpViewHierarchyAsync();
                        return d >= distance ||
                            hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title'][@text='Top Tracks']") != null ||
                            hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title'][@text='Albums']") != null ||
                            hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title'][@text='EP & Singles']") != null;
                    });
                }

                // Phát Nhạc Từ Top Tracks
                if (await ldium.ExistAsync($"//node[@resource-id='{package_name}:id/title'][@text='Top Tracks']"))
                {
                    var view_all = (await ldium.DumpViewHierarchyAsync())
                        .FindNode($"//node[@resource-id='{package_name}:id/title'][@text='Top Tracks']")
                        .NextSibling;
                    await ldium.TouchAsync(view_all);

                    await ldium.WaitForExistAsync($"//node[@resource-id='com.aspiro.tidal:id/toolbar']/node[@text='Top Tracks']");
                    await Task.Delay(1000);

                    // Chọn Một Dòng Ngẫu Nhiên
                    ViewNode row;
                    {
                        await Task.Delay(1000);

                        var rows = (await ldium.DumpViewHierarchyAsync())
                            .FindNodeByResourceID($"{package_name}:id/recyclerView")
                            .ChildNodes.Where((x) =>
                            {
                                var fc = x.FirstChild;
                                var lc = x.LastChild;
                                return fc != null && lc != null && fc.ResourceID.EndsWith(":id/number") && lc.ResourceID.EndsWith(":id/options");
                            }).ToArray();

                        row = rows[random.Next(rows.Length)];
                    }

                    // Mở Bài Được Chọn
                    {
                        await ldium.TouchAsync(row, new TouchOptions()
                        {
                            Padding = new Padding()
                            {
                                Right = 72f
                            }
                        });

                        // Đợi Trình Phát
                        var song_name = row.ChildNodes[1].Text;
                        for (var j = 15; true; j--)
                        {
                            await Task.Delay(2000);

                            hierarchy = await ldium.DumpViewHierarchyAsync();

                            // Kiểm Tra Hộp Thoại Giới Hạn Thiết Bị
                            var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                            if (dialog_title != null)
                            {
                                var dialog_text = dialog_title.Text;
                                if (string.Equals(dialog_text, "Playback Paused"))
                                {
                                    // Đóng Băng Phiên
                                    try
                                    {
                                        await psm.FreezeAsync(session.session_id);
                                    }
                                    catch { }
                                    session = null;

                                    // Tung Lỗi
                                    throw new Exception("Failed to play music, account is playing on another device.");
                                }
                            }

                            // Kiểm Tra Hộp Thoại Nâng Cấp Tài Khoản
                            //if (hierarchy.FindNode("//node[contains(@text, 'plans are not available')]") != null)
                            //{
                            //    // Báo Cáo Tài Khoản
                            //    try
                            //    {
                            //        await pm.UpdateAsync
                            //        (
                            //            profile_id: profile.profile_id,
                            //            status: ProfileStatus.EXPIRED
                            //        );
                            //    }
                            //    catch { }

                            //    throw new Exception("Failed to play source, account has expired.");
                            //}

                            // Kiểm Tra Tiêu Đề Bài Đang Được Phát
                            var player_title = hierarchy.FindNodeByResourceID($"{package_name}:id/miniPlayerMediaItemTitle");
                            if (player_title != null && player_title.Text.Equals(song_name))
                            {
                                break;
                            }

                            if (j <= 0)
                            {
                                throw new Exception("Failed to wait player_title.");
                            }
                        }

                        // Mở Trình Phát
                        await ldium.TouchByResourceIDAsync($"{package_name}:id/miniPlayerMediaItemTitle");
                    }
                }
                // Phát Nhạc Từ Albums
                else if (await ldium.ExistAsync($"//node[@resource-id='{package_name}:id/title'][@text='Albums']") ||
                    await ldium.ExistAsync($"//node[@resource-id='{package_name}:id/title'][@text='EP & Singles']"))
                {
                    if (await ldium.ExistAsync($"//node[@resource-id='{package_name}:id/title'][@text='Albums']"))
                    {
                        var view_all = (await ldium.DumpViewHierarchyAsync())
                            .FindNode($"//node[@resource-id='{package_name}:id/title'][@text='Albums']")
                            .NextSibling;
                        await ldium.TouchAsync(view_all);

                        await ldium.WaitForExistAsync($"//node[@resource-id='{package_name}:id/toolbar']/node[@text='Albums']");
                        await Task.Delay(1000);
                    }
                    else
                    {
                        var view_all = (await ldium.DumpViewHierarchyAsync())
                            .FindNode($"//node[@resource-id='{package_name}:id/title'][@text='EP & Singles']")
                            .NextSibling;
                        await ldium.TouchAsync(view_all);

                        await ldium.WaitForExistAsync($"//node[@resource-id='{package_name}:id/toolbar']/node[@text='EP & Singles']");
                        await Task.Delay(1000);
                    }

                    // Chọn Một Dòng Ngẫu Nhiên
                    ViewNode row;
                    {
                        await Task.Delay(1000);

                        var rows = (await ldium.DumpViewHierarchyAsync())
                            .FindNodeByResourceID($"{package_name}:id/recyclerView")
                            .ChildNodes.Where((x) =>
                            {
                                var fc = x.FirstChild;
                                var lc = x.LastChild;
                                return fc != null && lc != null && fc.ResourceID.EndsWith(":id/artwork") && lc.ResourceID.EndsWith(":id/releaseYear");
                            }).ToArray();

                        row = rows[random.Next(rows.Length)];
                    }

                    // Mở Dòng Được Chọn
                    {
                        await ldium.TouchAsync(row, new TouchOptions()
                        {
                            Padding = new Padding()
                            {
                                Right = 72f
                            }
                        });

                        await ldium.WaitForExistAnyAsync(new string[]
                        {
                            $"{package_name}:id/animatedAlbumCover",
                            $"{package_name}:id/placeholderButton"
                        });

                        if (await ldium.ExistAsync($"{package_name}:id/placeholderButton"))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                not_from_artist = true;
            }

            // PHÁT NHẠC TỪ TRANG ALBUM
            if (await ldium.ExistByResourceIDAsync($"{package_name}:id/animatedAlbumCover"))
            {
                var hierarchy = await ldium.DumpViewHierarchyAsync();

                // Trường Hợp Hiện Hộp Thoại Chuyển Giao Danh Sách Phát
                if (hierarchy.FindNodeByText("Close Message") != null)
                {
                    await ldium.TouchByTextAsync("Close Message");
                    await ldium.WaitForGoneByTextAsync("Close Message");
                }

                // Cuộn Trang Một Khoảng Ngẫu Nhiên
                {
                    await Task.Delay(2000);

                    hierarchy = await ldium.DumpViewHierarchyAsync();

                    var node = hierarchy.FindNodeByResourceID($"{package_name}:id/recyclerView");
                    var x = node.Bound.Left + 24 + random.Next(node.Bound.Width - 48);
                    var y = node.Bound.Top + 24 + random.Next(node.Bound.Height - 48);
                    var distance = random.Next(100, 800);

                    await ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                    {
                        return d >= distance || await ldium.ExistByResourceIDAsync($"{package_name}:id/copyright");
                    });
                }

                // Chọn ngẫu nhiên 1 trong 3 track đầu tiên.
                ViewNode row;
                {
                    await Task.Delay(1000);

                    hierarchy = await ldium.DumpViewHierarchyAsync();

                    var rows = hierarchy.FindNodeByResourceID($"{package_name}:id/recyclerView")
                        .ChildNodes.Where((x) =>
                        {
                            var fc = x.FirstChild;
                            var lc = x.LastChild;
                            return fc != null && lc != null && fc.ResourceID.EndsWith(":id/number") && lc.ResourceID.EndsWith(":id/options");
                        }).ToArray();


                    var index = random.Next(Math.Min(3, rows.Length));
                    row = rows[index];
                }

                // Mở Bài Được Chọn
                {
                    await ldium.TouchAsync(row, new TouchOptions()
                    {
                        Padding = new Padding()
                        {
                            Right = 72f
                        }
                    });

                    // Đợi Trình Phát
                    var song_name = row.ChildNodes[1].Text;
                    for (var j = 15; true; j--)
                    {
                        await Task.Delay(2000);

                        hierarchy = await ldium.DumpViewHierarchyAsync();

                        // Kiểm Tra Hộp Thoại Giới Hạn Thiết Bị
                        var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                        if (dialog_title != null)
                        {
                            var dialog_text = dialog_title.Text;
                            if (string.Equals(dialog_text, "Playback Paused"))
                            {
                                // Đóng Băng Phiên
                                try
                                {
                                    await psm.FreezeAsync(session.session_id);
                                }
                                catch { }
                                session = null;

                                // Tung Lỗi
                                throw new Exception("Failed to play music, account is playing on another device.");
                            }
                        }

                        // Kiểm Tra Hộp Thoại Nâng Cấp Tài Khoản
                        //if (hierarchy.FindNode("//node[contains(@text, 'plans are not available')]") != null)
                        //{
                        //    // Báo Cáo Tài Khoản
                        //    try
                        //    {
                        //        await pm.UpdateAsync
                        //        (
                        //            profile_id: profile.profile_id,
                        //            status: ProfileStatus.EXPIRED
                        //        );
                        //    }
                        //    catch { }

                        //    throw new Exception("Failed to play source, account has expired.");
                        //}

                        // Kiểm Tra Tiêu Đề Bài Đang Được Phát
                        var player_title = hierarchy.FindNodeByResourceID($"{package_name}:id/miniPlayerMediaItemTitle");
                        if (player_title != null && player_title.Text.Equals(song_name))
                        {
                            break;
                        }

                        if (j <= 0)
                        {
                            throw new Exception("Failed to wait player_title.");
                        }
                    }

                    // Mở Trình Phát
                    await ldium.TouchByResourceIDAsync($"{package_name}:id/miniPlayerMediaItemTitle");
                }
            }
            else
            {
                not_from_album = true;
            }

            // PHÁT NHẠC TỪ TRANG ALBUM
            //if (await ldium.ExistByResourceIDAsync($"{package_name}:id/animatedAlbumCover"))
            //{
            //    // Lấy Tên Nghệ Sĩ
            //    var hierarchy = await ldium.DumpViewHierarchyAsync();
            //    var artists_name = hierarchy.FindNodeByResourceID($"{package_name}:id/artistNames").Text;

            //    // Phát Album
            //    await ldium.TouchByResourceIDAsync($"{package_name}:id/playbackControlButtonFirst");

            //    // Đợi Trình Phát
            //    for (var i = 15; true; i--)
            //    {
            //        await Task.Delay(2000);

            //        hierarchy = await ldium.DumpViewHierarchyAsync();

            //        // Kiểm Tra Hộp Thoại Giới Hạn Thiết Bị
            //        var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
            //        if (dialog_title != null)
            //        {
            //            var dialog_text = dialog_title.Text;
            //            if (string.Equals(dialog_text, "Playback Paused"))
            //            {
            //                // Đóng Băng Phiên
            //                try
            //                {
            //                    await psm.FreezeAsync(session.session_id);
            //                }
            //                catch { }
            //                session = null;

            //                // Tung Lỗi
            //                throw new Exception("Failed to play music, account is playing on another device.");
            //            }
            //        }

            //        // Kiểm Tra Hộp Thoại Nâng Cấp Tài Khoản
            //        if (hierarchy.FindNode("//node[contains(@text, 'plans are not available')]") != null)
            //        {
            //            // Báo Cáo Tài Khoản
            //            try
            //            {
            //                await pm.UpdateAsync
            //                (
            //                    profile_id: profile.profile_id,
            //                    status: ProfileStatus.EXPIRED
            //                );
            //            }
            //            catch { }

            //            throw new Exception("Failed to play source, account has expired.");
            //        }

            //        // Kiểm Tra Nghệ Sĩ Đang Được Phát
            //        var player_artist = hierarchy.FindNodeByResourceID($"{package_name}:id/miniPlayerArtistName");
            //        if (player_artist != null && artists_name.Contains(player_artist.Text))
            //        {
            //            break;
            //        }

            //        if (i <= 0)
            //        {
            //            throw new Exception("Failed to wait player_title.");
            //        }
            //    }

            //    // Mở Trình Phát
            //    await ldium.TouchByResourceIDAsync($"{package_name}:id/miniPlayerMediaItemTitle");
            //}
            //else
            //{
            //    not_from_album = true;
            //}

            if (not_from_artist && not_from_album)
            {
                //var xml = await ldium.UI.DumpLayoutAsync();
                //var image = await ldium.UI.ScreenShootAsync(ImageFormat.JPEG, 80);
                //var bytes = Convert.FromBase64String(image);

                //File.WriteAllText("logs\\DumpLayout.xml", xml);
                //File.WriteAllBytes("logs\\ScreenShoot.jpg", bytes);
            }

            return true;
        }

        private async Task<bool> ListenSourceAsync()
        {
            int parse_time(string text)
            {
                var split = text.Split(':');
                var min = int.Parse(split[0]);
                var sec = int.Parse(split[1]);
                return min * 60 + sec;
            }

            // ĐỢI NHẠC
            int time_total;
            int time_elapsed;
            int play_time;
            {
                time_total = 0;
                time_elapsed = 0;

                // Đợi Nhạc
                for (int i = 15; time_total == 0 || time_elapsed == 0; i--)
                {
                    await Task.Delay(2000);

                    var hierarchy = await ldium.DumpViewHierarchyAsync();

                    // Kiểm Tra Hộp Thoại Giới Hạn Thiết Bị
                    var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                    if (dialog_title != null)
                    {
                        var dialog_text = dialog_title.Text;
                        if (string.Equals(dialog_text, "Playback Paused"))
                        {
                            // Đóng Băng Phiên
                            try
                            {
                                await psm.FreezeAsync(session.session_id);
                            }
                            catch { }
                            session = null;

                            // Tung Lỗi
                            throw new Exception("Failed to play music, account is playing on another device.");
                        }
                    }

                    // Kiểm Tra Hộp Thoại Nâng Cấp Tài Khoản
                    //if (hierarchy.FindNode("//node[contains(@text, 'plans are not available')]") != null)
                    //{
                    //    // Báo Cáo Tài Khoản
                    //    try
                    //    {
                    //        await pm.UpdateAsync
                    //        (
                    //            profile_id: profile.profile_id,
                    //            status: ProfileStatus.EXPIRED
                    //        );
                    //    }
                    //    catch { }

                    //    throw new Exception("Failed to play source, account has expired.");
                    //}

                    // Kiểm Tra Thời Gian Hiện Tại
                    if (i > 0)
                    {
                        var time_total_node = hierarchy.FindNodeByResourceID($"{package_name}:id/totalTime");
                        var time_elapsed_node = hierarchy.FindNodeByResourceID($"{package_name}:id/elapsedTime");
                        if (time_total_node != null && time_elapsed_node != null)
                        {
                            time_total = parse_time(time_total_node.Text);
                            time_elapsed = parse_time(time_elapsed_node.Text);
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        throw new Exception("Failed to wait song time_total and time_elapsed.");
                    }
                }

                // Tính Thời Gian Nghe
                var play_min = await stm.GetPlayMinAsync();
                var play_max = await stm.GetPlayMaxAsync();
                play_time = Math.Min(random.Next(play_min, play_max), time_total - 10);
            }

            // NGHE NHẠC
            {
                var track_name = "";
                var stuck_count = 0;
                while (true)
                {
                    await Task.Delay(3000);

                    var hierarchy = await ldium.DumpViewHierarchyAsync();

                    // Kiểm Tra Hộp Thoại Giới Hạn Thiết Bị
                    var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                    if (dialog_title != null)
                    {
                        var dialog_text = dialog_title.Text;
                        if (string.Equals(dialog_text, "Playback Paused"))
                        {
                            // Đóng Băng Phiên
                            try
                            {
                                await psm.FreezeAsync(session.session_id);
                            }
                            catch { }
                            session = null;

                            // Tung Lỗi
                            throw new Exception("Failed to play music, account is playing on another device.");
                        }
                    }

                    // Kiểm Tra Tiêu Đề Bài Đang Được Phát
                    var track_current = hierarchy.FindNodeByResourceID($"{package_name}:id/mediaItemTitle").Text;
                    if (string.Equals(track_name, ""))
                    {
                        track_name = track_current;
                    }
                    else if (!string.Equals(track_name, track_current))
                    {
                        break;
                    }

                    // Kiểm Tra Thời Gian Hiện Tại
                    var time_current = parse_time(hierarchy.FindNodeByResourceID($"{package_name}:id/elapsedTime").Text);
                    if (time_current != time_elapsed)
                    {
                        OnLog(status = $"PLAYING: {time_current} / {time_total}");

                        time_elapsed = time_current;
                        stuck_count = 0;

                        if (time_elapsed >= play_time)
                        {
                            break;
                        }
                    }
                    else
                    {
                        OnLog(status = $"LOADING: {time_current} / {time_total}");

                        if (stuck_count >= 10)
                        {
                            break;
                        }
                        stuck_count++;
                    }
                }
            }

            // DỪNG NHẠC
            {
                try
                {
                    var node = (await ldium.DumpViewHierarchyAsync())
                        .FindNodeByResourceID($"{package_name}:id/play");
                    if (node != null && node.ContentDesc.Equals("Pause"))
                    {
                        await ldium.TouchAsync(node);
                    }
                }
                catch { }
            }

            // ĐÓNG TRÌNH PHÁT
            {
                await Task.Delay(1000);

                try
                {
                    await ldium.PressTriangleAsync();
                }
                catch { }
            }

            return time_elapsed >= 30;
        }

        private async Task ListenSourceAsync2()
        {
            int parse_time(string text)
            {
                var split = text.Split(':');
                var min = int.Parse(split[0]);
                var sec = int.Parse(split[1]);
                return min * 60 + sec;
            }

            // Đợi trình phát được mở
            await ldium.WaitForExistAsync($"//node[@resource-id='{package_name}:id/mediaItemTitle']");
            await Task.Delay(1000);

            var listen_lilmit = listen_count + random.Next(3, 5);
            while (listen_count < listen_lilmit && listen_count < listen_times)
            {
                // Lấy thông tin track đang được phát.
                var hierarchy = await ldium.DumpViewHierarchyAsync();
                var tra_name = hierarchy.FindNodeByResourceID($"{package_name}:id/mediaItemTitle").Text;
                var art_name = hierarchy.FindNodeByResourceID($"{package_name}:id/artistName").Text;

                // Đợi nhạc được phát.
                var time_total = 0;
                var time_elapsed = 0;
                for (var j = 15; time_total == 0 || time_elapsed == 0; j--)
                {
                    await Task.Delay(2000);

                    hierarchy = await ldium.DumpViewHierarchyAsync();

                    // Kiểm tra hộp thoại giới hạn thiết bị.
                    var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                    if (dialog_title != null)
                    {
                        var dialog_text = dialog_title.Text;
                        if (string.Equals(dialog_text, "Playback Paused"))
                        {
                            // Đóng băng phiên.
                            await psm.TryFreezeAsync(session.session_id);
                            session = null;

                            // Tung lỗi.
                            throw new Exception("Failed to play music, account is playing on another device.");
                        }
                    }

                    // Kiểm tra hộp thoại nâng cấp tài khoản.
                    //if (hierarchy.FindNode("//node[contains(@text, 'plans are not available')]") != null)
                    //{
                    //    // Báo cáo tài khoản.
                    //    await pm.TryUpdateAsync
                    //    (
                    //        profile_id: profile.profile_id,
                    //        status: ProfileStatus.EXPIRED
                    //    );
                    //    throw new Exception("Failed to play source, account has expired.");
                    //}

                    // Kiểm tra thời gian hiện tại.
                    if (j > 0)
                    {
                        var time_total_node = hierarchy.FindNodeByResourceID($"{package_name}:id/totalTime");
                        var time_elapsed_node = hierarchy.FindNodeByResourceID($"{package_name}:id/elapsedTime");
                        if (time_total_node != null && time_elapsed_node != null)
                        {
                            time_total = parse_time(time_total_node.Text);
                            time_elapsed = parse_time(time_elapsed_node.Text);
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        throw new Exception("Failed to wait song time_total and time_elapsed.");
                    }
                }

                // Tính thời gian nghe.
                var play_min = await stm.GetPlayMinAsync();
                var play_max = await stm.GetPlayMaxAsync();
                var play_time = Math.Min(random.Next(play_min, play_max), time_total - 10);

                // Nghe nhạc
                var stuck_count = 0;
                var track_nexted = false;
                while (true)
                {
                    await Task.Delay(3000);

                    hierarchy = await ldium.DumpViewHierarchyAsync();

                    // Kiểm tra hộp thoại giới hạn thiết bị.
                    var dialog_title = hierarchy.FindNode($"//node[@resource-id='{package_name}:id/title_template']/node[1]");
                    if (dialog_title != null)
                    {
                        var dialog_text = dialog_title.Text;
                        if (string.Equals(dialog_text, "Playback Paused"))
                        {
                            // Đóng băng phiên.
                            await psm.TryFreezeAsync(session.session_id);
                            session = null;

                            // Tung lỗi.
                            throw new Exception("Failed to play music, account is playing on another device.");
                        }
                    }

                    // Kiểm tra track đang được phát.
                    var now_tra_name = hierarchy.FindNodeByResourceID($"{package_name}:id/mediaItemTitle").Text;
                    if (!string.Equals(now_tra_name, tra_name))
                    {
                        track_nexted = true;
                        break;
                    }

                    // Kiểm tra thời gian hiện tại.
                    var time_current = parse_time(hierarchy.FindNodeByResourceID($"{package_name}:id/elapsedTime").Text);
                    if (time_current != time_elapsed)
                    {
                        int time = listen_count + 1;
                        OnLog(status = $"PLAYING [{time}/{listen_times}]: {time_current} / {time_total}");

                        time_elapsed = time_current;
                        stuck_count = 0;

                        if (time_elapsed >= play_time)
                        {
                            break;
                        }
                    }
                    else
                    {
                        OnLog(status = $"LOADING [{listen_count + 1}/{listen_times}]: {time_current} / {time_total}");

                        if (stuck_count >= 10)
                        {
                            break;
                        }
                        stuck_count++;
                    }
                }

                hierarchy = await ldium.DumpViewHierarchyAsync();

                // Dừng nhạc
                if (!track_nexted)
                {
                    var play = hierarchy.FindNodeByResourceID($"{package_name}:id/play");
                    if (play != null && play.ContentDesc.Equals("Pause"))
                    {
                        await ldium.TouchAsync(play);
                    }
                }

                // Báo cáo lượt phát
                listen_count++;
                if (time_elapsed >= 35)
                {
                    await sm.TryReportAsync(source.source_id, SourceReportType.SUCCESS);
                    sphm.IncreaseSuccess();
                }
                else
                {
                    await sm.TryReportAsync(source.source_id, SourceReportType.FAILED);
                    sphm.IncreaseFailure();
                }

                // Chuyển bài
                if (!track_nexted)
                {
                    var next = hierarchy.FindNodeByResourceID($"{package_name}:id/next");
                    if (next != null && next.Enabled && listen_count < listen_lilmit && listen_count < listen_times)
                    {
                        await ldium.TouchAsync(next);

                        // Đợi chuyển bài
                        for (var j = 15; true; j--)
                        {
                            await Task.Delay(2000);

                            hierarchy = await ldium.DumpViewHierarchyAsync();

                            var now_tra_name = hierarchy.FindNodeByResourceID($"{package_name}:id/mediaItemTitle").Text;
                            if (!string.Equals(now_tra_name, tra_name))
                            {
                                break;
                            }
                            else if (j <= 0)
                            {
                                return;
                            }
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }

        private void OnLog(string text)
        {
            if (config.DebugMode)
            {
                Console.WriteLine(text);
            }
        }

        public void PlayMusic(string link, LDium ldium, int i)
        {
            if (ldium.ExistAsync("//node[@text=\"Unable to load page.\"]").Result)
            {
                return;
            }

            ldium.ClearDumpViewHierarchyCacheAsync().Wait();
            var hierarchy = ldium.DumpViewHierarchyAsync().Result;
            if (link.Contains("album"))
            {
                // Cuộn 1 khoảng ngẫu nhiên
                Thread.Sleep(2000);
                var node = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]/node[1]");
                string data_album = node.ContentDesc;
                string pattern = @"(.*), By (.*), Album out (.*)";
                Regex regex = new Regex(pattern);
                Match match = regex.Match(data_album);
                string name_artist = match.Groups[2].Value.ToLower().Trim();
                var x = node.Bound.Left + random.Next(50, 200);
                var y = node.Bound.Bottom + random.Next(50, 200);
                var distance = random.Next(100, 1200);
                ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                {
                    return d >= distance || await ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]");
                }).Wait();
                if (!ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]").Result)
                {
                    int time_swipe = 3;
                    while (time_swipe > 0)
                    {
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        int rnd_swipe = random.Next(1, 100);
                        if (rnd_swipe < 40 && !ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]").Result)
                        {
                            distance = random.Next(100, 1200);
                            ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                            {
                                return d >= distance || await ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]");
                            }).Wait();

                        }
                        else
                        {
                            break;
                        }

                        time_swipe--;
                        Thread.Sleep(1000);
                    }
                }

                // Lấy ra các node có thể play được
                ViewNode[] nodes_list1 = null;
                List<ViewNode> nodes_list = new List<ViewNode>();
                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                hierarchy = ldium.DumpViewHierarchyAsync().Result;
                nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;
                var nodes = nodes_list1[nodes_list1.Length - 1].ChildNodes;
                foreach (var node_details in nodes)
                {
                    if (node_details.Bound.Top < 1480 && node_details.Bound.Top > 200)
                    {
                        nodes_list.Add(node_details);
                    }
                }


                // Play 1 trong số các track trong album
                int time_play = 3;
                while (time_play > 0)
                {
                    int index_track = random.Next(0, nodes_list.Count);
                    ViewNode[] node_list_focus = nodes_list[index_track].ChildNodes;
                    ViewNode node_focus = node_list_focus[1];
                    ldium.TouchAsync(node_focus).Wait();
                    Thread.Sleep(3000);
                    if (!ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        break;
                    }
                    time_play--;
                    //Thread.Sleep(2000);
                }
                string name_artist_player2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();

                OnLog(status = "Wait Music Run");
                int time2 = 8;
                while (time2 > 0)
                {

                    if (time2 < 6 && ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Wait();
                    }
                    if (name_artist.Contains(name_artist_player2) || name_artist_player2.Contains(name_artist))
                    {
                        Thread.Sleep(2000);
                        break;
                    }
                    time2--;
                    Thread.Sleep(2000);
                }
                if (time2 == 0)
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                string first_name_track2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower().Trim();
                ListenMusic(link, ldium, first_name_track2, name_artist, i);


                return;
            }
            else
            {
                try
                {
                    //ldium.SwipeUntilExistAsync(0, 100, 100, "//node[@text=\"View all\"]").Wait(5000);

                    ldium.SwipeUntilAsync(SwipeDirection.UP, 200, 200, async (distance) =>
                    {
                        return await ldium.ExistAsync("//node[@content-desc=\"View all\"]") || distance > 500;
                    }).Wait();
                }
                catch
                {
                }

                if (ldium.ExistAsync("//node[@content-desc=\"View all\"]").Result)
                {

                    Thread.Sleep(500);
                    ldium.SwipeAsync(0, 100, 100, 200).Wait();
                    ldium.TouchAsync("//node[@text=\"View all\"]").Wait();
                    Thread.Sleep(3000);

                    ViewNode[] nodes2 = null;
                    int times = 3;
                    while (times > 0)
                    {
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        nodes2 = ldium.DumpViewHierarchyAsync().Result.FindNodes("//node[@resource-id=\"deezer.android.app:id/title\"]");
                        if (nodes2.Length != 0)
                        {
                            break;
                        }
                        times--;
                        Thread.Sleep(1000);
                    }
                    if (times == 0)
                    {
                        sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                        sphm.IncreaseFailure();
                        return;
                    }
                    List<ViewNode> nodes = new List<ViewNode>();
                    ViewNode[] nodes_list1 = null;
                    // Tối đa vuốt 3 đoạn
                    int time_swipe = 3;
                    int index = 0;
                    while (time_swipe > 0)
                    {
                        hierarchy = ldium.DumpViewHierarchyAsync().Result;
                        nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/swipe_refresh_layout\"]/node[1]").ChildNodes;
                        ViewNode viewNode = nodes_list1[nodes_list1.Length - 1];
                        // Nếu có thể vuốt
                        if (nodes_list1.Length > 9)
                        {
                            index = random.Next(1, 100);
                            if (index < 40)
                            {
                                ldium.SwipeAsync(0, 200, 200, random.Next(300, 700)).Wait();

                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                        time_swipe--;
                        Thread.Sleep(2000);
                    }

                    hierarchy = ldium.DumpViewHierarchyAsync().Result;
                    nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/swipe_refresh_layout\"]/node[1]").ChildNodes;
                    if (nodes_list1[0].ContentDesc != null && nodes_list1[0].Bound.Height > nodes_list1[1].Bound.Height - 40)
                    {
                        nodes.Add(nodes_list1[0]);
                    }
                    for (int j = 1; j < nodes_list1.Length; j++)
                    {
                        if (nodes_list1[j].Bound.Top < 1450)
                        {
                            nodes.Add(nodes_list1[j]);
                        }
                    }
                    index = random.Next(0, nodes.Count);
                    ViewNode node_fucus = nodes[index].FirstChild;

                    ldium.TouchAsync(node_fucus).Wait();

                    //ldium.TouchAsync("//node[@text=\"" + node_fucus.Text + "\" and @resource-id=\"deezer.android.app:id/title\"]",

                    //    new TouchOptions()
                    //    {
                    //        Padding = new Padding() { Bottom = 40, Top = 40, Left = 20, Right = 20 }

                    //    }
                    //).Wait();
                }
                else if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]").Result)
                {
                    ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]",
                        new TouchOptions()
                        {
                            Padding = new Padding() { Bottom = 40, Top = 40, Left = 20, Right = 20 }

                        }

                        ).Wait();
                }
                else if (ldium.ExistAsync("//node[@text=\"No Items to Display\"]").Result)
                {
                    return;
                }
                else
                {
                    string text = "//node[@resource-id=\"deezer.android.app:id/card\"]/node[@resource-id=\"deezer.android.app:id/card_play_button\"]/node[1]/node[1]/node[1]";
                    TouchOptions touchOptions2 = new TouchOptions();
                    Padding padding = default(Padding);
                    padding.Bottom = 40;
                    padding.Left = 30;
                    padding.Right = 30;
                    padding.Top = 40;
                    touchOptions2.Padding = padding;
                    ldium.TouchAsync(text, touchOptions2).Wait();
                }
                Thread.Sleep(4000);
                OnLog(status = "Wait Player Artist");
                try
                {
                    ldium.WaitForExistAsync("//node[@resource-id=\"deezer.android.app:id/player_background\"]").Wait();
                }
                catch
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                ClickPopup(ldium);
                string name_artist2;
                if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/toolbar_title\"]").Result)
                {
                    name_artist2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/toolbar_title\"]").Text.ToLower().Trim();
                }
                else
                {
                    name_artist2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/toolbar\"]/node[2]").Text.ToLower().Trim();
                }
                string name_artist_player2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();

                OnLog(status = "Wait Music Run");
                int time2 = 10;
                while (time2 > 0)
                {
                    if (time2 < 6 && ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Wait();
                    }
                    if (name_artist2.Contains(name_artist_player2) || name_artist_player2.Contains(name_artist2))
                    {
                        Thread.Sleep(2000);
                        break;
                    }
                    time2--;
                    Thread.Sleep(2000);
                }
                if (time2 == 0)
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                string first_name_track2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower().Trim();
                ListenMusic(link, ldium, first_name_track2, name_artist2, i);
            }
        }

        public void ListenMusic(string link, LDium ldium, string first_name_track, string name_artist, int luot)
        {
            ldium.WaitForExistAsync("//node[@resource-id=\"deezer.android.app:id/seekbar_text_left\" or @resource-id=\"deezer.android.app:id/seekbar_text_right\" or @resource-id=\"android:id/button1\"]").Wait();
            ClickPopup(ldium);
            ViewNode node = ldium.DumpViewHierarchyAsync().Result;
            int time = 15;
            while (time > 0)
            {
                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                node = ldium.DumpViewHierarchyAsync().Result;
                string text = node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text;
                if (!string.IsNullOrEmpty(node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text) && node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text != "0:00")
                {
                    break;
                }
                time--;
                Thread.Sleep(2000);
            }
            if (time == 0)
            {
                sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                sphm.IncreaseFailure();
                return;
            }
            int track_number = 1;
            while (track_number <= 5)
            {
                string name_track = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower().Trim();
                string name_artist_player = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();
                if ((!name_artist_player.Contains(name_artist) && !name_artist.Contains(name_artist_player)) || (name_track == first_name_track && track_number > 1))
                {
                    break;
                }
                else
                {

                }
                int duration = ConvertTime(node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_right").Text);
                int now_time = 0;
                int time_max = new Random().Next(90, 120);
                bool send = false;
                if (duration <= time_max)
                {
                    time_max = duration;
                }

                for (int i = 0; i < 30; i++)
                {
                    ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                    ViewNode node2 = ldium.DumpViewHierarchyAsync().Result;
                    if (ldium.ExistAsync("//node[@text=\"x\"]").Result)
                    {
                        ldium.TouchAsync("//node[@text=\"x\"]").Wait();
                    }
                    try
                    {
                        now_time = ConvertTime(node2.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text);
                    }
                    catch
                    {
                        if (ldium.ExistByResourceIDAsync("android:id/message").Result)
                        {
                            profile = null;
                            throw new Exception("Playing together");
                        }
                        if (ldium.ExistAsync("//node[@text=\"Maybe later\"]").Result)
                        {
                            ldium.TouchAsync("//node[@text=\"Maybe later\"]", null).Wait();
                        }
                    }
                    if (i < 5 && now_time > 40)
                    {
                        Thread.Sleep(4000);
                    }
                    if (i > 6 && now_time == 0)
                    {
                        sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                        sphm.IncreaseFailure();
                        throw new Exception("Playing failed");

                    }
                    else
                    {
                        if (now_time > time_max - 4)
                        {
                            sm.TryReportAsync(source.source_id, SourceReportType.SUCCESS).Wait();
                            sphm.IncreaseSuccess();
                            send = true;
                            break;
                        }
                        OnLog(status = string.Concat(new string[]
                        {
                            "Playing [" + (luot + 1) + ",",
                            track_number.ToString(),
                            "] : ",
                            now_time.ToString(),
                            "/",
                            time_max.ToString()
                        }));
                        Thread.Sleep(4000);
                    }
                }
                //if (!send)
                //{
                //    api.Source.SendReport(artistId, true);
                //}
                track_number++;
                if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_next_collapsed\" and @enabled=\"false\"]").Result)
                {
                    break;
                }
                ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_next_collapsed\"]").Wait();
                Thread.Sleep(4000);
            }
        }

        public void PlayMusic2(string link, LDium ldium, int i)
        {
            if (ldium.ExistAsync("//node[@text=\"Unable to load page.\"]").Result)
            {
                return;
            }

            ldium.ClearDumpViewHierarchyCacheAsync().Wait();
            var hierarchy = ldium.DumpViewHierarchyAsync().Result;
            if (link.Contains("album"))
            {
                // Cuộn 1 khoảng ngẫu nhiên
                Thread.Sleep(2000);
                var node = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]/node[1]");
                //string data_album = node.ContentDesc;
                //string pattern = @"(.*), By (.*), Album out (.*)";
                //Regex regex = new Regex(pattern);
                //Match match = regex.Match(data_album);
                var x = node.Bound.Left + random.Next(50, 200);
                var y = node.Bound.Bottom + random.Next(50, 200);
                var distance = random.Next(100, 1200);
                ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                {
                    return d >= distance || await ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]");
                }).Wait();
                if (!ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]").Result)
                {
                    int time_swipe = 3;
                    while (time_swipe > 0)
                    {
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        int rnd_swipe = random.Next(1, 100);
                        if (rnd_swipe < 40 && !ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]").Result)
                        {
                            distance = random.Next(100, 1200);
                            ldium.SwipeUntilAsync(SwipeDirection.UP, x, y, async (d) =>
                            {
                                return d >= distance || await ldium.ExistAsync("//node[@content-desc=\"By the same artist\"]");
                            }).Wait();

                        }
                        else
                        {
                            break;
                        }

                        time_swipe--;
                        Thread.Sleep(1000);
                    }
                }

                // Lấy ra các node có thể play được
                ViewNode[] nodes_list1 = null;
                List<ViewNode> nodes_list = new List<ViewNode>();
                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                hierarchy = ldium.DumpViewHierarchyAsync().Result;
                nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;
                var nodes = nodes_list1[nodes_list1.Length - 1].ChildNodes;
                foreach (var node_details in nodes)
                {
                    if (node_details.ChildNodes.Length > 4)
                    {
                        if (node_details.Bound.Top < 1480 && node_details.Bound.Top > 200)
                        {
                            nodes_list.Add(node_details);
                        }

                    }
                }

                // Play 1 trong số các track trong album
                int time_play = 3;
                while (time_play > 0)
                {
                    int index_track = random.Next(0, nodes_list.Count);
                    ViewNode[] node_list_focus = nodes_list[index_track].ChildNodes;
                    ViewNode node_focus = node_list_focus[1];
                    ldium.TouchAsync(node_focus, new TouchOptions()
                    {

                        Padding = new Padding { Bottom = 10, Top = 10, Right = 10, Left = 10 }

                    }).Wait();
                    Thread.Sleep(4000);
                    if (!ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        break;
                    }
                    time_play--;
                }
            }
            else
            {
                try
                {

                    ldium.SwipeUntilAsync(SwipeDirection.UP, 200, 200, async (distance) =>
                    {
                        return await ldium.ExistAsync("//node[@content-desc=\"View all\"]") || distance > 500;
                    }).Wait();
                }
                catch
                {
                }

                if (ldium.ExistAsync("//node[@content-desc=\"View all\"]").Result)
                {

                    Thread.Sleep(500);
                    ldium.SwipeAsync(0, 100, 100, 200).Wait();
                    ldium.TouchAsync("//node[@text=\"View all\"]").Wait();
                    Thread.Sleep(3000);

                    ViewNode[] nodes2 = null;
                    int times = 3;
                    while (times > 0)
                    {
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        nodes2 = ldium.DumpViewHierarchyAsync().Result.FindNodes("//node[@resource-id=\"deezer.android.app:id/title\"]");
                        if (nodes2.Length != 0)
                        {
                            break;
                        }
                        times--;
                        Thread.Sleep(1000);
                    }
                    if (times == 0)
                    {
                        sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                        sphm.IncreaseFailure();
                        return;
                    }
                    List<ViewNode> nodes = new List<ViewNode>();
                    ViewNode[] nodes_list1 = null;
                    // Tối đa vuốt 3 đoạn
                    int time_swipe = 3;
                    int index = 0;
                    while (time_swipe > 0)
                    {
                        hierarchy = ldium.DumpViewHierarchyAsync().Result;
                        nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/swipe_refresh_layout\"]/node[1]").ChildNodes;
                        ViewNode viewNode = nodes_list1[nodes_list1.Length - 1];
                        // Nếu có thể vuốt
                        if (nodes_list1.Length > 9)
                        {
                            index = random.Next(1, 100);
                            if (index < 40)
                            {
                                ldium.SwipeAsync(0, 200, 200, random.Next(300, 700)).Wait();

                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                        time_swipe--;
                        Thread.Sleep(2000);
                    }

                    hierarchy = ldium.DumpViewHierarchyAsync().Result;
                    nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/swipe_refresh_layout\"]/node[1]").ChildNodes;
                    if (nodes_list1[0].ContentDesc != null && nodes_list1[0].Bound.Height > nodes_list1[1].Bound.Height - 40)
                    {
                        nodes.Add(nodes_list1[0]);
                    }
                    for (int j = 1; j < nodes_list1.Length; j++)
                    {
                        if (nodes_list1[j].Bound.Top < 1450)
                        {
                            nodes.Add(nodes_list1[j]);
                        }
                    }
                    if (nodes.Count > 3)
                    {
                        index = random.Next(0, nodes.Count - 2);
                    }
                    else
                    {

                        index = random.Next(0, 2);
                    }
                    ViewNode node_fucus = nodes[index].FirstChild;

                    ldium.TouchAsync(node_fucus).Wait();
                }
                else if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]").Result)
                {
                    ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]",
                        new TouchOptions()
                        {
                            Padding = new Padding() { Bottom = 40, Top = 40, Left = 20, Right = 20 }

                        }

                        ).Wait();
                }
                else if (ldium.ExistAsync("//node[@text=\"No Items to Display\"]").Result)
                {
                    return;
                }
                else
                {
                    string text = "//node[@resource-id=\"deezer.android.app:id/card\"]/node[@resource-id=\"deezer.android.app:id/card_play_button\"]/node[1]/node[1]/node[1]";
                    TouchOptions touchOptions2 = new TouchOptions();
                    Padding padding = default(Padding);
                    padding.Bottom = 40;
                    padding.Left = 30;
                    padding.Right = 30;
                    padding.Top = 40;
                    touchOptions2.Padding = padding;
                    ldium.TouchAsync(text, touchOptions2).Wait();
                }
                Thread.Sleep(4000);

            }
        }

        public void ListenMusic2(string link, LDium ldium, int luot)
        {
            ldium.ClearDumpViewHierarchyCacheAsync().Wait();
            var hierarchy = ldium.DumpViewHierarchyAsync().Result;
            string first_name_track = "";
            string name_artist = "";
            if (link.Contains("album"))
            {
                Thread.Sleep(2000);
                var node_list = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;

                for (int i = 0; i < node_list.Length; i++)
                {
                    if (node_list[i].Text != null && node_list[i].Text != "")
                    {
                        if (name_artist == "")
                        {
                            name_artist = node_list[i].Text;
                            break;
                        }
                    }
                }
                string name_artist_player2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();

                OnLog(status = "Wait Music Run");
                int time2 = 8;
                while (time2 > 0)
                {
                    if (time2 < 6 && ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Wait();
                    }
                    if (name_artist.ToLower().Contains(name_artist_player2.ToLower()) || name_artist_player2.ToLower().Contains(name_artist.ToLower()))
                    {
                        Thread.Sleep(2000);
                        break;
                    }
                    time2--;
                    Thread.Sleep(2000);
                }
                if (time2 == 0)
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                first_name_track = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower();
            }
            else
            {
                OnLog(status = "Wait Player Artist");
                try
                {
                    ldium.WaitForExistAsync("//node[@resource-id=\"deezer.android.app:id/player_background\"]").Wait();
                }
                catch
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                ClickPopup(ldium);
                if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/toolbar_title\"]").Result)
                {
                    name_artist = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/toolbar_title\"]").Text.ToLower().Trim();
                }
                else
                {
                    name_artist = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/toolbar\"]/node[2]").Text.ToLower().Trim();
                }
                string name_artist_player2 = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();

                OnLog(status = "Wait Music Run");


                int time2 = 10;

                while (time2 > 0)
                {
                    if (time2 < 6 && ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Result)
                    {
                        ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_play_pause_collapsed\" and @content-desc=\"Play\"]").Wait();
                    }
                    if (name_artist.ToLower().Contains(name_artist_player2.ToLower()) || name_artist_player2.ToLower().Contains(name_artist.ToLower()))
                    {
                        Thread.Sleep(2000);
                        break;
                    }
                    time2--;
                    Thread.Sleep(2000);
                }
                if (time2 == 0)
                {
                    sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                    sphm.IncreaseFailure();
                    return;
                }
                first_name_track = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower().Trim();
            }




            ldium.WaitForExistAsync("//node[@resource-id=\"deezer.android.app:id/seekbar_text_left\" or @resource-id=\"deezer.android.app:id/seekbar_text_right\" or @resource-id=\"android:id/button1\"]").Wait();
            ClickPopup(ldium);
            ViewNode node = ldium.DumpViewHierarchyAsync().Result;
            int time = 15;
            while (time > 0)
            {
                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                node = ldium.DumpViewHierarchyAsync().Result;
                string text = node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text;
                if (!string.IsNullOrEmpty(node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text) && node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text != "0:00")
                {
                    break;
                }
                time--;
                Thread.Sleep(2000);
            }
            if (time == 0)
            {
                sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                sphm.IncreaseFailure();
                return;
            }
            ldium.ClearDumpViewHierarchyCacheAsync().Wait();

            int track_number = 1;
            var listen_lilmit = listen_count + random.Next(3, 5);
            while (listen_count < listen_lilmit && listen_count < listen_times)
            {
                //    while (track_number <= 5)
                //{
                string name_track = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_title_collapsed\"]").Text.ToLower().Trim();
                string name_artist_player = ldium.DumpViewHierarchyAsync().Result.FindNode("//node[@resource-id=\"deezer.android.app:id/track_sub_title_collapsed\"]").Text.ToLower().Trim();
                if ((!name_artist_player.ToLower().Contains(name_artist.ToLower()) && !name_artist.ToLower().Contains(name_artist_player.ToLower())) || (name_track == first_name_track && track_number > 1))
                {
                    break;
                }
                else
                {

                }
                int duration = ConvertTime(node.FindNodeByResourceID("deezer.android.app:id/seekbar_text_right").Text);
                int now_time = 0;
                int time_max = new Random().Next(90, 120);
                bool send = false;
                if (duration <= time_max)
                {
                    time_max = duration;
                }

                for (int i = 0; i < 30; i++)
                {
                    ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                    ViewNode node2 = ldium.DumpViewHierarchyAsync().Result;
                    if (ldium.ExistAsync("//node[@text=\"x\"]").Result)
                    {
                        ldium.TouchAsync("//node[@text=\"x\"]").Wait();
                    }
                    try
                    {
                        now_time = ConvertTime(node2.FindNodeByResourceID("deezer.android.app:id/seekbar_text_left").Text);
                    }
                    catch
                    {
                        if (ldium.ExistByResourceIDAsync("android:id/message").Result)
                        {
                            profile = null;
                            throw new Exception("Playing together");
                        }
                        if (ldium.ExistAsync("//node[@text=\"Maybe later\"]").Result)
                        {
                            ldium.TouchAsync("//node[@text=\"Maybe later\"]", null).Wait();
                        }
                    }
                    if (i < 5 && now_time > 40)
                    {
                        Thread.Sleep(4000);
                    }
                    if (i > 6 && now_time == 0)
                    {
                        sm.TryReportAsync(source.source_id, SourceReportType.FAILED).Wait();
                        sphm.IncreaseFailure();
                        throw new Exception("Playing failed");

                    }
                    else
                    {
                        if (now_time > time_max - 4)
                        {
                            sm.TryReportAsync(source.source_id, SourceReportType.SUCCESS).Wait();
                            sphm.IncreaseSuccess();
                            send = true;
                            break;
                        }
                        OnLog(status = string.Concat(new string[]
                        {
                            "Playing [" + (listen_count + 1),
                            "] : ",
                            now_time.ToString(),
                            "/",
                            time_max.ToString()
                        }));
                        Thread.Sleep(4000);
                    }
                }
                //if (!send)
                //{
                //    api.Source.SendReport(artistId, true);
                //}
                if (ldium.ExistAsync("//node[@resource-id=\"deezer.android.app:id/player_next_collapsed\" and @enabled=\"false\"]").Result)
                {
                    // Nếu không thể play 
                    if (link.Contains("album"))
                    {
                        // Nếu trường hợp là track cuôi của Album
                        if (track_number > 1)
                        {
                            ViewNode[] nodes_list1 = null;
                            List<ViewNode> nodes_list = new List<ViewNode>();
                            ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                            hierarchy = ldium.DumpViewHierarchyAsync().Result;
                            nodes_list1 = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;
                            var nodes = nodes_list1[nodes_list1.Length - 1].ChildNodes;
                            foreach (var node_details in nodes)
                            {
                                if (node_details.ChildNodes.Length > 4)
                                {
                                    if (node_details.Bound.Top < 1480 && node_details.Bound.Top > 200)
                                    {
                                        nodes_list.Add(node_details);
                                    }

                                }
                            }
                            ViewNode[] node_list_focus = nodes_list1[1].ChildNodes;
                            ViewNode node_focus = node_list_focus[1];
                            ldium.TouchAsync(node_focus).Wait();
                            Thread.Sleep(2000);
                        }
                        // Nếu trường hợp album chỉ có đúng 1 track
                        else
                        {
                            // Nếu nghệ sĩ đấy có nhiều album khác nữa thì kéo xuống dưới play bừa
                            if (source.source_id.Contains("anony"))
                            {
                                var node_detail = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]/node[1]");
                                var x = node_detail.Bound.Left + random.Next(50, 200);
                                var y = node_detail.Bound.Bottom + random.Next(50, 200);
                                ldium.SwipeAsync(0, x, y, random.Next(700, 1000)).Wait();
                                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                                hierarchy = ldium.DumpViewHierarchyAsync().Result;
                                var nodes_play = hierarchy.FindNodes("//node[@content-desc=\"Play\"]");
                                ViewNode viewNode = nodes_play[0];
                                ldium.TouchAsync(viewNode);
                                Thread.Sleep(2000);
                            }
                            else
                            {
                                ldium.TouchAsync("//node[@content-desc=\"Home\"]").Wait();
                                Thread.Sleep(2000);
                                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                                hierarchy = ldium.DumpViewHierarchyAsync().Result;
                                var nodes = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/horizontal_grid\"]").ChildNodes;
                                string text = nodes[1].ContentDesc;
                                ldium.TouchAsync(nodes[1]).Wait();
                                Thread.Sleep(2000);
                                ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                                hierarchy = ldium.DumpViewHierarchyAsync().Result;
                                ViewNode[] nodes_list = null;
                                ViewNode viewNode = null;
                                if (text.ToLower().Contains("artist"))
                                {
                                    nodes_list = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]").ChildNodes;
                                    viewNode = nodes_list[nodes_list.Length - 2];
                                    //ldium.TouchAsync(viewNode);
                                    //Thread.Sleep(3000);
                                }
                                else if (text.ToLower().Contains("playlist"))
                                {
                                    viewNode = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]");
                                }
                                else
                                {
                                    nodes_list = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;
                                    viewNode = nodes_list[nodes_list.Length - 2];

                                }
                                //ViewNode viewNode = nodes_list[nodes_list.Length - 2];
                                ldium.TouchAsync(viewNode);
                                Thread.Sleep(3000);

                            }


                        }
                    }
                    else
                    {
                        ldium.TouchAsync("//node[@content-desc=\"Home\"]").Wait();
                        Thread.Sleep(2000);
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        hierarchy = ldium.DumpViewHierarchyAsync().Result;
                        var nodes = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/horizontal_grid\"]").ChildNodes;
                        string text = nodes[1].ContentDesc;
                        ldium.TouchAsync(nodes[1]).Wait();
                        Thread.Sleep(2000);
                        ldium.ClearDumpViewHierarchyCacheAsync().Wait();
                        hierarchy = ldium.DumpViewHierarchyAsync().Result;
                        ViewNode[] nodes_list = null;
                        ViewNode viewNode = null;
                        if (text.ToLower().Contains("artist"))
                        {
                            nodes_list = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]").ChildNodes;
                            viewNode = nodes_list[nodes_list.Length - 2];
                            //ldium.TouchAsync(viewNode);
                            //Thread.Sleep(3000);
                        }
                        else if (text.ToLower().Contains("playlist"))
                        {
                            viewNode = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/masthead_motion_layout_main_container\"]/node[1]/node[1]/node[1]");
                        }
                        else
                        {
                            nodes_list = hierarchy.FindNode("//node[@resource-id=\"deezer.android.app:id/fl_insets\"]/node[1]/node[1]/node[1]/node[1]").ChildNodes;
                            viewNode = nodes_list[nodes_list.Length - 2];

                        }
                        //ViewNode viewNode = nodes_list[nodes_list.Length - 2];
                        ldium.TouchAsync(viewNode);
                        Thread.Sleep(3000);

                    }

                    break;
                }


                ldium.TouchAsync("//node[@resource-id=\"deezer.android.app:id/player_next_collapsed\"]").Wait();
                listen_count++;
                track_number++;



                Thread.Sleep(4000);
            }
        }


        public static int ConvertTime(string time)
        {
            string[] split = time.Split(new char[]
            {
                ':'
            });
            return int.Parse(split[0]) * 60 + int.Parse(split[1]);
        }

        // Token: 0x06000022 RID: 34 RVA: 0x0000514C File Offset: 0x0000334C
        public void ClickPopup(LDium ldium)
        {
            int times = 5;
            while (times > 0 && ldium.ExistAsync("//node[@resource-id=\"android:id/button1\"]").Result)
            {
                ldium.TouchAsync("//node[@resource-id=\"android:id/button1\"]", null).Wait();
                times--;
                Thread.Sleep(2000);
            }
        }


        protected override Task<bool> OnErrorAsync(Exception ex)
        {
            // Giết LDium Nếu Cần
            if (ex is JavaException jex && string.Equals(jex.Message, "android.os.DeadSystemException"))
            {
                machine.RunShell("killall", "ldium").Dispose();
                return Task.FromResult(true);
            }

            // # DEBUG
            if (config.DebugMode)
            {
                Console.WriteLine(ex.ToString());
            }

            // # LOG
            Logger.Create<MainAsyncAutoThread>()
                .AppendError(ex).Commit();

            return Task.FromResult(true);
        }

        protected override async Task<bool> OnFinalAsync()
        {
            // DỌN SOURCE
            if (source != null)
            {
                source = null;
            }

            // DỌN PROXY
            if (proxy != null)
            {
                proxy = null;
            }

            // DỌN PROFILE
            if (profile != null)
            {
                // sao lưu profile nếu có thể.
                if (backupable)
                {
                    try
                    {
                        var data = await ldium.BackupAppAsync(package_name, new BackupOptions
                        {
                            IncludePrivateData = true,
                            IncludePrivateCache = false,
                            IncludePublicData = false,
                            IncludePublicCache = false,
                            Ignores = new string[]
                            {
                                 "/data/data/deezer.android.app/files/data/smart/track"
                            }
                        });
                        using (var mem = new MemoryStream(data))
                        {
                            await pm.WritePayloadAsync(profile.profile_id, mem);
                        };
                    }
                    catch { }
                }

                profile = null;
            }

            // DỌN PHIÊN PROFILE
            if (session != null)
            {
                try
                {
                    await psm.CloseAsync(session.session_id);
                }
                catch { }
                session = null;
            }

            // DỌN LDIUM
            if (ldium != null)
            {
                ldium.Dispose();
                ldium = null;
            }

            // DỌN NHỮNG BIẾN KHÁC
            backupable = false;

            return true;
        }
    }
}
