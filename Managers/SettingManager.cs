using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BoosterClient.Managers
{
    public class SettingManager
    {
        public const string KEY_MAINTENANCE = "maintenance";
        public const string KEY_PLAY_MIN = "play_min";
        public const string KEY_PLAY_MAX = "play_max";

        public const bool DEF_MAINTENANCE = true;
        public const int DEF_PLAY_MIN = 60;
        public const int DEF_PLAY_MAX = 90;

        private class LazyValue<T>
        {
            private readonly APIClient client;
            private readonly string key;
            private readonly T def;
            private readonly TimeSpan life_time;

            private Stopwatch sw;
            private T value;

            public LazyValue(APIClient client, string key, T def, TimeSpan life_time)
            {
                this.client = client;
                this.key = key;
                this.def = def;
                this.life_time = life_time;

                this.sw = new Stopwatch();
                this.value = default;
            }

            public async Task<T> ValueAsync()
            {
                if (!sw.IsRunning || sw.Elapsed > life_time)
                {
                    try
                    {
                        var raw = await client.Setting.GET(key);
                        value = JsonConvert.DeserializeObject<T>(raw);
                    }
                    catch
                    {
                        value = def;
                    }

                    sw.Restart();
                }

                return value;
            }
        }

        private readonly APIClient client;
        private readonly LazyValue<bool> maintenance;
        private readonly LazyValue<int> play_min;
        private readonly LazyValue<int> play_max;

        public SettingManager(APIClient client)
        {
            this.client = client;

            maintenance = new LazyValue<bool>(client, KEY_MAINTENANCE, DEF_MAINTENANCE, TimeSpan.FromMinutes(3));
            play_min = new LazyValue<int>(client, KEY_PLAY_MIN, DEF_PLAY_MIN, TimeSpan.FromMinutes(3));
            play_max = new LazyValue<int>(client, KEY_PLAY_MAX, DEF_PLAY_MAX, TimeSpan.FromMinutes(3));
        }

        public Task<bool> GetMaintenanceAsync() =>
            maintenance.ValueAsync();

        public async Task<bool> TryGetMaintenanceAsync(bool def)
        {
            try
            {
                return await GetMaintenanceAsync();
            }
            catch
            {
                return def;
            }
        }

        public Task<int> GetPlayMinAsync() =>
            play_min.ValueAsync();

        public Task<int> GetPlayMaxAsync() =>
            play_max.ValueAsync();
    }
}
