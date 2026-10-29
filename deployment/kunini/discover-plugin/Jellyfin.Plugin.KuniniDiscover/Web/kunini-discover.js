// SPDX-License-Identifier: GPL-2.0-only

(function (root, factory) {
    "use strict";
    const discover = factory();
    if (typeof module === "object" && module.exports) {
        module.exports = discover;
        return;
    }
    discover.start(root);
})(typeof window === "undefined" ? undefined : window, () => {
    "use strict";

    const sectionId = "kunini-discover-section";
    const itemIdPattern = /^(?:[a-f\d]{32}|[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12})$/i;
    let completedItemId = null;
    let renderGeneration = 0;
    let refreshScheduled = false;

    const parseItemId = (hash) => {
        if (!hash.toLocaleLowerCase().startsWith("#/details")) {
            return null;
        }
        const queryStart = hash.indexOf("?");
        if (queryStart === -1) {
            return null;
        }
        const itemId = new URLSearchParams(hash.slice(queryStart + 1)).get("id");
        return itemId && itemIdPattern.test(itemId) ? itemId : null;
    };

    const buildMagnetUrl = (title, year) => {
        const url = new URL("https://magnet.kunini.ru/");
        url.searchParams.set("query", `${title.trim()} ${year}`);
        return url.toString().replace(/\+/g, "%20");
    };

    const createElement = (document, tagName, className, text) => {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }
        if (text !== undefined) {
            element.textContent = text;
        }
        return element;
    };

    const createLink = (document, className, text, href, label) => {
        const link = createElement(document, "a", className, text);
        link.href = href;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        link.setAttribute("aria-label", label);
        return link;
    };

    const createCard = (document, item) => {
        const card = createElement(document, "article", "kunini-discover-card");
        card.tabIndex = 0;
        card.setAttribute("role", "listitem");
        card.setAttribute("aria-label", `${item.title} (${item.year}), not in library`);

        const poster = createElement(document, "div", "kunini-discover-poster");
        if (item.posterUrl) {
            const image = createElement(document, "img", "kunini-discover-image");
            image.src = item.posterUrl;
            image.alt = `${item.title} poster`;
            image.loading = "lazy";
            image.decoding = "async";
            image.addEventListener("error", () => {
                image.replaceWith(createElement(document, "div", "kunini-discover-poster-fallback", item.title));
            }, { once: true });
            poster.append(image);
        } else {
            poster.append(createElement(document, "div", "kunini-discover-poster-fallback", item.title));
        }

        const magnetUrl = buildMagnetUrl(item.title, item.year);
        const overlay = createElement(document, "div", "kunini-discover-overlay");
        overlay.append(
            createElement(document, "strong", "kunini-discover-match", `${item.matchPercent}% match`),
            createElement(document, "span", "kunini-discover-genre", item.genre)
        );
        const overlayActions = createElement(document, "div", "kunini-discover-overlay-actions");
        overlayActions.append(
            createLink(document, "kunini-discover-details", "View details", item.detailsUrl, `View ${item.title} on TMDb`),
            createLink(document, "kunini-discover-availability", "Search availability", magnetUrl, `Search for ${item.title} (${item.year})`)
        );
        overlay.append(overlayActions);

        const badges = createElement(document, "div", "kunini-discover-badges");
        badges.append(
            createElement(document, "span", "kunini-discover-status", "NOT IN LIBRARY"),
            createLink(document, "kunini-discover-magnet", "MAGNET", magnetUrl, `Search magnet for ${item.title} (${item.year})`)
        );
        poster.append(overlay, badges);

        const caption = createElement(document, "div", "kunini-discover-caption");
        caption.append(
            createElement(document, "h3", "kunini-discover-title", item.title),
            createElement(document, "span", "kunini-discover-year", String(item.year))
        );
        card.append(poster, caption);
        return card;
    };

    const render = (document, container, itemId, response) => {
        document.getElementById(sectionId)?.remove();
        if (!response?.items?.length) {
            return;
        }

        const section = createElement(document, "section", "kunini-discover-section verticalSection detailVerticalSection");
        section.id = sectionId;
        section.dataset.itemId = itemId;
        section.setAttribute("aria-labelledby", "kunini-discover-heading");

        const header = createElement(document, "div", "kunini-discover-header");
        const copy = createElement(document, "div", "kunini-discover-copy");
        const heading = createElement(document, "h2", "kunini-discover-heading", "Discover beyond your library");
        heading.id = "kunini-discover-heading";
        copy.append(
            heading,
            createElement(document, "p", "kunini-discover-subtitle", "Suggestions from free public catalogs • Not stored on this server")
        );
        const sources = createElement(document, "div", "kunini-discover-sources");
        const source = createElement(document, "span", "kunini-discover-source", response.source || "TMDb");
        source.title = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
        sources.append(source);
        header.append(copy, sources);

        const rail = createElement(document, "div", "kunini-discover-rail");
        rail.setAttribute("role", "list");
        rail.setAttribute("aria-label", "External recommendations");
        response.items.forEach((item) => rail.append(createCard(document, item)));
        section.append(header, rail);
        container.prepend(section);
    };

    const refresh = async (window) => {
        const itemId = parseItemId(window.location.hash);
        const document = window.document;
        if (!itemId) {
            completedItemId = null;
            document.getElementById(sectionId)?.remove();
            return;
        }

        const container = document.querySelector(".itemDetailPage:not(.hide) .detailPageSecondaryContainer")
            || document.querySelector(".itemDetailPage .detailPageSecondaryContainer");
        const apiClient = window.ApiClient;
        if (!container || !apiClient?.ajax || !apiClient?.getUrl || completedItemId === itemId) {
            return;
        }

        completedItemId = itemId;
        const generation = ++renderGeneration;
        try {
            const response = await apiClient.ajax({
                type: "GET",
                dataType: "json",
                url: apiClient.getUrl("/KuniniDiscover/recommendations", { itemId, limit: 12 })
            });
            if (generation === renderGeneration && parseItemId(window.location.hash) === itemId) {
                render(document, container, itemId, response);
            }
        } catch (error) {
            console.warn("Kunini Discover could not load recommendations", error);
        }
    };

    const scheduleRefresh = (window) => {
        if (refreshScheduled) {
            return;
        }
        refreshScheduled = true;
        window.requestAnimationFrame(() => {
            refreshScheduled = false;
            void refresh(window);
        });
    };

    const start = (window) => {
        if (!window?.document || window.__kuniniDiscoverStarted) {
            return;
        }
        window.__kuniniDiscoverStarted = true;
        const observer = new window.MutationObserver(() => scheduleRefresh(window));
        observer.observe(window.document.documentElement, { childList: true, subtree: true });
        window.addEventListener("hashchange", () => {
            completedItemId = null;
            renderGeneration += 1;
            scheduleRefresh(window);
        });
        scheduleRefresh(window);
    };

    return { buildMagnetUrl, createCard, parseItemId, render, start };
});
