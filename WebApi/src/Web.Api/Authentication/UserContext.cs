using Web.Api.Authentication;
using Microsoft.AspNetCore.Http;

namespace Web.Api.Authentication;

internal sealed class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId =>
        _httpContextAccessor
            .HttpContext?
            .User
            .GetUserId() ??
        throw new UserContextUnavailableException();

    public Guid? DeviceId => _httpContextAccessor.HttpContext?.User.GetDeviceId();
}
