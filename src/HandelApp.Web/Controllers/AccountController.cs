using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HandelApp.Web.Controllers;

[AllowAnonymous]
public class AccountController(IConfiguration configuration, IWebHostEnvironment env) : Controller
{
    private static readonly PasswordHasher<string> _hasher = new();

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var users = configuration.GetSection("Auth:Users").Get<AuthUser[]>() ?? [];

        var user = users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        var valid = user is not null &&
            _hasher.VerifyHashedPassword(user.Username, user.PasswordHash, password)
                != PasswordVerificationResult.Failed;

        if (!valid)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user!.Username),
            new(ClaimTypes.Role, user.Role),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Apps");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View();

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var username = User.Identity!.Name!;
        var users    = configuration.GetSection("Auth:Users").Get<AuthUser[]>() ?? [];
        var user     = users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "User not found.");
            return View();
        }

        if (_hasher.VerifyHashedPassword(username, user.PasswordHash, currentPassword)
                == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Current password is incorrect.");
            return View();
        }

        if (newPassword != confirmPassword)
        {
            ModelState.AddModelError(string.Empty, "New password and confirmation do not match.");
            return View();
        }

        if (newPassword.Length < 6)
        {
            ModelState.AddModelError(string.Empty, "New password must be at least 6 characters.");
            return View();
        }

        var newHash = _hasher.HashPassword(username, newPassword);
        SavePasswordHash(username, newHash);

        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction(nameof(ChangePassword));
    }

    // Writes the new hash back to appsettings.json and reloads configuration.
    private void SavePasswordHash(string username, string newHash)
    {
        var path = Path.Combine(env.ContentRootPath, "appsettings.json");
        var json = JsonNode.Parse(System.IO.File.ReadAllText(path))!;

        var usersArray = json["Auth"]?["Users"]?.AsArray();
        if (usersArray is null) return;

        foreach (var node in usersArray)
        {
            if (node is null) continue;
            var uname = node["Username"]?.GetValue<string>();
            if (string.Equals(uname, username, StringComparison.OrdinalIgnoreCase))
            {
                node["PasswordHash"] = newHash;
                break;
            }
        }

        System.IO.File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Reload so the new hash is active without restarting the app.
        ((IConfigurationRoot)configuration).Reload();
    }

    private sealed record AuthUser(string Username, string PasswordHash, string Role);
}
