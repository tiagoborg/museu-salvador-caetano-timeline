using Newtonsoft.Json.Linq;

public static class JsonExtensions
{
    public static bool IsNullOrEmpty(this JToken token)
    {
        return token is null ||
               (token.Type == JTokenType.Array || token.Type == JTokenType.Object) && !token.HasValues ||
               (token.Type == JTokenType.String && token.ToString() == string.Empty) ||
               token.Type == JTokenType.Null;
    }
}

