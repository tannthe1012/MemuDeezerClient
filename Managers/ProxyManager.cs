using BoosterClient.Exceptions;
using BoosterClient.Models;
using System.Threading.Tasks;

namespace BoosterClient.Managers
{
    public class ProxyManager
    {
        private readonly APIClient client;

        public ProxyManager(APIClient client)
        {
            this.client = client;
        }

        public async Task<Proxy> GetAsync(int proxy_id)
        {
            return await client.Proxy.GET(proxy_id) ?? throw new ProxyNotFoundException();
        }
    }
}
