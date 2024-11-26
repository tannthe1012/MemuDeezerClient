using Newtonsoft.Json;
using System.IO;

namespace MemuDeezerClient
{
    internal class Config
    {
        private static readonly string CONFIG_FILE = Path.Combine(Build.BASE_DIR, "config.json");

        public static readonly Config Instance;

        [JsonProperty("debug_mode")]
        public bool DebugMode { get; set; } = false;

        [JsonProperty("thread_count")]
        public int ThreadCount { get; set; } = 1;

        [JsonProperty("memu_directory")]
        public string MEmuDirectory { get; set; } = "D:\\Program Files\\Microvirt\\MEmu";

        static Config()
        {
            Config cfg = null;
            try
            {
                var json = File.ReadAllText(CONFIG_FILE);
                cfg = JsonConvert.DeserializeObject<Config>(json);
            }
            catch { }

            if (cfg == null)
            {
                cfg = new Config();
                cfg.Save();
            }

            Instance = cfg;
        }

        protected Config() { }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(CONFIG_FILE, json);
            }
            catch { }
        }
    }
}
