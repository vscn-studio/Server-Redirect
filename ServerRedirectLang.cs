using Vintagestory.API.Config;

namespace ServerRedirect;

internal static class ServerRedirectLang
{
    private const string Prefix = "serverredirect:";

    public static string Get(string key, params object[] args)
    {
        return Lang.Get(Prefix + key, args);
    }

    public static string GetFor(string languageCode, string key, params object[] args)
    {
        return Lang.GetL(languageCode, Prefix + key, args);
    }
}
