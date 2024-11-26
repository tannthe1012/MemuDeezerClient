using BoosterClient.Models;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoosterClient.Branchs
{
    public class ProxyBranch : Branch
    {
        public ProxyBranch(APIClient client) : base(client) { }

        public Task<Proxy> GET(int proxy_id) =>
            Client.RequestAsync<Proxy>(HttpMethod.Get, $"api/proxy/{proxy_id}");
    }
}
