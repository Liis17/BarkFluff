/* Loads the current fingerprinted messenger bundle after runtime configuration scripts. */
(function () {
    "use strict";

    function loadBundle(manifest) {
        if (!manifest || typeof manifest.src !== "string") {
            throw new Error("Invalid messenger app manifest");
        }
        var script = document.createElement("script");
        script.async = false;
        script.src = manifest.src;
        script.onerror = function () {
            console.error("Could not load the messenger application bundle");
        };
        document.head.appendChild(script);
    }

    fetch("/js/app/app-manifest.json", { cache: "no-store" })
        .then(function (response) {
            if (!response.ok)
                throw new Error("Could not load the messenger app manifest");
            return response.json();
        })
        .then(loadBundle)
        .catch(function (error) {
            console.error(error);
        });
})();
