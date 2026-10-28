// SPDX-License-Identifier: GPL-2.0-only

(() => {
    "use strict";

    const guardId = "kunini-auth-guard";
    const statusUrl = "/KuniniAuth/status";
    const startupTimeoutMs = 20000;
    let loginAttempted = false;
    let finished = false;

    const revealApplication = () => {
        document.getElementById(guardId)?.remove();
        finished = true;
    };

    const showFailure = () => {
        if (document.getElementById("kunini-auth-error")) {
            return;
        }
        const message = document.createElement("div");
        message.id = "kunini-auth-error";
        message.setAttribute("role", "alert");
        message.textContent = "Kunini sign-in could not start. Reload the page or contact the administrator.";
        message.style.cssText = "position:fixed;inset:0;display:grid;place-items:center;padding:2rem;background:#101010;color:#fff;font:18px system-ui;visibility:visible;z-index:2147483647;text-align:center";
        document.documentElement.append(message);
        finished = true;
    };

    const setInputValue = (input, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        setter?.call(input, value);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const attemptLogin = (loginUsername) => {
        if (finished) {
            return;
        }

        const usernameInput = document.getElementById("txtManualName");
        const passwordInput = document.getElementById("txtManualPassword");
        const submitButton = document.querySelector(".manualLoginForm button.button-submit");
        const form = document.querySelector(".manualLoginForm");

        if (usernameInput && passwordInput && submitButton && form && !form.classList.contains("hide")) {
            if (loginAttempted) {
                return;
            }
            loginAttempted = true;
            setInputValue(usernameInput, loginUsername);
            passwordInput.type = "text";
            setInputValue(passwordInput, crypto.randomUUID());
            submitButton.click();
            return;
        }

        const onLoginRoute = window.location.hash.toLocaleLowerCase().includes("login");
        const appRoot = document.getElementById("reactRoot");
        if (!onLoginRoute && appRoot?.childElementCount) {
            revealApplication();
        }
    };

    const start = async () => {
        try {
            const response = await fetch(statusUrl, {
                cache: "no-store",
                credentials: "same-origin",
                headers: { Accept: "application/json" }
            });
            if (!response.ok) {
                throw new Error(`Kunini status returned ${response.status}`);
            }

            const status = await response.json();
            if (!status.enabled || !status.loginUsername || !status.jellyfinUsername) {
                throw new Error("Kunini status response was incomplete");
            }

            sessionStorage.setItem("kunini-auth-target", status.jellyfinUsername);
            const observer = new MutationObserver(() => attemptLogin(status.loginUsername));
            observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true });
            window.addEventListener("hashchange", () => attemptLogin(status.loginUsername));
            attemptLogin(status.loginUsername);

            window.setTimeout(() => {
                observer.disconnect();
                if (!finished) {
                    showFailure();
                }
            }, startupTimeoutMs);
        } catch (error) {
            console.error("Kunini SSO failed", error);
            showFailure();
        }
    };

    void start();
})();
