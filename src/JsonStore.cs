// JSON 读写（DataContractJsonSerializer，.NET 4.0 内置，无第三方依赖）
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AutoDateShift
{
    internal static class JsonStore
    {
        public static T Read<T>(string file) where T : new()
        {
            if (!File.Exists(file)) return new T();
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0) return new T();
                return (T)ser.ReadObject(fs);
            }
        }

        /// <summary>写盘前先备份旧文件为 .bak</summary>
        public static void Write<T>(string file, T obj)
        {
            if (File.Exists(file))
                File.Copy(file, file + ".bak", true);
            var ser = new DataContractJsonSerializer(typeof(T));
            string tmp = file + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                ser.WriteObject(fs, obj);
            }
            File.Copy(tmp, file, true);
            File.Delete(tmp);
        }

        /// <summary>校验 JSON 是否可解析（供 GUI 保存前/Worker 读取失败判断）</summary>
        public static bool TryRead<T>(string file, out T obj) where T : new()
        {
            try { obj = Read<T>(file); return true; }
            catch { obj = new T(); return false; }
        }
    }
}
