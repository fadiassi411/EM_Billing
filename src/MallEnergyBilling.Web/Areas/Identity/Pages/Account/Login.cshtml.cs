using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace MallEnergyBilling.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string ReturnUrl { get; private set; } = "/";

    public void OnGet(string? returnUrl = null) => ReturnUrl = LocalReturnUrl(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = LocalReturnUrl(returnUrl);
        if (!ModelState.IsValid) return Page();

        var username = Input.Username.Trim();
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            ModelState.AddModelError("", "The username or password is incorrect.");
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "This account is temporarily locked. Try again later.");
            return Page();
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "The username or password is incorrect.");
            return Page();
        }

        return LocalRedirect(ReturnUrl);
    }

    private string LocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    public sealed class InputModel
    {
        [Required] public string Username { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }
}
