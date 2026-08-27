// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Show the Watch Dog EM startup identity once per browser tab.
document.addEventListener("DOMContentLoaded", () => {
    const splash = document.getElementById("startupSplash");
    if (!splash || document.documentElement.classList.contains("splash-seen")) return;

    window.setTimeout(() => {
        splash.classList.add("is-closing");
        try { sessionStorage.setItem("watchDogSplashSeen", "true"); } catch { }
        window.setTimeout(() => splash.remove(), 400);
    }, 5000);
});
