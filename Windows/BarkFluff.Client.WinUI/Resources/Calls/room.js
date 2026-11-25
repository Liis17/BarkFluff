(() => {
    "use strict";

    const bridge = window.chrome.webview;
    const sdk = window.LivekitClient;
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
    const share = document.getElementById("share");
    const tiles = document.getElementById("tiles");
    const tileByTrack = new Map();
    const audioBitrate = [0, 14000, 24000, 48000];
    const videoPreset = [null, [640, 360, 24, 400000], [960, 540, 25, 1000000], [1280, 720, 30, 1700000]];
    const labels = {
        en: { share: "Share screen", stop: "Stop sharing", you: "You", user: "User", screen: "Screen" },
        ru: { share: "Показать экран", stop: "Остановить показ", you: "Вы", user: "Пользователь", screen: "Экран" }
    };
    let lang = "en";
    let callId;
    let room;
    let commands = Promise.resolve();
    let displayStream;
    let audioQuality = 0;
    let videoQuality = 0;

    function send(type, fields = {}) {
        if (callId) bridge.postMessage({ type, call_id: callId, ...fields });
    }

    function showVideo(track, publication, participant, local) {
        if (tileByTrack.has(track) || publication.isMuted) return;
        const figure = document.createElement("figure");
        const caption = document.createElement("figcaption");
        const text = labels[lang];
        const identity = /^[1-9][0-9]{0,18}$/.test(participant.identity) ? participant.identity : "";
        const name = local ? text.you : [text.user, identity].filter(Boolean).join(" ");
        caption.textContent = publication.source === sdk.Track.Source.ScreenShare ? name + " · " + text.screen : name;
        const element = track.attach();
        element.autoplay = true;
        element.playsInline = true;
        element.muted = true;
        figure.append(element, caption);
        tiles.append(figure);
        tileByTrack.set(track, figure);
        if (tileByTrack.size === 1) send("video_visible");
    }

    function attach(track, publication, participant) {
        if (track.kind === "video") {
            showVideo(track, publication, participant, false);
        } else if (track.kind === "audio") {
            const element = track.attach();
            element.autoplay = true;
            document.body.append(element);
        }
    }

    function detach(track) {
        for (const element of track.detach()) element.remove();
        tileByTrack.get(track)?.remove();
        if (tileByTrack.delete(track) && tileByTrack.size === 0) send("video_hidden");
    }

    function participants() {
        send("participants", { participants: [...room.remoteParticipants.keys()]
            .filter(id => /^[1-9][0-9]{0,18}$/.test(id)).slice(0, 256) });
    }

    async function devices() {
        const list = await navigator.mediaDevices.enumerateDevices();
        send("devices", { devices: list.filter(device =>
            device.deviceId && device.deviceId.length <= 512 && ["audioinput", "audiooutput", "videoinput"].includes(device.kind)
        ).slice(0, 128).map(device => ({
            id: device.deviceId, label: device.label.slice(0, 512), kind: device.kind
        })) });
    }

    function mic(enabled) {
        const bitrate = audioBitrate[audioQuality];
        return room.localParticipant.setMicrophoneEnabled(enabled, undefined,
            bitrate ? { audioPreset: { maxBitrate: bitrate } } : undefined);
    }

    async function camera(enabled) {
        const preset = videoPreset[videoQuality];
        const capture = preset ? { resolution: { width: preset[0], height: preset[1], frameRate: preset[2] } } : undefined;
        const publish = preset ? { videoEncoding: { maxBitrate: preset[3], maxFramerate: preset[2] }, simulcast: false } : undefined;
        if (!enabled) {
            await unpublish(sdk.Track.Source.Camera);
        } else {
            await room.localParticipant.setCameraEnabled(true, capture, publish);
        }
        send(enabled ? "camera_on" : "camera_off");
        await devices();
    }

    async function unpublish(source) {
        const track = room.localParticipant.getTrackPublication(source)?.track;
        if (track) await room.localParticipant.unpublishTrack(track, true);
    }

    function renderShare() {
        share.textContent = displayStream ? labels[lang].stop : labels[lang].share;
        share.setAttribute("aria-pressed", String(!!displayStream));
        share.disabled = false;
    }

    async function stopSharing() {
        const stream = displayStream;
        displayStream = undefined;
        stream?.getTracks().forEach(track => track.stop());
        await unpublish(sdk.Track.Source.ScreenShare);
        send("screen_off");
    }

    share.addEventListener("click", async () => {
        if (!callId || share.hidden || share.disabled) return;
        share.disabled = true;
        try {
            if (displayStream) {
                await stopSharing();
            } else {
                const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
                if (!callId) {
                    stream.getTracks().forEach(track => track.stop());
                    return;
                }
                displayStream = stream;
                const track = stream.getVideoTracks()[0];
                await room.localParticipant.publishTrack(track, { source: sdk.Track.Source.ScreenShare });
                if (track.readyState === "ended") {
                    await stopSharing();
                } else {
                    track.addEventListener("ended", () => {
                        if (displayStream === stream) {
                            share.disabled = true;
                            stopSharing().catch(() => send("screen_error")).finally(renderShare);
                        }
                    }, { once: true });
                    send("screen_on");
                }
            }
        } catch (error) {
            await stopSharing().catch(() => {});
            if (error.name !== "NotAllowedError") send("screen_error");
        } finally {
            renderShare();
        }
    });

    async function connect(data) {
        if (!sdk) throw new Error();
        sdk.setLogLevel("silent");
        audioQuality = data.audioQuality;
        lang = data.lang;
        document.documentElement.lang = lang;
        const current = new sdk.Room({ adaptiveStream: false, dynacast: false });
        room = current;
        const events = sdk.RoomEvent;
        current.on(events.TrackSubscribed, attach)
            .on(events.TrackUnsubscribed, detach)
            .on(events.LocalTrackPublished, (publication, participant) => {
                if (publication.track?.kind === "video") showVideo(publication.track, publication, participant, true);
            })
            .on(events.LocalTrackUnpublished, publication => {
                if (publication.track) detach(publication.track);
            })
            .on(events.TrackMuted, publication => {
                if (publication.track?.kind === "video") detach(publication.track);
            })
            .on(events.TrackUnmuted, (publication, participant) => {
                if (publication.track?.kind === "video") showVideo(publication.track, publication, participant, participant.isLocal);
            })
            .on(events.ParticipantConnected, () => { send("participant_joined"); participants(); })
            .on(events.ParticipantDisconnected, () => { send("participant_left"); participants(); })
            .on(events.Reconnected, participants)
            .on(events.Disconnected, () => send("disconnected"))
            .on(events.MediaDevicesChanged, () => devices().catch(() => send("error")));
        await current.connect(data.url, data.token);
        await mic(true);
        await current.startAudio();
        if (data.video) await camera(true).catch(() => send("camera_error"));
        renderShare();
        share.hidden = !data.canShare || typeof navigator.mediaDevices.getDisplayMedia !== "function";
        send("connected");
        if (current.remoteParticipants.size > 0) send("participant_joined");
        participants();
        await devices();
    }

    function disconnect() {
        callId = undefined;
        displayStream?.getTracks().forEach(track => track.stop());
        displayStream = undefined;
        room?.disconnect().catch(() => {});
    }

    function enqueue(run, error = "error") {
        commands = commands.then(() => callId && run()).catch(() => send(error));
    }

    bridge.addEventListener("message", ({ data }) => {
        if (!data || typeof data !== "object" || typeof data.type !== "string" ||
            typeof data.call_id !== "string" || !guid.test(data.call_id)) return;

        const count = Object.keys(data).length;
        if (data.type === "connect") {
            if (callId || count !== 8 || typeof data.url !== "string" || data.url.length > 2048 ||
                !data.url.startsWith("wss://") || typeof data.token !== "string" ||
                !data.token || data.token.length > 16384 || typeof data.video !== "boolean" || typeof data.canShare !== "boolean" ||
                ![0, 1, 2, 3].includes(data.audioQuality) || !["ru", "en"].includes(data.lang)) return;
            callId = data.call_id;
            enqueue(() => connect(data));
            return;
        }
        if (!room || data.call_id !== callId) return;
        if (count === 2) {
            if (data.type === "disconnect") disconnect();
            else if (["mute", "unmute"].includes(data.type)) enqueue(() => mic(data.type === "unmute"));
            else if (["camera_on", "camera_off"].includes(data.type))
                enqueue(() => camera(data.type === "camera_on"), "camera_error");
        } else if (count === 3 && ["audio_quality", "video_quality"].includes(data.type) &&
            typeof data.deviceId === "string" && /^[0-3]$/.test(data.deviceId)) {
            enqueue(async () => {
                const quality = Number(data.deviceId);
                const local = room.localParticipant;
                if (data.type === "audio_quality" && audioQuality !== quality) {
                    audioQuality = quality;
                    const enabled = local.isMicrophoneEnabled;
                    await unpublish(sdk.Track.Source.Microphone);
                    if (enabled) await mic(true);
                } else if (data.type === "video_quality" && videoQuality !== quality) {
                    videoQuality = quality;
                    if (local.isCameraEnabled) {
                        await unpublish(sdk.Track.Source.Camera);
                        send("camera_off");
                        await camera(true);
                    }
                }
            }, "quality_error");
        } else if (count === 3 && ["audioinput", "audiooutput", "videoinput"].includes(data.type) &&
            typeof data.deviceId === "string" && data.deviceId.length > 0 && data.deviceId.length <= 512) {
            enqueue(async () => {
                if (!(await room.switchActiveDevice(data.type, data.deviceId))) throw new Error();
            }, data.type === "videoinput" ? "camera_error" : "error");
        }
    });

    window.addEventListener("pagehide", disconnect);
})();
