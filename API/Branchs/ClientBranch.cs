using BoosterClient.Models;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoosterClient.Branchs
{
    public class ClientBranch : Branch
    {
        public ClientBranch(APIClient client) : base(client) { }

        public Task GET_Ping() => Client.RequestAsync(HttpMethod.Get, "api/client/ping");

        public Task<Client> POST_Register(string name) =>
            Client.RequestAsync<Client>(HttpMethod.Post, "api/client/register", new { name });

        public Task PUT_Update(string name) =>
            Client.RequestAsync(HttpMethod.Put, "api/client/update", new { name });

        public Task PUT_Report(int sph, int thread_count) =>
            Client.RequestAsync(HttpMethod.Put, "api/client/report", new { sph, thread_count });
    }
}
