using System;
using System.Collections.Generic;
using System.Text;
using MurmurHash.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public static class JObjectExtensions
{
    public static uint CalculateMurmurHash32Bits(this JObject @this, JsonSerializerSettings settings)
    {
        var asString = JsonConvert.SerializeObject(@this, Formatting.None, settings);
        var asBytes = Encoding.UTF8.GetBytes(asString);
        return MurmurHash3.Hash32(asBytes, 42U);
    }
}
