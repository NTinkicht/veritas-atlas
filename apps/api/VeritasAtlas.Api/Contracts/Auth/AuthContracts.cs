namespace VeritasAtlas.Api.Contracts.Auth;

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string Username, string Role);
public sealed record CurrentUserResponse(string Username, string Role, bool IsAuthenticated, DateTime TimestampUtc);
public sealed record AuthErrorResponse(string Code, string Message, DateTime TimestampUtc);