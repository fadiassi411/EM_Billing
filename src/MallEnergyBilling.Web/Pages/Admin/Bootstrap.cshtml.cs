using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MallEnergyBilling.Web.Pages.Admin;

[Authorize]
public sealed class BootstrapModel(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : PageModel
{
    public bool Available { get; private set; }
    public async Task OnGet() => Available = !(await users.GetUsersInRoleAsync("Administrator")).Any();
    public async Task<IActionResult> OnPostAsync()
    {
        if ((await users.GetUsersInRoleAsync("Administrator")).Any()) return Forbid();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await users.AddToRoleAsync(user, "Administrator");
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); return Page(); }
        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        return RedirectToPage("/Admin/Controllers/Index");
    }
}
