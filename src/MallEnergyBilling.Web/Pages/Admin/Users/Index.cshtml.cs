using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace MallEnergyBilling.Web.Pages.Admin.Users;

public sealed class IndexModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    ApplicationDbContext db) : PageModel
{
    public static readonly string[] AccessLevels = ["Administrator", "BillingManager", "Operator", "Viewer"];
    public List<UserAccessRow> Users { get; private set; } = [];

    [BindProperty] public string? UserId { get; set; }
    [BindProperty] public string? AccessLevel { get; set; }
    [BindProperty] public CreateUserInput NewUser { get; set; } = new();

    public async Task OnGetAsync() => await LoadUsersAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!AccessLevels.Contains(NewUser.AccessLevel, StringComparer.Ordinal))
            ModelState.AddModelError("NewUser.AccessLevel", "Select a valid access level.");
        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        var username = NewUser.Username.Trim();
        var email = string.IsNullOrWhiteSpace(NewUser.Email) ? null : NewUser.Email.Trim();
        if (await userManager.FindByNameAsync(username) is not null)
        {
            ModelState.AddModelError("NewUser.Username", "This username is already in use.");
            await LoadUsersAsync();
            return Page();
        }
        if (email is not null && await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError("NewUser.Email", "This contact email is already assigned to another user.");
            await LoadUsersAsync();
            return Page();
        }

        var newUser = new IdentityUser { UserName = username, Email = email, EmailConfirmed = email is not null };
        var createResult = await userManager.CreateAsync(newUser, NewUser.Password);
        if (!createResult.Succeeded) return await IdentityErrorAsync(createResult);

        var roleResult = await userManager.AddToRoleAsync(newUser, NewUser.AccessLevel);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(newUser);
            return await IdentityErrorAsync(roleResult);
        }

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserId = User.Identity?.Name ?? "Administrator",
            Action = "User account created",
            EntityType = "IdentityUser",
            EntityId = newUser.Id,
            NewValue = $"username {username}; email {email ?? "Not provided"}; access {NewUser.AccessLevel}",
            Reason = "Administrator created user account",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await db.SaveChangesAsync();

        TempData["Success"] = $"User {username} was created with {NewUser.AccessLevel} access.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        var user = string.IsNullOrWhiteSpace(UserId) ? null : await userManager.FindByIdAsync(UserId);
        if (user is null) ModelState.AddModelError("", "The selected user was not found.");
        if (AccessLevel is null || !AccessLevels.Contains(AccessLevel, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(AccessLevel), "Select a valid access level.");

        if (!ModelState.IsValid || user is null)
        {
            await LoadUsersAsync();
            return Page();
        }
        var selectedAccessLevel = AccessLevel!;

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentAccessRoles = currentRoles.Where(AccessLevels.Contains).ToArray();
        if (currentAccessRoles.Contains("Administrator") && selectedAccessLevel != "Administrator")
        {
            var administrators = await userManager.GetUsersInRoleAsync("Administrator");
            if (administrators.Count <= 1)
            {
                ModelState.AddModelError("", "The last Administrator cannot be changed to another access level.");
                await LoadUsersAsync();
                return Page();
            }
        }

        var rolesToRemove = currentAccessRoles.Where(role => role != selectedAccessLevel).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded) return await IdentityErrorAsync(removeResult);
        }

        if (!currentAccessRoles.Contains(selectedAccessLevel))
        {
            var addResult = await userManager.AddToRoleAsync(user, selectedAccessLevel);
            if (!addResult.Succeeded) return await IdentityErrorAsync(addResult);
        }

        await userManager.UpdateSecurityStampAsync(user);
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserId = User.Identity?.Name ?? "Administrator",
            Action = "User access level changed",
            EntityType = "IdentityUser",
            EntityId = user.Id,
            OldValue = currentAccessRoles.Length == 0 ? "Unassigned" : string.Join(", ", currentAccessRoles),
            NewValue = selectedAccessLevel,
            Reason = "Administrator updated user access",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await db.SaveChangesAsync();

        var signedInUser = await userManager.GetUserAsync(User);
        if (signedInUser?.Id == user.Id) await signInManager.RefreshSignInAsync(user);

        TempData["Success"] = $"Access level for {user.Email ?? user.UserName} is now {selectedAccessLevel}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        var user = string.IsNullOrWhiteSpace(userId) ? null : await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["Error"] = "The selected user was not found.";
            return RedirectToPage();
        }

        var signedInUser = await userManager.GetUserAsync(User);
        if (signedInUser?.Id == user.Id)
        {
            TempData["Error"] = "You cannot delete the account currently signed in.";
            return RedirectToPage();
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains("Administrator"))
        {
            var administrators = await userManager.GetUsersInRoleAsync("Administrator");
            if (administrators.Count <= 1)
            {
                TempData["Error"] = "The last Administrator cannot be deleted.";
                return RedirectToPage();
            }
        }

        var email = user.Email ?? user.UserName ?? "Unknown user";
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToPage();
        }

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserId = User.Identity?.Name ?? "Administrator",
            Action = "User account deleted",
            EntityType = "IdentityUser",
            EntityId = user.Id,
            OldValue = $"{email}; access {(roles.Count == 0 ? "Unassigned" : string.Join(", ", roles))}",
            Reason = "Administrator confirmed user deletion",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await db.SaveChangesAsync();

        TempData["Success"] = $"User {email} was deleted.";
        return RedirectToPage();
    }

    private async Task<IActionResult> IdentityErrorAsync(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        await LoadUsersAsync();
        return Page();
    }

    private async Task LoadUsersAsync()
    {
        Users = [];
        var users = await userManager.Users.OrderBy(user => user.Email ?? user.UserName).ToListAsync();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            Users.Add(new UserAccessRow(
                user.Id,
                user.UserName ?? "Unknown user",
                user.Email ?? "Not provided",
                roles.FirstOrDefault(AccessLevels.Contains) ?? "Unassigned"));
        }
    }

    public sealed record UserAccessRow(string Id, string Username, string Email, string AccessLevel);

    public sealed class CreateUserInput
    {
        [Required, StringLength(50, MinimumLength = 3), RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Username may contain letters, numbers, dots, underscores and hyphens only.")] public string Username { get; set; } = "";
        [EmailAddress, StringLength(256)] public string? Email { get; set; }
        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 10)] public string Password { get; set; } = "";
        [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
        [Required] public string AccessLevel { get; set; } = "Viewer";
    }
}
