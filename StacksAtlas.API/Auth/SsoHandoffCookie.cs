namespace StacksAtlas.API.Auth;

/// <summary>
/// Short-lived HttpOnly cookie used to pass the JWT after OIDC callback without exposing it in the URL.
/// </summary>
public static class SsoHandoffCookie
{
    public const string Name = "StacksAtlas.Sso.Handoff";
    public const int MaxAgeSeconds = 60;
}
