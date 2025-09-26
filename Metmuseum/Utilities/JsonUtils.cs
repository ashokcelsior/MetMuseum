using Newtonsoft.Json.Linq;

namespace Metmuseum.Utilities
{
    public static class JsonUtils
    {
        public static JObject CleanForStorage(JObject original, params string[] propertiesToRemove)
        {
            var clone = (JObject)original.DeepClone();
            foreach (var prop in propertiesToRemove)
                clone.Remove(prop);
            return clone;
        }
    }
}
