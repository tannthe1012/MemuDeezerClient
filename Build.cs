using System.IO;
using System.Reflection;

namespace MemuDeezerClient
{
    internal class Build
    {
        public static readonly string PRODUCT_NAME;

        public static readonly string UPDATE_URL;

        public static readonly string ASSEMBLY_NAME;
        public static readonly string ASSEMBLY_FILE;
        public static readonly int ASSEMBLY_MAJOR_VERSION;
        public static readonly int ASSEMBLY_MINOR_VERSION;

        public static readonly string BASE_DIR;
        public static readonly string DATA_DIR;

        public static readonly string LOCK_FILE;
        public static readonly string VANGUARD_FILE;
        public static readonly string UPDATE_FILE;
        public static readonly bool IS_LITE;

        static Build()
        {
            PRODUCT_NAME = "Deezer Mobile Client";

            UPDATE_URL = "http://103.141.136.84:3030/update/deezermobile";

            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetName();
            var version = name.Version;

            ASSEMBLY_NAME = name.Name;
            ASSEMBLY_FILE = assembly.Location;
            ASSEMBLY_MAJOR_VERSION = version.Major;
            ASSEMBLY_MINOR_VERSION = version.Minor;

            BASE_DIR = Path.GetDirectoryName(ASSEMBLY_FILE);
            DATA_DIR = Path.Combine(BASE_DIR, "data");

            LOCK_FILE = Path.Combine(BASE_DIR, string.Concat(ASSEMBLY_NAME, ".lock"));
            VANGUARD_FILE = Path.Combine(BASE_DIR, "MemuDeezerVanguard.exe");
            UPDATE_FILE = Path.Combine(BASE_DIR, "MemuDeezerUpdate.exe");

            Directory.CreateDirectory(DATA_DIR);
            IS_LITE = true;



        }
    }
}
