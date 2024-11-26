using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoosterClient.Branchs
{
    public class SettingBranch : Branch
    {
        public SettingBranch(APIClient client) : base(client) { }

        public Task<Dictionary<string, string>> GET() =>
            Client.RequestAsync<Dictionary<string, string>>(HttpMethod.Get, "api/setting");

        public Task<string> GET(string key) =>
            Client.RequestAsync<string>(HttpMethod.Get, $"api/setting/{key}");
    }
}
