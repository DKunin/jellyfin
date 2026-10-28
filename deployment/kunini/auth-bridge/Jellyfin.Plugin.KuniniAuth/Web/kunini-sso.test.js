// SPDX-License-Identifier: GPL-2.0-only

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "kunini-sso.js"), "utf8");

const createHarness = async () => {
    let formHidden = true;
    let hashChange;
    let observerCallback;
    let submitCount = 0;
    let guardRemoved = false;

    class FakeInput {
        set value(value) {
            this.currentValue = value;
        }

        get value() {
            return this.currentValue;
        }

        dispatchEvent() {}
    }

    const usernameInput = new FakeInput();
    const passwordInput = new FakeInput();
    const form = { classList: { contains: () => formHidden } };
    const submitButton = { click: () => { submitCount += 1; } };
    const guard = { remove: () => { guardRemoved = true; } };
    const appRoot = { childElementCount: 1 };
    const location = { hash: "#/home" };

    const document = {
        documentElement: {},
        getElementById(id) {
            return {
                "kunini-auth-guard": guard,
                reactRoot: appRoot,
                txtManualName: usernameInput,
                txtManualPassword: passwordInput
            }[id] ?? null;
        },
        querySelector(selector) {
            return {
                ".manualLoginForm": form,
                ".manualLoginForm button.button-submit": submitButton
            }[selector] ?? null;
        },
        createElement() {
            throw new Error("failure UI was not expected");
        }
    };

    const context = {
        console,
        crypto: { randomUUID: () => "test-password" },
        document,
        Event: class {},
        fetch: async () => ({
            ok: true,
            json: async () => ({
                enabled: true,
                jellyfinUsername: "kunini-dkunin-test",
                loginUsername: "KuniniAuth"
            })
        }),
        HTMLInputElement: FakeInput,
        MutationObserver: class {
            constructor(callback) {
                observerCallback = callback;
            }

            disconnect() {}
            observe() {}
        },
        sessionStorage: { setItem() {} },
        window: {
            addEventListener(type, callback) {
                if (type === "hashchange") {
                    hashChange = callback;
                }
            },
            location,
            setTimeout() {}
        }
    };

    vm.runInNewContext(source, context);
    await new Promise(setImmediate);

    return {
        enterLoginRoute() {
            formHidden = false;
            location.hash = "#/login?url=%2Fhome";
            hashChange();
            observerCallback();
        },
        get guardRemoved() { return guardRemoved; },
        get password() { return passwordInput.value; },
        get submitCount() { return submitCount; },
        get username() { return usernameInput.value; }
    };
};

test("continues SSO when Jellyfin redirects from its splash to login", async () => {
    const harness = await createHarness();

    assert.equal(harness.guardRemoved, true, "the splash may be revealed");
    harness.enterLoginRoute();

    assert.equal(harness.username, "KuniniAuth");
    assert.equal(harness.password, "test-password");
    assert.equal(harness.submitCount, 1);
});
