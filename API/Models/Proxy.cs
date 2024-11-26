namespace BoosterClient.Models
{
    public class Proxy
    {
        public int proxy_id { get; set; }

        public int group_id { get; set; }

        public string host { get; set; } = "";

        public int port { get; set; }

        public string username { get; set; }

        public string password { get; set; }

        public string country { get; set; }

        public string timezone { get; set; }

        public double? latitude { get; set; }

        public double? longitude { get; set; }

        public ProxyType type { get; set; }
    }

    public enum ProxyType
    {
        HTTPS = 0,
        SOCKS5 = 1
    }
}
