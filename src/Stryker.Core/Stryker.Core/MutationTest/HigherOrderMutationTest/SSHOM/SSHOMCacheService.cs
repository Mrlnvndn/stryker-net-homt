using System;
using System.IO;
using System.Text.Json;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.SSHOM
{
    public static class SSHOMCacheService
    {
        public static SSHOMCache Load(string path)
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SSHOMCache>(json);
        }

        public static void Save(string path, SSHOMCache cache)
        {
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
