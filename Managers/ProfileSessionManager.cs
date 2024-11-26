using BoosterClient.Exceptions;
using BoosterClient.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemuDeezerClient.Log;

namespace BoosterClient.Managers
{
    public class ProfileSessionManager
    {
        private readonly APIClient client;
        private readonly ConcurrentDictionary<string, ProfileSession> sessions;
        private readonly Timer extend_sessions_timer;

        public ProfileSessionManager(APIClient client)
        {
            this.client = client;
            sessions = new ConcurrentDictionary<string, ProfileSession>();

            // KHỞI CHẠY BỘ HẸN GIỜ.
            extend_sessions_timer = new Timer(OnExtendSessions, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
        }

        /// <summary>
        /// Hàm được gọi một lần mỗi 3 phút, có công việc gia hạn các phiên.
        /// </summary>
        private void OnExtendSessions(object state)
        {
            var session_ids = sessions.Keys.ToArray();
            try
            {
                client.Profile.PUT_SessionExtend(session_ids);
            }
            catch { }
        }

        public async Task<ProfileSession> OpenAsync()
        {
            var session = await client.Profile.GET_SessionOpen() ?? throw new ProfileQueueOverException();

            sessions.TryAdd(session.session_id, session);

            return session;
        }

        public Task FreezeAsync(string session_id)
        {
            // # DEBUG
            Logger.Create<ProfileSessionManager>()
                .AppendLine("DEBUG", $"Freeze session {session_id}")
                .Commit();

            if (sessions.TryRemove(session_id, out var _))
            {
                return client.Profile.PUT_SessionFreeze(session_id);
            }
            return Task.CompletedTask;
        }

        public async Task<bool> TryFreezeAsync(string session_id)
        {
            try
            {
                await client.Profile.PUT_SessionFreeze(session_id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task CloseAsync(string session_id)
        {
            if (sessions.TryRemove(session_id, out var _))
            {
                return client.Profile.DELETE_SessionClose(session_id);
            }
            return Task.CompletedTask;
        }
    }
}
