using BoosterClient.Models;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoosterClient.Branchs
{
    public class SourceBranch : Branch
    {
        public SourceBranch(APIClient client) : base(client) { }

        public Task<Source> GET(string source_id) =>
            Client.RequestAsync<Source>(HttpMethod.Get, $"api/source/{source_id}");

        public Task PUT_Report(string source_id, SourceReportType type) =>
            Client.RequestAsync(HttpMethod.Put, $"api/source/{source_id}/report", new { type });

        public Task<SourceURL> GET_PoolPick() =>
            Client.RequestAsync<SourceURL>(HttpMethod.Get, "api/source/pool/pick");

        public Task<int> GET_PoolCount() =>
            Client.RequestAsync<int>(HttpMethod.Get, "api/source/pool/count");
    }
}
