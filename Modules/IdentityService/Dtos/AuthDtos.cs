namespace Shopping_web.Modules.IdentityService.Dtos;
public record RegisterRequest(string Email, string Password, string FullName, string PhoneNumber); // Request DTO for user registration
public record LoginRequest(string EmailOrPhone, string Password); // Request DTO for user login, allowing either email or phone number for authentication
public record AuthResponse(string Token, string FullName, string Email, string Role); // Response DTO for authentication responses