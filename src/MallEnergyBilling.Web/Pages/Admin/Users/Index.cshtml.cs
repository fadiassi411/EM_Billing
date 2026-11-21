using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Models;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MallEnergyBilling.Web.Pages.Admin.Users;

public sealed class IndexModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    PermissionService permissions,
    ApplicationDbContext db) : PageModel
{
    public List<UserAccessRow> Users { get; private set; } = [];
    /// <summary>All user levels: built-in levels first, then levels created by an Administrator.</summary>
    public List<string> Levels { get; private set; } = [];
    public Dictionary<string, IReadOnlySet<string>> LevelPermissions { get; private set; } = [];
    public Dictionary<string, int> LevelUserCounts { get; private set; } = [];

    [BindProperty] public string? UserId { get; set; }
    [BindProperty] public string? AccessLevel { get; set; }
    [BindProperty] public CreateUserInput NewUser { get; set; } = new();

    public static bool IsBuiltIn(string level) => Permissions.BuiltInRoles.Contains(level, StringComparer.OrdinalIgnoreCase);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadLevelsAsync();
        if (!Levels.Contains(NewUser.AccessLevel, StringComparer.Ordinal))
            ModelState.AddModelError("NewUser.AccessLevel", "Select a valid access level.");
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var username = NewUser.Username.Trim();
        var email = string.IsNullOrWhiteSpace(NewUser.Email) ? null : NewUser.Email.Trim();
        if (await userManager.FindByNameAsync(username) is not null)
        {
            ModelState.AddModelError("NewUser.Username", "This username is already in use.");
            await LoadAsync();
            return Page();
        }
        if (email is not null && await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError("NewUser.Email", "This contact email is already assigned to another user.");
            await LoadAsync();
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

        Audit("User account created", "IdentityUser", newUser.Id, "", $"username {username}; email {email ?? "Not provided"}; access {NewUser.AccessLevel}", "Administrator created user account");
        await db.SaveChangesAsync();
        TempData["Success"] = $"User {username} was created with {Permissions.DisplayName(NewUser.AccessLevel)} access.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        await LoadLevelsAsync();
        var user = string.IsNullOrWhiteSpace(UserId) ? null : await userManager.FindByIdAsync(UserId);
        if (user is null) ModelState.AddModelError("", "The selected user was not found.");
        if (AccessLevel is null || !Levels.Contains(AccessLevel, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(AccessLevel), "Select a valid access level.");

        if (!ModelState.IsValid || user is null)
        {
            await LoadAsync();
            return Page();
        }
        var selectedAccessLevel = AccessLevel!;

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentAccessRoles = currentRoles.Where(Levels.Contains).ToArray();
        if (currentAccessRoles.Contains(Permissions.AdministratorRole) && selectedAccessLevel != Permissions.AdministratorRole)
        {
            var administrators = await userManager.GetUsersInRoleAsync(Permissions.AdministratorRole);
            if (administrators.Count <= 1)
            {
                ModelState.AddModelError("", "The last Administrator cannot be changed to another access level.");
                await LoadAsync();
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
        Audit("User access level changed", "IdentityUser", user.Id, currentAccessRoles.Length == 0 ? "Unassigned" : string.Join(", ", currentAccessRoles), selectedAccessLevel, "Administrator updated user access");
        await db.SaveChangesAsync();

        var signedInUser = await userManager.GetUserAsync(User);
        if (signedInUser?.Id == user.Id) await signInManager.RefreshSignInAsync(user);

        TempData["Success"] = $"Access level for {user.UserName} is now {Permissions.DisplayName(selectedAccessLevel)}.";
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
        if (roles.Contains(Permissions.AdministratorRole))
        {
            var administrators = await userManager.GetUsersInRoleAsync(Permissions.AdministratorRole);
            if (administrators.Count <= 1)
            {
                TempData["Error"] = "The last Administrator cannot be deleted.";
                return RedirectToPage();
            }
        }

        var name = user.UserName ?? user.Email ?? "Unknown user";
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToPage();
        }

        Audit("User account deleted", "IdentityUser", user.Id, $"{name}; access {(roles.Count == 0 ? "Unassigned" : string.Join(", ", roles))}", "", "Administrator confirmed user deletion");
        await db.SaveChangesAsync();
        TempData["Success"] = $"User {name} was deleted.";
        return RedirectToPage();
    }

    /// <summary>Saves the ticked boxes of every level (except Administrator, which always has everything).</summary>
    /// <param name="grants">Ticked boxes, each "level|permission".</param>
    public async Task<IActionResult> OnPostSavePermissionsAsync(string[]? grants)
    {
        await LoadLevelsAsync();
        var ticked = (grants ?? []).Select(x => x.Split('|', 2)).Where(x => x.Length == 2).ToLookup(x => x[0], x => x[1]);
        var changes = new List<string>();
        foreach (var level in Levels.Where(x => x != Permissions.AdministratorRole))
        {
            var before = await permissions.ForRoleAsync(level);
            await permissions.SetAsync(roleManager, level, ticked[level]);
            var after = await permissions.ForRoleAsync(level);
            if (before.SetEquals(after)) continue;
            var added = after.Except(before).Select(Label).ToList();
            var removed = before.Except(after).Select(Label).ToList();
            changes.Add(Permissions.DisplayName(level));
            Audit("User level permissions changed", "UserLevel", level,
                removed.Count == 0 ? "" : "Removed: " + string.Join(", ", removed),
                added.Count == 0 ? "" : "Added: " + string.Join(", ", added), "Administrator updated user level permissions");
        }
        await db.SaveChangesAsync();
        TempData["Success"] = changes.Count == 0 ? "No permissions were changed." : $"Permissions saved for {string.Join(", ", changes)}. They apply on the users' next click.";
        return RedirectToPage(null, null, "levels");
    }

    public async Task<IActionResult> OnPostCreateLevelAsync(string? levelName, string? copyFrom)
    {
        await LoadLevelsAsync();
        var name = Regex.Replace(levelName ?? "", @"\s+", " ").Trim();
        if (name.Length is < 3 or > 40 || !Regex.IsMatch(name, @"^[\p{L}\p{N} ._-]+$"))
            return LevelError("Enter a level name of 3 to 40 letters, numbers, spaces, dots, dashes or underscores.");
        if (Levels.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase) || string.Equals(Permissions.DisplayName(x), name, StringComparison.OrdinalIgnoreCase)))
            return LevelError($"A level called {name} already exists.");
        var result = await roleManager.CreateAsync(new IdentityRole(name));
        if (!result.Succeeded) return LevelError(string.Join(" ", result.Errors.Select(e => e.Description)));
        var start = !string.IsNullOrWhiteSpace(copyFrom) && Levels.Contains(copyFrom) ? await permissions.ForRoleAsync(copyFrom) : new HashSet<string>();
        await permissions.SetAsync(roleManager, name, start);
        Audit("User level created", "UserLevel", name, "", start.Count == 0 ? "No permissions" : "Copied from " + Permissions.DisplayName(copyFrom!), "Administrator created a user level");
        await db.SaveChangesAsync();
        TempData["Success"] = $"Level {name} was created. Tick its permissions below, then save.";
        return RedirectToPage(null, null, "levels");
    }

    public async Task<IActionResult> OnPostDeleteLevelAsync(string? levelName)
    {
        await LoadLevelsAsync();
        if (string.IsNullOrWhiteSpace(levelName) || !Levels.Contains(levelName)) return LevelError("That level was not found.");
        if (IsBuiltIn(levelName)) return LevelError("Built-in levels cannot be deleted. Untick their permissions instead.");
        var members = await userManager.GetUsersInRoleAsync(levelName);
        if (members.Count > 0) return LevelError($"{levelName} still has {members.Count} user(s). Move them to another level first.");
        var role = await roleManager.FindByNameAsync(levelName);
        if (role is not null) await roleManager.DeleteAsync(role);
        permissions.Invalidate();
        Audit("User level deleted", "UserLevel", levelName, levelName, "", "Administrator deleted a user level");
        await db.SaveChangesAsync();
        TempData["Success"] = $"Level {levelName} was deleted.";
        return RedirectToPage(null, null, "levels");
    }

    static string Label(string key) => Permissions.All.FirstOrDefault(x => x.Key == key)?.Name ?? key;

    IActionResult LevelError(string message) { TempData["Error"] = message; return RedirectToPage(null, null, "levels"); }

    void Audit(string action, string entityType, string entityId, string oldValue, string newValue, string reason) => db.AuditLogs.Add(new AuditLog
    {
        Timestamp = DateTimeOffset.UtcNow, UserId = User.Identity?.Name ?? "Administrator", Action = action, EntityType = entityType, EntityId = entityId,
        OldValue = oldValue, NewValue = newValue, Reason = reason, SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
    });

    private async Task<IActionResult> IdentityErrorAsync(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        await LoadAsync();
        return Page();
    }

    private async Task LoadLevelsAsync()
    {
        var names = await roleManager.Roles.Select(x => x.Name!).ToListAsync();
        Levels = Permissions.BuiltInRoles.Where(names.Contains)
            .Concat(names.Where(x => !IsBuiltIn(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadAsync()
    {
        await LoadLevelsAsync();
        Users = [];
        LevelUserCounts = Levels.ToDictionary(x => x, _ => 0);
        LevelPermissions = [];
        foreach (var level in Levels) LevelPermissions[level] = await permissions.ForRoleAsync(level);
        var users = await userManager.Users.OrderBy(user => user.UserName).ToListAsync();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var level = roles.FirstOrDefault(Levels.Contains);
            if (level is not null) LevelUserCounts[level]++;
            Users.Add(new UserAccessRow(user.Id, user.UserName ?? "Unknown user", user.Email ?? "Not provided", level ?? "Unassigned"));
        }
    }

    public sealed record UserAccessRow(string Id, string Username, string Email, string AccessLevel);

    public sealed class CreateUserInput
    {
        [Required, StringLength(50, MinimumLength = 3), RegularExpression("^[A-Za-z0-9._@-]+$", ErrorMessage = "Username may contain letters, numbers, dots, underscores, @ and hyphens only.")] public string Username { get; set; } = "";
        [EmailAddress, StringLength(256)] public string? Email { get; set; }
        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 10)] public string Password { get; set; } = "";
        [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
        [Required] public string AccessLevel { get; set; } = "Viewer";
    }
}
