using BoosterClient.Models;
using MemuDeezerClient;
using System.Threading.Tasks;

namespace BoosterClient.Managers
{
    public class SourceManager
    {
        private readonly APIClient client;

        public SourceManager(APIClient client)
        {
            this.client = client;
        }

        public Task<Source> FindAsync(string source_id)
        {
            return client.Source.GET(source_id);
        }

        public Task ReportAsync(string source_id, SourceReportType type)
        {
            if (Build.IS_LITE)
            {
                return null;
            }
            return client.Source.PUT_Report(source_id, type);
        }

        public async Task<bool> TryReportAsync(string source_id, SourceReportType type)
        {
            if (Build.IS_LITE)
            {
                return false;
            }

            try
            {
                await client.Source.PUT_Report(source_id, type);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
