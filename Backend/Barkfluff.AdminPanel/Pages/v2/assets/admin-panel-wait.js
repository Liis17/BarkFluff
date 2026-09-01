(function () {
    const checkIntervalMs = 3000;
    const requiredSuccessCount = 3;

    function start({ initialDelayMs, pageServerStartedAt }) {
        let seconds = 0;
        let successCount = 0;
        let checkInProgress = false;
        let redirectStarted = false;
        const timerEl = document.getElementById('timer');

        setInterval(() => {
            seconds++;
            if (timerEl) timerEl.textContent = seconds;
        }, 1000);

        async function checkServer() {
            if (checkInProgress || redirectStarted) return;
            checkInProgress = true;

            try {
                const response = await fetch('/services', {
                    cache: 'no-store',
                    credentials: 'same-origin',
                    redirect: 'manual'
                });
                const serverStartedAt = response.headers.get('X-Admin-Panel-Started-At');

                if (response.status === 200 && serverStartedAt && serverStartedAt !== pageServerStartedAt) {
                    successCount++;
                    if (successCount >= requiredSuccessCount) {
                        redirectStarted = true;
                        window.location.replace('/services');
                    }
                } else {
                    successCount = 0;
                }
            } catch {
                successCount = 0;
            } finally {
                checkInProgress = false;
                if (!redirectStarted) setTimeout(checkServer, checkIntervalMs);
            }
        }

        setTimeout(checkServer, initialDelayMs);
    }

    window.AdminPanelWait = { start };
})();
