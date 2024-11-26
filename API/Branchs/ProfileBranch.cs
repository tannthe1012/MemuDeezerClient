using BoosterClient.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoosterClient.Branchs
{
    public class ProfileBranch : Branch
    {
        public ProfileBranch(APIClient client) : base(client) { }

        public Task<Profile> GET(int profile_id) =>
            Client.RequestAsync<Profile>(HttpMethod.Get, $"api/profile/{profile_id}");

        public Task PUT(int profile_id, string country, DateTime? subs_start_date, DateTime? subs_end_date, ProfileType? type, ProfileStatus? status) =>
            Client.RequestAsync(HttpMethod.Put, $"api/profile/{profile_id}", new { country, subs_start_date, subs_end_date, type, status });

        public Task PUT_Rotate_Proxy(int profile_id) =>
            Client.RequestAsync(HttpMethod.Put, $"api/profile/proxy/{profile_id}/rotate");

        public Task<Stream> GET_Payload(int profile_id) =>
            Client.DownloadAsync(HttpMethod.Get, $"api/profile/{profile_id}/payload");

        public Task PUT_Payload(int profile_id, Stream stream) =>
            Client.UploadAsync(HttpMethod.Put, $"api/profile/{profile_id}/payload", stream);

        public Task<Stream> GET_Fingerprint(int profile_id) =>
            Client.DownloadAsync(HttpMethod.Get, $"api/profile/{profile_id}/fingerprint");

        public Task<ProfileSession> GET_SessionOpen() =>
            Client.RequestAsync<ProfileSession>(HttpMethod.Get, "api/profile/session/open");

        public Task PUT_SessionExtend(string[] session_ids) =>
            Client.RequestAsync(HttpMethod.Put, "api/profile/session/extend", session_ids);

        public Task PUT_SessionExtend(string session_id) =>
            Client.RequestAsync(HttpMethod.Put, $"api/profile/session/{session_id}/extend");

        public Task PUT_SessionFreeze(string session_id) =>
            Client.RequestAsync(HttpMethod.Put, $"api/profile/session/{session_id}/freeze");

        public Task DELETE_SessionClose(string session_id) =>
            Client.RequestAsync(HttpMethod.Delete, $"api/profile/session/{session_id}/close");
    }
}
