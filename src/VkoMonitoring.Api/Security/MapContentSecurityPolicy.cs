namespace VkoMonitoring.Api.Security;

public static class MapContentSecurityPolicy
{
    public static string Create(string tileUrl)
    {
        if (!Uri.TryCreate(tileUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Map tile URL must be HTTPS without credentials.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return $"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: {origin}; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }
}
