using BoosterClient.Exceptions;
using BoosterClient.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using MemuDeezerClient.Log;

namespace BoosterClient.Managers
{
    public class ProfileManager
    {
        private readonly APIClient client;

        public ProfileManager(APIClient client)
        {
            this.client = client;
        }

        public async Task<Profile> GetAsync(int profile_id)
        {
            return await client.Profile.GET(profile_id) ?? throw new ProfileNotFoundException();
        }

        public Task UpdateAsync(int profile_id, string country = null, DateTime? subs_start_date = null, DateTime? subs_end_date = null, ProfileType? type = null, ProfileStatus? status = null)
        {
            return client.Profile.PUT(profile_id, country, subs_start_date, subs_end_date, type, status);
        }

        public async Task<bool> TryUpdateAsync(int profile_id, string country = null, DateTime? subs_start_date = null, DateTime? subs_end_date = null, ProfileType? type = null, ProfileStatus? status = null)
        {
            try
            {
                await client.Profile.PUT(profile_id, country, subs_start_date, subs_end_date, type, status);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task RotateProxyAsync(int profile_id)
        {
            return client.Profile.PUT_Rotate_Proxy(profile_id);
        }

        public Task<Stream> ReadPayloadAsync(int profile_id)
        {
            return client.Profile.GET_Payload(profile_id);
        }

        public Task WritePayloadAsync(int profile_id, Stream stream)
        {
            return client.Profile.PUT_Payload(profile_id, stream);
        }

        public Task<Stream> ReadFingerprintAsync(int profile_id)
        {
            return client.Profile.GET_Fingerprint(profile_id);
        }
    }
}
