using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Dtos;

/// Admin view of a user. No PasswordHash: hashes are not secret in the way
/// passwords are, but publishing them lets an attacker crack offline at leisure.
public record UserDto(
    long                  Id,
    string                Name,
    string                Email,
    bool                  IsActive,
    IReadOnlyList<string> Roles,
    DateTime              CreatedAt);

/// POST /api/v1/users/{id}/roles - SLIDE 37: granting a role is one INSERT
/// into the user_roles junction table.
public record AssignRoleDto(
    [Required, StringLength(40)] string Role);
