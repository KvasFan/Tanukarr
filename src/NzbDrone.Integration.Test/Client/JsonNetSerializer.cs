using NzbDrone.Common.Serializer;
using RestSharp.Serializers;

namespace NzbDrone.Integration.Test.Client
{
    // RestSharp 106 serializes request bodies with its bundled SimpleJson, which turns a
    // Newtonsoft JArray value (for example a non-empty category default) into nested arrays
    // and a Dictionary-derived resource property into a list of key/value pairs. Serialize
    // with the application's Newtonsoft settings instead so the client sends the same JSON
    // shapes the API itself produces.
    public class JsonNetSerializer : ISerializer
    {
        public string ContentType { get; set; } = "application/json";

        public string Serialize(object obj)
        {
            return obj.ToJson();
        }
    }
}
