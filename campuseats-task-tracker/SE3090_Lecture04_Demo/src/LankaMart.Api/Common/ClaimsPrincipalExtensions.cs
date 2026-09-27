using System.Security.Claims;

namespace LankaMart.Api.Common;

// ============================================================================
//  SLIDE 31 step 6 / SLIDE 36 - reading the identity the JWT middleware
//  attached to the request.
//
//  WHY THIS FILE EXISTS AT ALL - a real trap worth 20 minutes of a student's
//  life. Historically ASP.NET Core "maps" short JWT claim names onto long
//  WS-Federation URIs, so the "sub" claim you signed arrives as
//  ClaimTypes.NameIdentifier ("http://schemas.xmlsoap.org/ws/2005/05/
//  identity/claims/nameidentifier") and User.FindFirst("sub") returns null.
//
//  This project turns that mapping OFF (MapInboundClaims = false in
//  Program.cs) so a decoded token on jwt.io looks exactly like SLIDE 30.
//  The helpers below read the short name FIRST and fall back to the mapped
//  URI, so they work either way - which is what you want in your own project
//  when you cannot remember which behaviour is switched on.
// ============================================================================
public static class ClaimsPrincipalExtensions
{
    public const string SubClaim        = "sub";
    public const string RoleClaim       = "role";
    public const string PermissionClaim = "perm";

    /// The authenticated user's id, or null when the request is anonymous.
    public static long? GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(SubClaim)?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return long.TryParse(value, out var id) ? id : null;
    }

    /// The same, but throws when absent. Use inside [Authorize]d code where an
    /// anonymous caller is impossible - it fails loudly rather than silently
    /// treating "no identity" as user 0.
    public static long RequireUserId(this ClaimsPrincipal user)
        => user.GetUserId()
           ?? throw new UnauthorizedException("The request carries no authenticated user id.");

    public static string? GetEmail(this ClaimsPrincipal user)
        => user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;

    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal user)
        => user.FindAll(RoleClaim).Select(c => c.Value)
               .Concat(user.FindAll(ClaimTypes.Role).Select(c => c.Value))
               .Distinct()
               .ToList();

    public static bool IsStaffOrAdmin(this ClaimsPrincipal user)
        => user.IsInRole(Roles.Staff) || user.IsInRole(Roles.Admin);

    public static IReadOnlyList<string> GetPermissions(this ClaimsPrincipal user)
        => user.FindAll(PermissionClaim).Select(c => c.Value).ToList();
}

/// SLIDE 36 - the three roles, as constants. Typing "Admin" as a string
/// literal in fifteen controllers is how you end up with one "admin" that
/// silently matches nothing.
public static class Roles
{
    public const string Admin    = "Admin";
    public const string Staff    = "Staff";
    public const string Customer = "Customer";

    public const string StaffOrAdmin = Staff + "," + Admin;
}

/// SLIDE 36 / 38 - permission codes, the finer-grained alternative to roles.
public static class Permissions
{
    public const string ProductsCreate = "products.create";
    public const string ProductsUpdate = "products.update";
    public const string ProductsDelete = "products.delete";
    public const string OrdersViewAll  = "orders.view_all";
    public const string OrdersRefund   = "orders.refund";
    public const string UsersManage    = "users.manage";
}

/// Named authorization policies (SLIDE 38, bottom note). A policy decouples
/// the endpoint from role names: "CanRefundOrders" can be re-mapped to
/// different roles later without editing a single controller.
public static class Policies
{
    public const string CanRefundOrders = "CanRefundOrders";
    public const string CanViewAllOrders = "CanViewAllOrders";
    public const string CanManageUsers  = "CanManageUsers";
}
