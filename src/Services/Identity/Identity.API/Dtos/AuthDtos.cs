namespace Identity.API.Dtos;
public record RegisterRequest(string Email, string Password, string FullName, string PhoneNumber);
public record LoginRequest(string EmailOrPhone, string Password);
public record AuthResponse(string Token, string RefreshToken, string FullName, string Email, string Role);
public record RefreshTokenRequest(string RefreshToken);