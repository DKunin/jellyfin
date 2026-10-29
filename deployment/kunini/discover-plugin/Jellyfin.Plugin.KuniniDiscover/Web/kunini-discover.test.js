// SPDX-License-Identifier: GPL-2.0-only

const assert = require("node:assert/strict");
const test = require("node:test");
const discover = require("./kunini-discover.js");

test("builds magnet search with the title and release year", () => {
    assert.equal(
        discover.buildMagnetUrl("Blade Runner 2049", 2017),
        "https://magnet.kunini.ru/?query=Blade%20Runner%202049%202017"
    );
    assert.equal(
        discover.buildMagnetUrl("Три богатыря", 2025),
        "https://magnet.kunini.ru/?query=%D0%A2%D1%80%D0%B8%20%D0%B1%D0%BE%D0%B3%D0%B0%D1%82%D1%8B%D1%80%D1%8F%202025"
    );
});

test("reads only Jellyfin detail-page item identifiers", () => {
    const itemId = "3cf2cc5c1467441a9f8bb42babc2eb7d";
    assert.equal(discover.parseItemId(`#/details?id=${itemId}&serverId=test`), itemId);
    assert.equal(discover.parseItemId(`#/home?id=${itemId}`), null);
    assert.equal(discover.parseItemId("#/details?id=not-an-id"), null);
});

test("builds and parses same-origin external detail routes", () => {
    const itemId = "3cf2cc5c1467441a9f8bb42babc2eb7d";
    const originalHash = `#/details?id=${itemId}&serverId=test`;
    const externalHash = discover.buildExternalDetailHash(originalHash, { sourceId: 438631, mediaType: "Movie" });
    assert.equal(
        externalHash,
        `#/details?id=${itemId}&serverId=test&kuniniSourceId=438631&kuniniMediaType=Movie`
    );
    assert.deepEqual(discover.parseExternalRoute(externalHash), { itemId, sourceId: 438631, mediaType: "Movie" });
    assert.equal(discover.stripExternalDetailHash(externalHash), originalHash);
    assert.equal(discover.parseExternalRoute(`${originalHash}&kuniniSourceId=bad&kuniniMediaType=Movie`), null);
});

test("formats external runtimes like Jellyfin", () => {
    assert.equal(discover.formatRuntime(128), "2h 8m");
    assert.equal(discover.formatRuntime(120), "2h");
    assert.equal(discover.formatRuntime(42), "42m");
    assert.equal(discover.formatRuntime(null), null);
});
