using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MemuDeezerClient.Utils
{
    internal class NetworkUtil
    {
        public static string GetLocalIP()
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in ifaces)
            {
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    var props = iface.GetIPProperties();
                    foreach (var uaddr in props.UnicastAddresses)
                    {
                        var addr = uaddr.Address;
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ip = addr.ToString();
                            if (ip.StartsWith("192.168") && !ip.StartsWith("192.168.56"))
                            {
                                return ip;
                            }
                        }
                    }
                }
            }
            return null;
        }
    }
}
