namespace StacksAtlas.Core.Services.Federation;

/// <summary>Shared secret header for Hub → Node wake-up pulses (not user JWT).</summary>
public static class FederationPulseAuth
{
    public const string HeaderName = "X-StacksAtlas-Federation-Token";
}
