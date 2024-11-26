using AutomationFramework;
using BoosterClient.Exceptions;
using BoosterClient.Models;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MemuDeezerClient;
using MemuDeezerClient.Log;
using MemuDeezerClient.Managers;

namespace BoosterClient.Managers
{
    public class ClientManager
    {
        private class ClientData : Client
        {
            public string user { get; set; }

            public string uuid { get; set; }
        }

        private readonly AutomationApplication app;
        private readonly APIClient client;
        private readonly SPHManager sph;
        private readonly SemaphoreSlim semaphore;
        private readonly Timer authorize_client_timer;
        private readonly Timer report_client_timer;

        private Client client_info = null;

        public ClientManager(AutomationApplication app, APIClient client, SPHManager sph)
        {
            this.app = app;
            this.client = client;
            this.sph = sph;

            // TẠO KHÓA AN TOÀN.
            semaphore = new SemaphoreSlim(1, 1);

            // KHỞI CHẠY BỘ HẸN GIỜ.
            authorize_client_timer = new Timer(OnAuthorizeClient, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            report_client_timer = new Timer(OnReportClient, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
        }

        /// <summary>
        /// Hàm được gọi mỗi giờ một lần, có công việc tái xác thực client.
        /// </summary>
        private async void OnAuthorizeClient(object state)
        {
            if (client_info != null)
            {
                var logger = Logger.Create("ClientManager.OnAuthorizeClient");
                try
                {
                    await client.AuthorizeAsync(client_info.client_id, client_info.secret);
                    logger.AppendLine("INFO", "Authorize client successfuly");
                }
                catch (Exception ex)
                {
                    logger.AppendError(ex);
                }
                logger.Commit();
            }
        }

        /// <summary>
        /// Hàm được gọi 3 phút một lần, có công việc báo cáo trạng thái client.
        /// </summary>
        private async void OnReportClient(object state)
        {
            var sph = this.sph.TotalSPH;
            var thread_count = app.ThreadManager.ThreadCount;
            try
            {
                await ReportAsync(sph, thread_count);
            }
            catch { }
        }

        public Task PingAsync()
        {
            return client.Client.GET_Ping();
        }

        public async Task AuthorizeAsync()
        {
            if (client.BearerToken == null)
            {
                await semaphore.WaitAsync();
                try
                {
                    if (client.BearerToken == null)
                    {
                        var data = RestoreClientData();
                        try
                        {
                            if (data != null)
                            {
                                await client.AuthorizeAsync(data.client_id, data.secret);
                                client_info = data;

                                var address = GetClientAddress();
                                var user = GetClientUser();
                                var name = string.Concat(address, " - ", user);
                                if (!string.Equals(client_info.name, name) && await TryUpdateAsync(name))
                                {
                                    client_info.name = name;
                                    BackupClientData(client_info);
                                }
                            }
                            else
                            {
                                throw new UnauthorizedException();
                            }
                        }
                        catch (UnauthorizedException)
                        {
                            var address = GetClientAddress();
                            var user = GetClientUser();
                            var name = string.Concat(address, " - ", user);
                            var temp = await client.Client.POST_Register(name);

                            await client.AuthorizeAsync(temp.client_id, temp.secret);
                            client_info = temp;

                            BackupClientData(temp);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }

        public Task UpdateAsync(string name)
        {
            return client.Client.PUT_Update(name);
        }

        public async Task<bool> TryUpdateAsync(string name)
        {
            try
            {
                await UpdateAsync(name);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task ReportAsync(int sph, int thread_count)
        {
            return client.Client.PUT_Report(sph, thread_count);
        }

        public async Task<bool> TryReportAsync(int sph, int thread_count)
        {
            try
            {
                await ReportAsync(sph, thread_count);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private ClientData RestoreClientData()
        {
            var file = Path.Combine(Build.DATA_DIR, "client.json");
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonConvert.DeserializeObject<ClientData>(json);
                if (data != null)
                {
                    var user = GetClientUser();
                    var uuid = GetClientUUID();
                    if (!string.Equals(data.user, user) || !string.Equals(data.uuid, uuid))
                    {
                        return null;
                    }
                }
                return data;
            }
            catch
            {
                return null;
            }
        }

        private void BackupClientData(Client data)
        {
            var file = Path.Combine(Build.DATA_DIR, "client.json");
            try
            {
                var user = GetClientUser();
                var uuid = GetClientUUID();
                var json = JsonConvert.SerializeObject(new ClientData()
                {
                    client_id = data.client_id,
                    name = data.name,
                    secret = data.secret,
                    user = user,
                    uuid = uuid
                });
                File.WriteAllText(file, json);
            }
            catch { }
        }

        private string GetClientAddress()
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in ifaces)
            {
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Ethernet && iface.OperationalStatus == OperationalStatus.Up)
                {
                    var props = iface.GetIPProperties();
                    foreach (var uaddr in props.UnicastAddresses)
                    {
                        var addr = uaddr.Address;
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ip = addr.ToString();
                            if (ip.StartsWith("192."))
                            {
                                return ip;
                            }
                        }
                    }
                }
            }
            return "0.0.0.0";
        }

        private string GetClientUser()
        {
            var identity = WindowsIdentity.GetCurrent();
            var name = identity.Name;
            var index = name.IndexOf('\\');
            if (index >= 0)
            {
                name = name.Substring(index + 1);
            }
            return name.ToUpper();
        }

        private string GetClientUUID()
        {
            using (var mc = new ManagementClass("WIN32_ComputerSystemProduct"))
            using (var moc = mc.GetInstances())
            {
                foreach (var mo in moc)
                {
                    using (mo)
                    {
                        return mo["UUID"].ToString();
                    }
                }
            }
            return "00000000-0000-0000-0000-000000000000";
        }
    }
}
