namespace Shopping_web.Modules.IdentityService.Dtos;
public record RegisterRequest(string Email, string Password, string FullName, string PhoneNumber);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, string FullName, string Role);