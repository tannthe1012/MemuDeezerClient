using LDiumSharp.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MemuDeezerClient.Log
{
    internal class Logger
    {
        private static readonly Dictionary<string, Logger> loggers = new Dictionary<string, Logger>();

        private readonly string file;
        private readonly StringBuilder sb = new StringBuilder();

        protected Logger(string name)
        {
            var base_dir = AppDomain.CurrentDomain.BaseDirectory;
            var logs_dir = Path.Combine(base_dir, "logs");

            Directory.CreateDirectory(logs_dir);

            file = Path.Combine(logs_dir, name + ".log");
        }

        public static Logger Create(string name)
        {
            if (loggers.TryGetValue(name, out var logger))
            {
                return logger;
            }
            return loggers[name] = new Logger(name);
        }

        public static Logger Create<T>()
        {
            var type = typeof(T);
            var name = type.Name;
            return Create(name);
        }

        public Logger AppendLine(string tag, string text)
        {
            // [28/10/2015 08:23:36 PM] TAG: text
            sb.Append("[").Append(DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt"))
                .Append("] ").Append(tag).Append(": ").Append(text).AppendLine();
            return this;
        }

        public Logger AppendError(Exception ex)
        {
            sb.Append("-----------------------------------------------------------").AppendLine()
                .Append("[").Append(DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt"))
                .Append("] ERROR: ").Append(ex.GetType().FullName).AppendLine()
                .Append("Message        : ").Append(ex.Message).AppendLine()
                .Append("Source         : ").Append(ex.Source).AppendLine()
                .Append("TargetSite     : ").Append(ex.TargetSite).AppendLine()
                .Append("StackTrace     : ").AppendLine().Append(ex.StackTrace).AppendLine();

            var iex = ex.InnerException;
            if (iex != null)
            {
                if (iex is JavaException jex)
                {
                    sb.Append("IException     : ").AppendLine(jex.GetType().FullName)
                        .Append("IMessage       : ").Append(jex.Message).AppendLine()
                        .Append("IStackTrace    : ").AppendLine().Append(jex.StackTrace).AppendLine()
                        .Append("JException     : ").Append(jex.JavaType).AppendLine()
                        .Append("JStackTrace    : ").AppendLine().Append(jex.JavaTrace).AppendLine();
                }
                else
                {
                    sb.Append("IException     : ").AppendLine(iex.GetType().FullName)
                        .Append("IMessage       : ").Append(iex.Message).AppendLine()
                        .Append("ISource        : ").Append(iex.Source).AppendLine()
                        .Append("ITargetSite    : ").Append(iex.TargetSite).AppendLine()
                        .Append("IStackTrace    : ").AppendLine().Append(iex.StackTrace).AppendLine();
                }
            }

            sb.Append("-----------------------------------------------------------").AppendLine();

            return this;
        }

        public void Commit()
        {
            try
            {
                File.AppendAllText(file, sb.ToString());
                sb.Clear();
            }
            catch { }
        }
    }
}