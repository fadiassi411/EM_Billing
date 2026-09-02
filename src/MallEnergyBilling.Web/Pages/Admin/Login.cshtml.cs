using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace MallEnergyBilling.Web.Pages.Admin;

[AllowAnonymous]
public sealed class LoginModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.IsInRole("Administrator")) return RedirectToPage("/Admin/Users/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var username = Input.Username.Trim();
        var user = await userManager.FindByNameAsync(username);
        if (user is null || !await userManager.IsInRoleAsync(user, "Administrator"))
        {
            ModelState.AddModelError("", "The Administrator username or password is incorrect.");
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(user, Input.Password, false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "This Administrator account is temporarily locked. Try again later.");
            return Page();
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "The Administrator username or password is incorrect.");
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)) return LocalRedirect(ReturnUrl);
        return RedirectToPage("/Admin/Users/Index");
    }

    public sealed class InputModel
    {
        [Required] public string Username { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    }
}
