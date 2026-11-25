const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../Resources/Calls/room.js"), "utf8");
const callId = "aaaaaaaa-bbbb-4ccc-dddd-eeeeeeeeeeee";

async function connect(getDisplayMedia, canShare = true) {
    let handleMessage;
    let connected;
    let disconnects = 0;
    let cameraStarted = false;
    let screenPublications = 0;
    const ready = new Promise(resolve => { connected = resolve; });
    const screenButton = {
        addEventListener(_, handler) { this.click = handler; },
        setAttribute() {}
    };
    class Room {
        remoteParticipants = new Map();
        localParticipant = {
            async setMicrophoneEnabled() {},
            async setCameraEnabled(enabled) { cameraStarted = enabled; },
            async publishTrack() { screenPublications++; },
            getTrackPublication() { return null; }
        };
        on() { return this; }
        async connect() {}
        async startAudio() {}
        async disconnect() { disconnects++; }
    }
    const webview = {
        addEventListener(_, handler) { handleMessage = handler; },
        postMessage({ type }) { if (type === "connected") connected(); }
    };
    vm.runInNewContext(script, {
        window: {
            chrome: { webview },
            LivekitClient: { Room, RoomEvent: {}, Track: { Source: { ScreenShare: "screen" } }, setLogLevel() {} },
            addEventListener() {}
        },
        document: { documentElement: {}, getElementById: () => screenButton },
        navigator: { mediaDevices: { getDisplayMedia, async enumerateDevices() { return []; } } }
    });
    handleMessage({ data: { type: "connect", call_id: callId, url: "wss://example.test", token: "test",
        video: true, canShare, audioQuality: 0, lang: "en" } });
    await ready;
    assert.equal(cameraStarted, true);
    return {
        click: () => screenButton.click(),
        disconnect: () => handleMessage({ data: { type: "disconnect", call_id: callId } }),
        disconnects: () => disconnects,
        publications: () => screenPublications
    };
}

function display() {
    let stops = 0;
    const track = {
        readyState: "live",
        stop() { stops++; },
        addEventListener() {}
    };
    return {
        displayStream: { getVideoTracks: () => [track], getTracks: () => [track] },
        stops: () => stops
    };
}

test("screen capture starts in the click handler and disconnect releases the display track", async () => {
    const capture = display();
    let pickerOpen = false;
    const room = await connect(() => {
        pickerOpen = true;
        return Promise.resolve(capture.displayStream);
    });
    const sharing = room.click();
    assert.equal(pickerOpen, true);
    await sharing;
    assert.equal(room.publications(), 1);
    room.disconnect();
    assert.equal(capture.stops(), 1);
    assert.equal(room.disconnects(), 1);
});

test("screen selection completing after hangup is stopped before publication", async () => {
    const capture = display();
    let choose;
    const picker = new Promise(resolve => { choose = resolve; });
    const room = await connect(() => picker);
    const sharing = room.click();
    room.disconnect();
    choose(capture.displayStream);
    await sharing;
    assert.equal(capture.stops(), 1);
    assert.equal(room.publications(), 0);
});

test("screen capture stays disabled when the host cannot verify its origin", async () => {
    const room = await connect(() => assert.fail("screen picker must stay closed"), false);
    await room.click();
    assert.equal(room.publications(), 0);
    room.disconnect();
});
