namespace StacksAtlas.API.Models;

public record LoginRequest(string Username, string Password);
public record SetupRequest(string Username, string Password);
public record AuthStatusResponse(bool IsConfigured);
