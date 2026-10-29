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
    const searchSectionId = "kunini-discover-search-section";
    const externalDetailId = "kunini-discover-external-detail";
    const trailerModalId = "kunini-discover-trailer-modal";
    const sourceIdParameter = "kuniniSourceId";
    const mediaTypeParameter = "kuniniMediaType";
    const itemIdPattern = /^(?:[a-f\d]{32}|[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12})$/i;
    let completedRouteKey = null;
    let renderGeneration = 0;
    let refreshScheduled = false;

    const hashParameters = (hash) => {
        const queryStart = hash.indexOf("?");
        return queryStart === -1 ? null : new URLSearchParams(hash.slice(queryStart + 1));
    };

    const parseItemId = (hash) => {
        if (!hash.toLocaleLowerCase().startsWith("#/details")) {
            return null;
        }
        const itemId = hashParameters(hash)?.get("id");
        return itemId && itemIdPattern.test(itemId) ? itemId : null;
    };

    const parseSearchQuery = (hash) => {
        if (!hash.toLocaleLowerCase().startsWith("#/search")) {
            return null;
        }
        const query = hashParameters(hash)?.get("query")?.trim();
        return query && query.length >= 2 ? query : null;
    };

    const parseExternalRoute = (hash) => {
        const itemId = parseItemId(hash);
        const searchQuery = parseSearchQuery(hash);
        const parameters = hashParameters(hash);
        const sourceIdValue = parameters?.get(sourceIdParameter);
        const mediaTypeValue = parameters?.get(mediaTypeParameter)?.toLocaleLowerCase();
        if ((!itemId && !searchQuery)
            || !/^\d{1,10}$/.test(sourceIdValue || "")
            || !["movie", "series"].includes(mediaTypeValue)) {
            return null;
        }

        const sourceId = Number(sourceIdValue);
        return sourceId > 0
            ? { itemId, searchQuery, sourceId, mediaType: mediaTypeValue === "movie" ? "Movie" : "Series" }
            : null;
    };

    const buildExternalDetailHash = (hash, item) => {
        const parameters = hashParameters(hash) || new URLSearchParams();
        parameters.set("id", parseItemId(hash) || "");
        parameters.set(sourceIdParameter, String(item.sourceId));
        parameters.set(mediaTypeParameter, item.mediaType);
        return `#/details?${parameters.toString()}`;
    };

    const buildExternalSearchHash = (hash, item) => {
        const parameters = hashParameters(hash) || new URLSearchParams();
        parameters.set(sourceIdParameter, String(item.sourceId));
        parameters.set(mediaTypeParameter, item.mediaType);
        return `#/search?${parameters.toString()}`;
    };

    const stripExternalDetailHash = (hash) => {
        const parameters = hashParameters(hash) || new URLSearchParams();
        parameters.delete(sourceIdParameter);
        parameters.delete(mediaTypeParameter);
        const queryStart = hash.indexOf("?");
        const route = queryStart === -1 ? hash : hash.slice(0, queryStart);
        return `${route}?${parameters.toString()}`;
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

    const createLink = (document, className, text, href, label, newTab = true) => {
        const link = createElement(document, "a", className, text);
        link.href = href;
        if (newTab) {
            link.target = "_blank";
            link.rel = "noopener noreferrer";
        }
        link.setAttribute("aria-label", label);
        return link;
    };

    const createImage = (document, className, source, alternative, fallbackText) => {
        const image = createElement(document, "img", className);
        image.src = source;
        image.alt = alternative;
        image.decoding = "async";
        image.addEventListener("error", () => {
            image.replaceWith(createElement(document, "div", `${className}-fallback`, fallbackText));
        }, { once: true });
        return image;
    };

    const createPlayIcon = (document) => {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        path.setAttribute("d", "M8 5v14l11-7z");
        path.setAttribute("fill", "currentColor");
        svg.append(path);
        return svg;
    };

    const readTrailerUrls = (details) => {
        try {
            const watchUrl = new URL(details?.trailerWatchUrl);
            const embedUrl = new URL(details?.trailerEmbedUrl);
            const proxyUrl = new URL(details?.trailerProxyUrl, "https://kunini.invalid");
            const watchKey = watchUrl.searchParams.get("v");
            const embedMatch = /^\/embed\/([A-Za-z0-9_-]{6,20})$/.exec(embedUrl.pathname);
            const proxyMatch = /^\/KuniniDiscover\/trailer\/([A-Za-z0-9_-]{11})$/.exec(proxyUrl.pathname);
            const isTrustedWatch = watchUrl.protocol === "https:"
                && watchUrl.hostname === "www.youtube.com"
                && watchUrl.port === ""
                && watchUrl.username === ""
                && watchUrl.password === ""
                && watchUrl.pathname === "/watch"
                && /^[A-Za-z0-9_-]{6,20}$/.test(watchKey || "");
            const isTrustedEmbed = embedUrl.protocol === "https:"
                && embedUrl.hostname === "www.youtube-nocookie.com"
                && embedUrl.port === ""
                && embedUrl.username === ""
                && embedUrl.password === ""
                && embedMatch?.[1] === watchKey;
            const isTrustedProxy = proxyUrl.origin === "https://kunini.invalid"
                && proxyUrl.username === ""
                && proxyUrl.password === ""
                && proxyMatch?.[1] === watchKey
                && /^\d{10,12}$/.test(proxyUrl.searchParams.get("expires") || "")
                && /^[A-Za-z0-9_-]{43}$/.test(proxyUrl.searchParams.get("signature") || "")
                && [...proxyUrl.searchParams.keys()].sort().join(",") === "expires,signature";
            return isTrustedWatch && isTrustedEmbed && isTrustedProxy
                ? {
                    watchUrl: `https://www.youtube.com/watch?v=${watchKey}`,
                    proxyUrl: `${proxyUrl.pathname}${proxyUrl.search}`
                }
                : null;
        } catch {
            return null;
        }
    };

    const closeTrailerModal = (document) => {
        const modal = document.getElementById(trailerModalId);
        if (!modal) {
            return;
        }

        document.removeEventListener("keydown", modal.kuniniKeydownHandler);
        const video = modal.querySelector("video");
        video?.pause();
        video?.removeAttribute("src");
        video?.load();
        modal.remove();
        document.body.classList.remove("kunini-discover-trailer-open");
        if (modal.kuniniReturnFocus?.isConnected) {
            modal.kuniniReturnFocus.focus();
        }
    };

    const openTrailerModal = (window, details, trailer, opener) => {
        const document = window.document;
        closeTrailerModal(document);

        const modal = createElement(document, "div", "kunini-discover-trailer-modal");
        modal.id = trailerModalId;
        const dialog = createElement(document, "section", "kunini-discover-trailer-dialog");
        dialog.setAttribute("role", "dialog");
        dialog.setAttribute("aria-modal", "true");
        dialog.setAttribute("aria-labelledby", "kunini-discover-trailer-title");

        const header = createElement(document, "header", "kunini-discover-trailer-header");
        const title = createElement(document, "h2", "kunini-discover-trailer-title", "Official trailer");
        title.id = "kunini-discover-trailer-title";
        const closeButton = createElement(document, "button", "kunini-discover-trailer-close");
        closeButton.type = "button";
        closeButton.setAttribute("aria-label", "Close trailer");
        closeButton.textContent = "×";
        header.append(title, closeButton);

        const player = createElement(document, "div", "kunini-discover-trailer-player");
        const video = createElement(document, "video", "kunini-discover-trailer-frame");
        video.src = trailer.proxyUrl;
        video.title = `${details.title} official trailer`;
        video.controls = true;
        video.preload = "none";
        video.playsInline = true;
        video.setAttribute("controlsList", "nodownload");
        player.append(video);

        const footer = createElement(document, "footer", "kunini-discover-trailer-footer");
        footer.append(
            createElement(document, "span", "kunini-discover-trailer-proxy-status", "STREAMED THROUGH SOCKS PROXY"),
            createLink(
                document,
                "kunini-discover-trailer-external",
                "Open on YouTube",
                trailer.watchUrl,
                `Open ${details.title} trailer on YouTube`
            )
        );
        dialog.append(header, player, footer);
        modal.append(dialog);

        const close = () => closeTrailerModal(document);
        closeButton.addEventListener("click", close);
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                close();
            }
        });
        modal.kuniniKeydownHandler = (event) => {
            if (event.key === "Escape") {
                event.preventDefault();
                close();
                return;
            }
            if (event.key !== "Tab") {
                return;
            }

            const focusable = [...dialog.querySelectorAll("button, video, a[href]")];
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };
        modal.kuniniReturnFocus = opener;
        document.addEventListener("keydown", modal.kuniniKeydownHandler);
        document.body.append(modal);
        document.body.classList.add("kunini-discover-trailer-open");
        closeButton.focus({ preventScroll: true });
    };

    const createCard = (document, item, itemId, currentHash) => {
        const card = createElement(document, "article", "kunini-discover-card");
        card.tabIndex = 0;
        card.setAttribute("role", "listitem");
        card.setAttribute("aria-label", `${item.title} (${item.year}), not in library`);

        const poster = createElement(document, "div", "kunini-discover-poster");
        if (item.posterUrl) {
            const image = createImage(document, "kunini-discover-image", item.posterUrl, `${item.title} poster`, item.title);
            image.loading = "lazy";
            poster.append(image);
        } else {
            poster.append(createElement(document, "div", "kunini-discover-image-fallback", item.title));
        }

        const magnetUrl = buildMagnetUrl(item.title, item.year);
        const detailHash = parseSearchQuery(currentHash)
            ? buildExternalSearchHash(currentHash, item)
            : buildExternalDetailHash(currentHash || `#/details?id=${itemId}`, item);
        const overlay = createElement(document, "div", "kunini-discover-overlay");
        if (item.matchPercent > 0) {
            overlay.append(createElement(document, "strong", "kunini-discover-match", `${item.matchPercent}% match`));
        }
        overlay.append(createElement(document, "span", "kunini-discover-genre", item.genre));
        const overlayActions = createElement(document, "div", "kunini-discover-overlay-actions");
        overlayActions.append(
            createLink(document, "kunini-discover-details", "View details", detailHash, `View ${item.title} details`, false)
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

    const placeDiscoverSection = (container, section) => {
        const similar = container.querySelector("#similarCollapsible, .similarCollapsible");
        if (similar) {
            if (similar.nextElementSibling !== section) {
                similar.after(section);
            }
            return;
        }
        if (container.lastElementChild !== section) {
            container.append(section);
        }
    };

    const render = (document, container, itemId, response, currentHash, onNext = null) => {
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
        const headingText = response.heading || "Discover beyond your library";
        const subtitleText = response.subtitle || "Suggestions from free public catalogs • Not stored on this server";
        const heading = createElement(document, "h2", "kunini-discover-heading", headingText);
        heading.id = "kunini-discover-heading";
        copy.append(
            heading,
            createElement(document, "p", "kunini-discover-subtitle", subtitleText)
        );
        const sources = createElement(document, "div", "kunini-discover-sources");
        const source = createElement(document, "span", "kunini-discover-source", response.source || "TMDb");
        source.title = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
        if (response.hasMore && typeof onNext === "function") {
            const next = createElement(document, "button", "kunini-discover-next", "NEXT →");
            next.type = "button";
            next.setAttribute("aria-label", `Load the next page of ${headingText}`);
            next.addEventListener("click", async () => {
                next.disabled = true;
                next.setAttribute("aria-busy", "true");
                next.textContent = "LOADING…";
                try {
                    await onNext();
                } catch (error) {
                    console.warn("Kunini Discover could not load the next recommendation page", error);
                } finally {
                    next.disabled = false;
                    next.removeAttribute("aria-busy");
                    next.textContent = "NEXT →";
                }
            });
            sources.append(next);
        }
        sources.append(source);
        header.append(copy, sources);

        const rail = createElement(document, "div", "kunini-discover-rail");
        rail.setAttribute("role", "list");
        rail.setAttribute("aria-label", headingText);
        response.items.forEach((item) => rail.append(createCard(document, item, itemId, currentHash)));
        section.append(header, rail);
        placeDiscoverSection(container, section);
    };

    const placeSearchSection = (page, section) => {
        const localResults = page.querySelector(".searchResults");
        const emptyMessage = page.querySelector(".noItemsMessage");
        const anchor = localResults || emptyMessage;
        if (anchor) {
            if (anchor.nextElementSibling !== section) {
                anchor.after(section);
            }
            return;
        }
        if (page.lastElementChild !== section) {
            page.append(section);
        }
    };

    const renderSearch = (document, page, query, response, currentHash) => {
        document.getElementById(searchSectionId)?.remove();
        const items = response?.items || [];
        page.classList.toggle("kunini-discover-search-has-results", items.length > 0);
        if (!items.length) {
            return;
        }

        const section = createElement(document, "section", "kunini-discover-section kunini-discover-search-section verticalSection");
        section.id = searchSectionId;
        section.dataset.query = query;
        section.setAttribute("aria-labelledby", "kunini-discover-search-heading");

        const header = createElement(document, "div", "kunini-discover-header");
        const copy = createElement(document, "div", "kunini-discover-copy");
        const heading = createElement(document, "h2", "kunini-discover-heading", "Not in your library");
        heading.id = "kunini-discover-search-heading";
        copy.append(
            heading,
            createElement(document, "p", "kunini-discover-subtitle", `External matches for “${query}”`)
        );
        const sources = createElement(document, "div", "kunini-discover-sources");
        const source = createElement(document, "span", "kunini-discover-source", response.source || "TMDb");
        source.title = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
        sources.append(source);
        header.append(copy, sources);

        const rail = createElement(document, "div", "kunini-discover-rail");
        rail.setAttribute("role", "list");
        rail.setAttribute("aria-label", `External search matches for ${query}`);
        items.forEach((item) => rail.append(createCard(document, item, null, currentHash)));
        section.append(header, rail);
        placeSearchSection(page, section);
    };

    const formatRuntime = (minutes) => {
        if (!Number.isInteger(minutes) || minutes <= 0) {
            return null;
        }
        const hours = Math.floor(minutes / 60);
        const remainder = minutes % 60;
        return hours > 0 ? `${hours}h${remainder ? ` ${remainder}m` : ""}` : `${remainder}m`;
    };

    const addDetailFact = (document, list, label, value) => {
        if (!value) {
            return;
        }
        list.append(
            createElement(document, "dt", "kunini-discover-external-fact-label", label),
            createElement(document, "dd", "kunini-discover-external-fact-value", value)
        );
    };

    const cleanupExternalDetail = (document) => {
        closeTrailerModal(document);
        document.getElementById(externalDetailId)?.remove();
        document.querySelectorAll(".kunini-discover-external-active")
            .forEach((page) => page.classList.remove("kunini-discover-external-active"));
    };

    const renderExternalDetail = (window, page, details, returnHash, returnLabel) => {
        const document = window.document;
        cleanupExternalDetail(document);
        document.getElementById(sectionId)?.remove();
        page.classList.add("kunini-discover-external-active");

        const section = createElement(document, "section", "kunini-discover-external-detail");
        section.id = externalDetailId;
        section.setAttribute("aria-labelledby", "kunini-discover-external-title");

        const hero = createElement(document, "div", "kunini-discover-external-hero");
        if (details.backdropUrl) {
            const backdrop = createImage(document, "kunini-discover-external-backdrop", details.backdropUrl, "", "");
            backdrop.setAttribute("aria-hidden", "true");
            hero.append(backdrop);
        }
        hero.append(createElement(document, "div", "kunini-discover-external-backdrop-shade"));
        hero.append(createLink(document, "kunini-discover-external-back", returnLabel, returnHash, returnLabel, false));

        const content = createElement(document, "div", "kunini-discover-external-content");
        const poster = createElement(document, "div", "kunini-discover-external-poster");
        if (details.posterUrl) {
            poster.append(createImage(document, "kunini-discover-external-poster-image", details.posterUrl, `${details.title} poster`, details.title));
        } else {
            poster.append(createElement(document, "div", "kunini-discover-external-poster-image-fallback", details.title));
        }

        const copy = createElement(document, "div", "kunini-discover-external-copy");
        const title = createElement(document, "h1", "kunini-discover-external-title", details.title);
        title.id = "kunini-discover-external-title";
        title.tabIndex = -1;
        copy.append(title);
        if (details.originalTitle) {
            copy.append(createElement(document, "p", "kunini-discover-external-original-title", details.originalTitle));
        }

        const meta = createElement(document, "div", "kunini-discover-external-meta");
        const runtime = formatRuntime(details.runtimeMinutes);
        const metaValues = [
            String(details.year),
            runtime,
            details.seasonCount ? `${details.seasonCount} season${details.seasonCount === 1 ? "" : "s"}` : null,
            details.certification,
            details.rating > 0 ? `★ ${Number(details.rating).toFixed(1)}` : null,
            details.voteCount > 0 ? `${new Intl.NumberFormat().format(details.voteCount)} votes` : null
        ].filter(Boolean);
        metaValues.forEach((value) => meta.append(createElement(document, "span", "kunini-discover-external-meta-item", value)));
        copy.append(meta);

        const actions = createElement(document, "div", "kunini-discover-external-actions");
        actions.append(createLink(
            document,
            "kunini-discover-external-magnet",
            "MAGNET",
            buildMagnetUrl(details.title, details.year),
            `Search magnet for ${details.title} (${details.year})`
        ));
        const trailer = readTrailerUrls(details);
        if (trailer) {
            const trailerButton = createElement(document, "button", "kunini-discover-external-trailer");
            trailerButton.type = "button";
            trailerButton.setAttribute("aria-label", `Watch official trailer for ${details.title}`);
            trailerButton.append(createPlayIcon(document), createElement(document, "span", null, "TRAILER"));
            trailerButton.addEventListener("click", () => openTrailerModal(window, details, trailer, trailerButton));
            actions.append(trailerButton);
        }
        actions.append(createElement(document, "span", "kunini-discover-external-status", "NOT IN LIBRARY"));
        copy.append(actions);

        if (details.tagline) {
            copy.append(createElement(document, "h2", "kunini-discover-external-tagline", details.tagline));
        }
        copy.append(createElement(
            document,
            "p",
            "kunini-discover-external-overview",
            details.overview || "No synopsis is available."
        ));

        const facts = createElement(document, "dl", "kunini-discover-external-facts");
        addDetailFact(document, facts, "Genres", details.genres?.join(", "));
        addDetailFact(document, facts, "Release", details.releaseDate);
        addDetailFact(document, facts, "Status", details.status);
        addDetailFact(document, facts, "Metadata", "TMDb · External title");
        copy.append(facts);

        content.append(poster, copy);
        section.append(hero, content);
        page.append(section);
        page.scrollTop = 0;
        if (document.scrollingElement) {
            document.scrollingElement.scrollTop = 0;
        }
        title.focus({ preventScroll: true });
    };

    const renderExternalError = (document, page, returnHash, returnLabel) => {
        cleanupExternalDetail(document);
        page.classList.add("kunini-discover-external-active");
        const section = createElement(document, "section", "kunini-discover-external-detail kunini-discover-external-error");
        section.id = externalDetailId;
        section.append(
            createElement(document, "h1", "kunini-discover-external-title", "Details unavailable"),
            createElement(document, "p", "kunini-discover-external-overview", "This external title could not be loaded right now."),
            createLink(document, "kunini-discover-external-back", returnLabel, returnHash, returnLabel, false)
        );
        page.append(section);
    };

    const refresh = async (window) => {
        const hash = window.location.hash;
        const itemId = parseItemId(hash);
        const searchQuery = parseSearchQuery(hash);
        const externalRoute = parseExternalRoute(hash);
        const document = window.document;
        const apiClient = window.ApiClient;
        if (externalRoute) {
            document.getElementById(sectionId)?.remove();
            document.getElementById(searchSectionId)?.remove();
            const page = externalRoute.searchQuery
                ? document.querySelector("#searchPage")
                : document.querySelector(".itemDetailPage:not(.hide)") || document.querySelector(".itemDetailPage");
            if (!page || !apiClient?.ajax || !apiClient?.getUrl) {
                return;
            }
            const routeKey = `external:${externalRoute.searchQuery ? "search" : "library"}:${externalRoute.mediaType}:${externalRoute.sourceId}`;
            if (completedRouteKey === routeKey && document.getElementById(externalDetailId)) {
                return;
            }

            completedRouteKey = routeKey;
            const generation = ++renderGeneration;
            const returnHash = stripExternalDetailHash(hash);
            const returnLabel = externalRoute.searchQuery ? "Back to search results" : "Back to library item";
            try {
                const details = await apiClient.ajax({
                    type: "GET",
                    dataType: "json",
                    url: apiClient.getUrl("/KuniniDiscover/details", {
                        sourceId: externalRoute.sourceId,
                        mediaType: externalRoute.mediaType
                    })
                });
                if (generation === renderGeneration && parseExternalRoute(window.location.hash)?.sourceId === externalRoute.sourceId) {
                    renderExternalDetail(window, page, details, returnHash, returnLabel);
                }
            } catch (error) {
                console.warn("Kunini Discover could not load external details", error);
                if (generation === renderGeneration) {
                    renderExternalError(document, page, returnHash, returnLabel);
                }
            }
            return;
        }

        if (searchQuery) {
            cleanupExternalDetail(document);
            document.getElementById(sectionId)?.remove();
            const page = document.querySelector("#searchPage");
            const existingSection = document.getElementById(searchSectionId);
            if (page && existingSection) {
                page.classList.add("kunini-discover-search-has-results");
                placeSearchSection(page, existingSection);
            }
            const routeKey = `search:${searchQuery.toLocaleLowerCase()}`;
            if (completedRouteKey === routeKey && !existingSection) {
                completedRouteKey = null;
            }
            if (!page || !apiClient?.ajax || !apiClient?.getUrl || completedRouteKey === routeKey) {
                return;
            }

            completedRouteKey = routeKey;
            const generation = ++renderGeneration;
            try {
                const response = await apiClient.ajax({
                    type: "GET",
                    dataType: "json",
                    url: apiClient.getUrl("/KuniniDiscover/search", { query: searchQuery, limit: 12 })
                });
                if (generation === renderGeneration && parseSearchQuery(window.location.hash) === searchQuery) {
                    renderSearch(document, page, searchQuery, response, hash);
                }
            } catch (error) {
                completedRouteKey = null;
                console.warn("Kunini Discover could not load external search results", error);
            }
            return;
        }

        document.getElementById(searchSectionId)?.remove();
        document.querySelector("#searchPage")?.classList.remove("kunini-discover-search-has-results");
        if (!itemId) {
            completedRouteKey = null;
            cleanupExternalDetail(document);
            document.getElementById(sectionId)?.remove();
            return;
        }

        cleanupExternalDetail(document);
        const page = document.querySelector(".itemDetailPage:not(.hide)")
            || document.querySelector(".itemDetailPage");
        const container = document.querySelector(".itemDetailPage:not(.hide) .detailPageSecondaryContainer")
            || document.querySelector(".itemDetailPage .detailPageSecondaryContainer");
        const existingSection = document.getElementById(sectionId);
        if (container && existingSection) {
            placeDiscoverSection(container, existingSection);
        }

        const routeKey = `library:${itemId}`;
        if (!container || !apiClient?.ajax || !apiClient?.getUrl || completedRouteKey === routeKey) {
            return;
        }

        completedRouteKey = routeKey;
        const generation = ++renderGeneration;
        const loadRecommendationPage = async (pageNumber, focusFirstCard = false) => {
            const response = await apiClient.ajax({
                type: "GET",
                dataType: "json",
                url: apiClient.getUrl("/KuniniDiscover/recommendations", { itemId, limit: 20, page: pageNumber })
            });
            if (generation === renderGeneration && parseItemId(window.location.hash) === itemId) {
                const currentPage = Number.isInteger(response.page) && response.page > 0 ? response.page : pageNumber;
                render(
                    document,
                    container,
                    itemId,
                    response,
                    hash,
                    response.hasMore ? () => loadRecommendationPage(currentPage + 1, true) : null
                );
                if (focusFirstCard) {
                    document.querySelector(`#${sectionId} .kunini-discover-card`)?.focus({ preventScroll: true });
                }
            }
        };
        try {
            await loadRecommendationPage(1);
        } catch (error) {
            completedRouteKey = null;
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
            completedRouteKey = null;
            renderGeneration += 1;
            scheduleRefresh(window);
        });
        scheduleRefresh(window);
    };

    return {
        buildExternalDetailHash,
        buildExternalSearchHash,
        buildMagnetUrl,
        createCard,
        formatRuntime,
        parseExternalRoute,
        parseItemId,
        parseSearchQuery,
        readTrailerUrls,
        render,
        start,
        stripExternalDetailHash
    };
});
