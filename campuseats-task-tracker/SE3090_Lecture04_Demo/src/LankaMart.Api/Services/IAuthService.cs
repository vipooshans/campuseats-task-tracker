using LankaMart.Api.Dtos;

namespace LankaMart.Api.Services;

/// SLIDE 31 - the whole authentication flow, behind one interface.
/// Note the absence of HttpContext, cookies and status codes: this service can
/// be unit-tested with fakes and no web server (see LankaMart.ServiceTests).
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}
