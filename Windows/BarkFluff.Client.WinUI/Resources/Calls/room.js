(() => {
    "use strict";

    const bridge = window.chrome.webview;
    const sdk = window.LivekitClient;
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
    let callId;
    let room;
    let commands = Promise.resolve();

    function send(type, fields = {}) {
        if (callId) bridge.postMessage({ type, call_id: callId, ...fields });
    }

    function attach(track) {
        if (track.kind !== "audio") return;
        const element = track.attach();
        element.autoplay = true;
        document.body.append(element);
    }

    function detach(track) {
        for (const element of track.detach()) element.remove();
    }

    async function devices() {
        const list = await navigator.mediaDevices.enumerateDevices();
        send("devices", { devices: list.filter(device =>
            device.deviceId && ["audioinput", "audiooutput"].includes(device.kind)
        ).slice(0, 128).map(device => ({
            id: device.deviceId, label: device.label.slice(0, 512), kind: device.kind
        })) });
    }

    async function connect(url, token) {
        if (!sdk) throw new Error();
        sdk.setLogLevel("silent");
        const current = new sdk.Room({ adaptiveStream: false, dynacast: false });
        room = current;
        const events = sdk.RoomEvent;
        current.on(events.TrackSubscribed, attach)
            .on(events.TrackUnsubscribed, detach)
            .on(events.ParticipantConnected, () => send("participant_joined"))
            .on(events.ParticipantDisconnected, () => send("participant_left"))
            .on(events.Disconnected, () => send("disconnected"))
            .on(events.MediaDevicesChanged, () => devices().catch(() => send("error")));
        await current.connect(url, token);
        await current.localParticipant.setMicrophoneEnabled(true);
        await current.startAudio();
        send("connected");
        if (current.remoteParticipants.size > 0) send("participant_joined");
        await devices();
    }

    bridge.addEventListener("message", ({ data }) => {
        if (!data || typeof data !== "object" || typeof data.type !== "string" ||
            typeof data.call_id !== "string" || !guid.test(data.call_id)) return;

        const count = Object.keys(data).length;
        if (data.type === "connect") {
            if (callId || count !== 4 || typeof data.url !== "string" ||
                !data.url.startsWith("wss://") || typeof data.token !== "string" ||
                !data.token || data.token.length > 16384) return;
            callId = data.call_id;
            commands = connect(data.url, data.token).catch(() => send("error"));
            return;
        }
        if (!room || data.call_id !== callId) return;
        if (data.type === "disconnect" && count === 2) {
            callId = undefined;
            room.disconnect().catch(() => {});
            return;
        }
        if (["mute", "unmute"].includes(data.type) && count === 2) {
            commands = commands.then(() => room.localParticipant.setMicrophoneEnabled(data.type === "unmute"))
                .catch(() => send("error"));
        } else if (["audioinput", "audiooutput"].includes(data.type) && count === 3 &&
            typeof data.deviceId === "string" && data.deviceId.length > 0 && data.deviceId.length <= 512) {
            commands = commands.then(async () => {
                if (!(await room.switchActiveDevice(data.type, data.deviceId))) throw new Error();
            }).catch(() => send("error"));
        }
    });

    window.addEventListener("pagehide", () => room?.disconnect().catch(() => {}));
})();
