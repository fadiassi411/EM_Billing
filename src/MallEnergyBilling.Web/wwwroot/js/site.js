// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Show the Watchdog Energy Management startup identity once per browser tab.
document.addEventListener("DOMContentLoaded", () => {
    const splash = document.getElementById("startupSplash");
    if (!splash || document.documentElement.classList.contains("splash-seen")) return;

    window.setTimeout(() => {
        splash.classList.add("is-closing");
        try { sessionStorage.setItem("watchDogSplashSeen", "true"); } catch { }
        window.setTimeout(() => splash.remove(), 400);
    }, 5000);
});

// About Watchdog is available from the Main menu without leaving the current page.
document.addEventListener("DOMContentLoaded", () => {
    const dialog = document.getElementById("aboutWatchDog");
    if (!dialog) return;
    const closeDialog = () => typeof dialog.close === "function"
        ? dialog.close()
        : dialog.removeAttribute("open");

    document.querySelectorAll("[data-open-about]").forEach(button => {
        button.addEventListener("click", () => {
            button.closest("details")?.removeAttribute("open");
            if (typeof dialog.showModal === "function") dialog.showModal();
            else dialog.setAttribute("open", "");
        });
    });

    dialog.querySelectorAll("[data-close-about]").forEach(button =>
        button.addEventListener("click", closeDialog)
    );

    dialog.addEventListener("click", event => {
        if (event.target === dialog) closeDialog();
    });
});
